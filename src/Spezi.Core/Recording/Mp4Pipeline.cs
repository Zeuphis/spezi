using Vortice.MediaFoundation;

namespace Spezi.Core.Recording;

internal sealed class ReplayBuffer
{
    readonly object _gate = new();
    readonly LinkedList<EncodedPacket> _video = new();
    readonly LinkedList<EncodedPacket> _audio = new();
    long _bytes;
    TimeSpan _window = TimeSpan.FromMinutes(2);
    long _maxBytes = 768L * 1024 * 1024;

    public void Configure(TimeSpan window, int bitrateKbps)
    {
        lock (_gate)
        {
            _window = window < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : window;
            var estimate = (long)bitrateKbps * 1000L / 8L * (long)_window.TotalSeconds;
            _maxBytes = Math.Clamp((long)(estimate * 1.35) + 32L * 1024 * 1024, 64L * 1024 * 1024, 1536L * 1024 * 1024);
            Trim();
        }
    }

    public void Add(EncodedPacket packet)
    {
        lock (_gate)
        {
            (packet.IsAudio ? _audio : _video).AddLast(packet);
            _bytes += packet.Data.Length;
            Trim();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _video.Clear();
            _audio.Clear();
            _bytes = 0;
        }
    }

    public (EncodedPacket[] Video, EncodedPacket[] Audio, TimeSpan Buffered) Snapshot()
    {
        lock (_gate)
        {
            return (_video.ToArray(), _audio.ToArray(), BufferedUnlocked());
        }
    }

    public TimeSpan Buffered
    {
        get { lock (_gate) return BufferedUnlocked(); }
    }

    public long Bytes
    {
        get { lock (_gate) return _bytes; }
    }

    TimeSpan BufferedUnlocked()
    {
        if (_video.Count == 0)
            return TimeSpan.Zero;
        var span = _video.Last!.Value.Timestamp - _video.First!.Value.Timestamp;
        return TimeSpan.FromTicks(Math.Max(0, span));
    }

    void Trim()
    {
        if (_video.Count == 0)
            return;
        var newest = _video.Last!.Value.Timestamp;
        var cutoff = newest - _window.Ticks;
        while (_video.Count > 0 && _video.First!.Value.Timestamp < cutoff)
            Drop(_video);
        while (_video.Count > 0 && !_video.First!.Value.IsKeyframe)
            Drop(_video);
        var start = _video.Count == 0 ? long.MaxValue : _video.First!.Value.Timestamp;
        while (_audio.Count > 0 && _audio.First!.Value.Timestamp < start)
            Drop(_audio);

        while (_bytes > _maxBytes && _video.Count > 1)
        {
            Drop(_video);
            while (_video.Count > 0 && !_video.First!.Value.IsKeyframe)
                Drop(_video);
            start = _video.Count == 0 ? long.MaxValue : _video.First!.Value.Timestamp;
            while (_audio.Count > 0 && _audio.First!.Value.Timestamp < start)
                Drop(_audio);
        }
    }

    void Drop(LinkedList<EncodedPacket> list)
    {
        var packet = list.First!.Value;
        _bytes -= packet.Data.Length;
        list.RemoveFirst();
    }
}

internal sealed class SegmentedSession : IDisposable
{
    readonly object _gate = new();
    readonly string _directory;
    readonly List<string> _parts = [];
    readonly PriorityQueue<EncodedPacket, (long Timestamp, int Audio)> _pendingPackets = new();
    long _newestVideo = long.MinValue;
    long _newestAudio = long.MinValue;
    IMFSinkWriter? _writer;
    IMFMediaType? _videoType;
    IMFMediaType? _audioType;
    int _videoStream = -1;
    int _audioStream = -1;
    long _segmentStart = long.MinValue;
    long _lastVideo = -1;
    long _lastAudio = -1;
    bool _open;
    bool _finished;
    string? _pendingPart;
    int _index;

    public string DirectoryPath => _directory;
    public string? GameTitle { get; private set; }

    public SegmentedSession(string directory, string? gameTitle = null)
    {
        _directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        SetGameTitle(gameTitle);
    }

    public void SetGameTitle(string? title)
    {
        lock (_gate)
        {
            if (_finished || GameTitle != null || string.IsNullOrWhiteSpace(title)) return;
            GameTitle = RecordingNames.Clean(title);
            File.WriteAllText(Path.Combine(_directory, "game-title.txt"), GameTitle);
        }
    }

    public void SetTypes(IMFMediaType videoType, IMFMediaType? audioType)
    {
        lock (_gate)
        {
            if (_finished)
                return;
            _videoType?.Dispose();
            _audioType?.Dispose();
            _videoType = CloneType(videoType);
            _audioType = audioType == null ? null : CloneType(audioType);
        }
    }

    static IMFMediaType CloneType(IMFMediaType source)
    {
        var copy = MediaFactory.MFCreateMediaType();
        source.CopyAllItems(copy);
        return copy;
    }

    public void Write(EncodedPacket packet)
    {
        lock (_gate)
        {
            if (_finished)
                return;
            if (packet.IsAudio)
                _newestAudio = Math.Max(_newestAudio, packet.Timestamp);
            else
                _newestVideo = Math.Max(_newestVideo, packet.Timestamp);
            _pendingPackets.Enqueue(packet, (packet.Timestamp, packet.IsAudio ? 1 : 0));
            // Audio and video arrive on different encoder threads. Rotate segments
            // only after older audio has arrived, rather than dropping it at the cut.
            var cutoff = _newestVideo;
            if (_audioType != null)
                cutoff = _newestAudio != long.MinValue && _newestVideo - _newestAudio < TimeSpan.TicksPerSecond
                    ? Math.Min(_newestVideo, _newestAudio)
                    : _newestVideo == long.MinValue ? long.MinValue : _newestVideo - TimeSpan.TicksPerMillisecond * 100;
            while (_pendingPackets.TryPeek(out _, out var priority) && priority.Timestamp <= cutoff)
                WriteUnlocked(_pendingPackets.Dequeue());
            // Keep failed or disconnected sources from growing the queue indefinitely.
            while (_pendingPackets.Count > 256)
                WriteUnlocked(_pendingPackets.Dequeue());
        }
    }

    void WriteUnlocked(EncodedPacket packet)
    {
        if (_finished || _videoType == null)
            return;
        if (!packet.IsAudio && packet.IsKeyframe && _open && packet.Timestamp - _segmentStart >= 8_000_000_0)
            ClosePart();
        if (!_open)
        {
            if (packet.IsAudio || !packet.IsKeyframe)
                return;
            OpenPart(packet.Timestamp);
        }

        var relative = packet.Timestamp - _segmentStart;
        if (relative < 0)
            return;
        if (packet.IsAudio)
        {
            if (_audioStream < 0 || relative <= _lastAudio)
                return;
            _lastAudio = relative;
            WriteSample(_audioStream, packet, relative);
        }
        else
        {
            if (relative <= _lastVideo)
                return;
            _lastVideo = relative;
            WriteSample(_videoStream, packet, relative);
        }
    }

    public IReadOnlyList<string> Finish()
    {
        lock (_gate)
        {
            if (_finished)
                return _parts.ToList();
            try
            {
                while (_pendingPackets.TryDequeue(out var packet, out _))
                    WriteUnlocked(packet);
                ClosePart();
                return _parts.ToList();
            }
            finally { _finished = true; }
        }
    }

    void OpenPart(long timestamp)
    {
        var path = Path.Combine(_directory, $"part-{_index:0000}.mp4");
        _index++;
        using var attributes = CreateWriterAttributes();
        _writer = MediaFactory.MFCreateSinkWriterFromURL(path, null!, attributes);
        _videoStream = _writer.AddStream(_videoType!);
        _writer.SetInputMediaType(_videoStream, _videoType!, null!);
        if (_audioType != null)
        {
            _audioStream = _writer.AddStream(_audioType);
            _writer.SetInputMediaType(_audioStream, _audioType, null!);
        }
        _writer.BeginWriting();
        _segmentStart = timestamp;
        _lastVideo = -1;
        _lastAudio = -1;
        _open = true;
        _pendingPart = path;
    }

    void WriteSample(int stream, EncodedPacket packet, long timestamp)
    {
        var buffer = MediaFactory.MFCreateMemoryBuffer(packet.Data.Length);
        H264Encoder.WriteBuffer(buffer, packet.Data);
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = timestamp;
        sample.SampleDuration = Math.Max(1, packet.Duration);
        if (packet.IsKeyframe)
            sample.Set(SampleAttributeKeys.CleanPoint, 1u);
        _writer!.WriteSample(stream, sample);
        sample.Dispose();
    }

    void ClosePart()
    {
        if (!_open)
            return;
        try
        {
            _writer?.Finalize();
            if (_pendingPart != null)
                _parts.Add(_pendingPart);
        }
        finally
        {
            _writer?.Dispose();
            _writer = null;
            _open = false;
            _pendingPart = null;
        }
    }

    public static void Remux(IReadOnlyList<string> parts, string destination)
    {
        if (parts.Count == 0)
            throw new InvalidOperationException("No recording segments found.");
        if (parts.Count == 1)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (new FileInfo(parts[0]).Length < 128)
                throw new InvalidOperationException("The recording segment is empty or incomplete.");
            File.Copy(parts[0], destination, false);
            return;
        }

        var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, 1u);
        var readers = new List<IMFSourceReader>();
        IMFSinkWriter? writer = null;
        try
        {
            using var writerAttributes = CreateWriterAttributes();
            writer = MediaFactory.MFCreateSinkWriterFromURL(destination, null!, writerAttributes);
            var map = new Dictionary<int, int>();
            var targetStreams = new Dictionary<Guid, int>();
            var first = OpenReader(parts[0], attributes);
            readers.Add(first);
            AddStreams(first, writer, map, targetStreams);
            writer.BeginWriting();
            long offset = 0;
            var partIndex = 0;
            foreach (var part in parts)
            {
                var reader = partIndex++ == 0 ? first : OpenReader(part, attributes);
                if (!ReferenceEquals(reader, readers[0]))
                    readers.Add(reader);
                map.Clear();
                for (var index = 0; index < 4; index++)
                {
                    IMFMediaType native;
                    try { native = reader.GetNativeMediaType(index, 0); }
                    catch { break; }
                    using (native)
                    {
                        native.GetGUID(MediaTypeAttributeKeys.MajorType, out var major).CheckError();
                        if (targetStreams.TryGetValue(major, out var target))
                            map[index] = target;
                    }
                }
                long partEnd = offset;
                long videoEnd = offset;
                var endedStreams = new HashSet<int>();
                var packetCounts = new Dictionary<int, int>();
                while (true)
                {
                    IMFSample? sample;
                    SourceReaderFlag flags;
                    int stream;
                    long timestamp;
                    try
                    {
                        sample = reader.ReadSample((int)SourceReaderIndex.AnyStream, SourceReaderControlFlag.None, out stream, out flags, out timestamp);
                    }
                    catch (Exception ex) when (IsEnd(ex))
                    {
                        break;
                    }

                    if (flags.HasFlag(SourceReaderFlag.EndOfStream))
                    {
                        if (!map.ContainsKey(stream))
                        {
                            sample?.Dispose();
                            break;
                        }
                        endedStreams.Add(stream);
                        reader.SetStreamSelection(stream, false);
                        if (endedStreams.Count >= map.Count && sample == null)
                            break;
                    }
                    if (flags.HasFlag(SourceReaderFlag.Error))
                        throw new InvalidOperationException("A recording segment could not be fully read.");
                    if (sample == null || !map.TryGetValue(stream, out var target))
                    {
                        sample?.Dispose();
                        continue;
                    }

                    var time = timestamp + offset;
                    packetCounts[target] = packetCounts.GetValueOrDefault(target) + 1;
                    using var inputBuffer = sample.ConvertToContiguousBuffer();
                    var bytes = H264Encoder.ReadBuffer(inputBuffer);
                    using var memory = MediaFactory.MFCreateMemoryBuffer(bytes.Length);
                    H264Encoder.WriteBuffer(memory, bytes);
                    using var outputSample = MediaFactory.MFCreateSample();
                    outputSample.AddBuffer(memory);
                    outputSample.SampleTime = time;
                    long duration = 0;
                    try { duration = sample.SampleDuration; } catch { /* use the next timestamp */ }
                    if (duration > 0)
                        outputSample.SampleDuration = duration;
                    try
                    {
                        if (sample.GetUInt32(SampleAttributeKeys.CleanPoint, out var clean).Success)
                            outputSample.Set(SampleAttributeKeys.CleanPoint, clean);
                    }
                    catch (SharpGen.Runtime.SharpGenException) { /* audio needs no clean point */ }
                    // A source reader may attach DTS as well as PTS. Both must move
                    // to the joined timeline, otherwise later AAC segments disappear.
                    try
                    {
                        if (sample.GetUInt64(SampleAttributeKeys.DecodeTimestamp, out var decodeTime).Success)
                            outputSample.Set(SampleAttributeKeys.DecodeTimestamp, checked(decodeTime + (ulong)offset));
                    }
                    catch (SharpGen.Runtime.SharpGenException)
                    {
                        // Some streams only supply presentation timestamps.
                    }
                    writer.WriteSample(target, outputSample);
                    sample.Dispose();
                    partEnd = Math.Max(partEnd, time + Math.Max(0, duration));
                    if (targetStreams.TryGetValue(MediaTypeGuids.Video, out var videoTarget) && target == videoTarget)
                        videoEnd = Math.Max(videoEnd, time + Math.Max(0, duration));
                }
                // A final AAC block can overlap the next video keyframe. Do not
                // insert that audio overlap as a pause in the joined video.
                offset = videoEnd > offset ? videoEnd : partEnd;
                AppLog.Info($"Joined {Path.GetFileName(part)}: {string.Join(", ", packetCounts.Select(p => $"stream {p.Key}={p.Value}"))}, end={TimeSpan.FromTicks(offset)}");
            }
            writer.Finalize();
        }
        finally
        {
            writer?.Dispose();
            foreach (var reader in readers)
                reader.Dispose();
            attributes.Dispose();
        }
    }

    static IMFSourceReader OpenReader(string path, IMFAttributes attributes)
    {
        var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        reader.SetStreamSelection((int)SourceReaderIndex.AllStreams, false);
        for (var index = 0; index < 4; index++)
        {
            IMFMediaType native;
            try { native = reader.GetNativeMediaType(index, 0); }
            catch { break; }
            using (native)
            {
                native.GetGUID(MediaTypeAttributeKeys.MajorType, out var major);
                if (major != MediaTypeGuids.Video && major != MediaTypeGuids.Audio)
                    continue;
                reader.SetCurrentMediaType(index, native);
                reader.SetStreamSelection(index, true);
            }
        }
        return reader;
    }

    static void AddStreams(IMFSourceReader reader, IMFSinkWriter writer, Dictionary<int, int> map, Dictionary<Guid, int> targets)
    {
        var streams = new List<(int Index, Guid Major, IMFMediaType Type)>();
        try
        {
            for (var index = 0; index < 4; index++)
            {
                IMFMediaType native;
                try { native = reader.GetNativeMediaType(index, 0); }
                catch { break; }
                native.GetGUID(MediaTypeAttributeKeys.MajorType, out var major);
                if (major == MediaTypeGuids.Video || major == MediaTypeGuids.Audio)
                    streams.Add((index, major, native));
                else
                    native.Dispose();
            }
            // Keep video first, matching the segment writer. Source readers may list audio first.
            foreach (var entry in streams.OrderBy(s => s.Major == MediaTypeGuids.Video ? 0 : 1))
            {
                var stream = writer.AddStream(entry.Type);
                writer.SetInputMediaType(stream, entry.Type, null!);
                map[entry.Index] = stream;
                targets[entry.Major] = stream;
                AppLog.Info($"Remux stream mapping source={entry.Index}, target={stream}, major={entry.Major}");
            }
        }
        finally { foreach (var entry in streams) entry.Type.Dispose(); }
    }

    internal static IMFAttributes CreateWriterAttributes()
    {
        var attributes = MediaFactory.MFCreateAttributes(4);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, 1u);
        attributes.Set(SinkWriterAttributeKeys.LowLatency, 1u);
        attributes.Set(SinkWriterAttributeKeys.DisableThrottling, 1u);
        return attributes;
    }

    static bool IsEnd(Exception exception) =>
        (uint)exception.HResult == 0xC00D3E85 || exception.HResult == -2147467259;

    public void Dispose()
    {
        lock (_gate)
        {
            _finished = true;
            _pendingPackets.Clear();
            try { ClosePart(); }
            finally
            {
                _videoType?.Dispose();
                _audioType?.Dispose();
                _videoType = null;
                _audioType = null;
            }
        }
    }
}

public static class MediaInspector
{
    public static TimeSpan GetDuration(string path)
    {
        MfBootstrap.Ensure();
        IMFSourceReader? reader = null;
        try
        {
            reader = MediaFactory.MFCreateSourceReaderFromURL(path, null!);
            var value = reader.GetPresentationAttribute((int)SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
            if (value.Value is long ticks)
                return TimeSpan.FromTicks(ticks);
            if (value.Value is ulong uticks)
                return TimeSpan.FromTicks((long)uticks);
            return TimeSpan.Zero;
        }
        catch
        {
            return TimeSpan.Zero;
        }
        finally
        {
            reader?.Dispose();
        }
    }

    public static bool TryReadThumbnail(string path, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;
        MfBootstrap.Ensure();
        IMFSourceReader? reader = null;
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
            reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
            using var type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            type.Set(MediaTypeAttributeKeys.Subtype, MfGuids.Rgb32);
            reader.SetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream, type);
            using var current = reader.GetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream);
            MediaFactory.MFGetAttributeSize(current, MediaTypeAttributeKeys.FrameSize, out var w, out var h);
            width = (int)w;
            height = (int)h;
            var stride = checked(width * 4);
            try
            {
                if (current.GetUInt32(MfGuids.DefaultStride, out var storedStride).Success)
                    stride = unchecked((int)storedStride);
            }
            catch (SharpGen.Runtime.SharpGenException) { /* RGB32 defaults to width * 4 */ }
            try { reader.SetCurrentPosition(1_000_000_0); } catch { /* short files stay at the start */ }
            IMFSample? frame = null;
            for (var attempt = 0; attempt < 12 && frame == null; attempt++)
            {
                frame = reader.ReadSample((int)SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if (flags.HasFlag(SourceReaderFlag.Error) || flags.HasFlag(SourceReaderFlag.EndOfStream))
                {
                    frame?.Dispose();
                    return false;
                }
            }
            using var sample = frame;
            if (sample == null)
                return false;
            using var buffer = sample.ConvertToContiguousBuffer();
            var rowBytes = checked(width * 4);
            var allocatedHeight = checked((height + 63) & ~63);
            var bytes = H264Encoder.ReadBuffer(buffer, checked(Math.Max(rowBytes, Math.Abs(stride)) * allocatedHeight));
            if (bytes.Length < checked(Math.Abs(stride) * height))
                return false;
            pixels = new byte[checked(rowBytes * height)];
            for (var row = 0; row < height; row++)
            {
                var sourceRow = stride < 0 ? height - 1 - row : row;
                Buffer.BlockCopy(bytes, sourceRow * Math.Abs(stride), pixels, row * rowBytes, rowBytes);
            }
            // MFVideoFormat_RGB32's fourth byte is padding, not an alpha channel.
            // WPF's BGRA thumbnails must be opaque.
            for (var i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;
            return pixels.Length >= width * height * 4 && width > 0 && height > 0;
        }
        catch (Exception ex)
        {
            AppLog.Error("Thumbnail extraction failed for " + path, ex);
            return false;
        }
        finally
        {
            reader?.Dispose();
        }
    }

    public static (bool HasVideo, bool HasAudio, TimeSpan Duration) Probe(string path)
    {
        MfBootstrap.Ensure();
        var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, 1u);
        var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        attributes.Dispose();
        try
        {
            var hasVideo = false;
            var hasAudio = false;
            long end = 0;
            for (var i = 0; i < 2000; i++)
            {
                IMFSample? sample;
                SourceReaderFlag flags;
                int stream;
                long timestamp;
                try
                {
                    sample = reader.ReadSample((int)SourceReaderIndex.AnyStream, SourceReaderControlFlag.None, out stream, out flags, out timestamp);
                }
                catch
                {
                    break;
                }

                if (flags.HasFlag(SourceReaderFlag.EndOfStream) && sample == null)
                    break;
                if (sample == null)
                    continue;
                try
                {
                    var type = reader.GetCurrentMediaType(stream);
                    type.GetGUID(MediaTypeAttributeKeys.MajorType, out var major);
                    if (major == MediaTypeGuids.Video) hasVideo = true;
                    if (major == MediaTypeGuids.Audio) hasAudio = true;
                    type.Dispose();
                }
                catch { /* ignore stream type */ }
                long duration = 0;
                try { duration = sample.SampleDuration; } catch { duration = 0; }
                end = Math.Max(end, timestamp + duration);
                sample.Dispose();
            }
            return (hasVideo, hasAudio, TimeSpan.FromTicks(end));
        }
        finally
        {
            reader.Dispose();
        }
    }
}
