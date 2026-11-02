using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Spezi.Core.Recording;

internal sealed class AacEncoder : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int FrameSamples = 1024;

    IMFTransform? _transform;
    readonly byte[] _pending = new byte[FrameSamples * Channels * 2 * 4];
    int _pendingBytes;
    long _pendingTimestamp;
    int _outputSize = 4096;
    int _inputAlignment = 16;
    int _outputAlignment = 16;
    bool _outputProvidesSamples;
    int _encoded;

    public IMFMediaType? OutputType { get; private set; }
    public event Action<EncodedPacket>? Packet;

    public void Start()
    {
        MfBootstrap.Ensure();
        var outputInfo = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Audio,
            GuidSubtype = AudioFormatGuids.Aac
        };
        var activates = Enumerate(outputInfo);
        if (activates.Count == 0)
            throw new InvalidOperationException("No AAC encoder found.");

        var activate = activates[0];
        for (var i = 1; i < activates.Count; i++)
            activates[i].Dispose();
        using (activate)
        {
            _transform = activate.ActivateObject<IMFTransform>();
        }

        var output = _transform.GetOutputAvailableType(0, 0);
        output.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
        output.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)Channels);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, 192000u);
        output.Set(MediaTypeAttributeKeys.AacPayloadType, 0u);
        _transform.SetOutputType(0, output, 0);
        output.Dispose();

        var input = _transform.GetInputAvailableType(0, 0);
        input.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
        input.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)Channels);
        input.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        input.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(Channels * 2));
        input.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(SampleRate * Channels * 2));
        _transform.SetInputType(0, input, 0);
        input.Dispose();
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
        var inputInfo = _transform.GetInputStreamInfo(0);
        var streamInfo = _transform.GetOutputStreamInfo(0);
        _inputAlignment = Math.Max(1, (int)inputInfo.Alignment);
        _outputAlignment = Math.Max(1, (int)streamInfo.Alignment);
        _outputProvidesSamples = ((int)streamInfo.Flags & 0x100) != 0;
        if (streamInfo.Size > 0)
            _outputSize = (int)streamInfo.Size;
        AppLog.Info($"AAC streams inAlign={_inputAlignment} outAlign={_outputAlignment} outSize={_outputSize} outFlags={streamInfo.Flags} inFlags={inputInfo.Flags} provides={_outputProvidesSamples}");
        OutputType = Clone(_transform.GetOutputCurrentType(0));
        if (Environment.GetEnvironmentVariable("SPEZI_AAC_BURST") == "1")
        {
            var silent = new byte[FrameSamples * Channels * 2];
            for (var i = 0; i < 500; i++)
            {
                if (i % 25 == 0)
                    AppLog.Info("AAC burst " + i);
                WritePcm16(silent, i * (long)FrameSamples * 10_000_000L / SampleRate);
            }
            AppLog.Info("AAC burst finished at " + _encoded);
        }
    }

    public void WritePcm16(ReadOnlySpan<byte> pcm, long timestamp)
    {
        if (_transform == null || pcm.Length == 0)
            return;
        var offset = 0;
        while (offset < pcm.Length)
        {
            if (_pendingBytes == 0)
            {
                _pendingTimestamp = timestamp + (long)offset * 10_000_000L / (SampleRate * Channels * 2);
            }

            var frameBytes = FrameSamples * Channels * 2;
            var copy = Math.Min(pcm.Length - offset, frameBytes - _pendingBytes);
            pcm.Slice(offset, copy).CopyTo(_pending.AsSpan(_pendingBytes));
            _pendingBytes += copy;
            offset += copy;
            if (_pendingBytes == frameBytes)
            {
                Encode(_pending.AsSpan(0, frameBytes), _pendingTimestamp);
                _pendingBytes = 0;
            }
        }
    }

    void Encode(ReadOnlySpan<byte> pcm, long timestamp)
    {
        var transform = _transform!;
        var buffer = MediaFactory.MFCreateAlignedMemoryBuffer(pcm.Length, _inputAlignment);
        H264Encoder.WriteBuffer(buffer, pcm);
        var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        buffer.Dispose();
        sample.SampleTime = timestamp;
        sample.SampleDuration = FrameSamples * 10_000_000L / SampleRate;
        try
        {
            transform.ProcessInput(0, sample, 0);
            Pull();
            _encoded++;
        }
        finally
        {
            sample.Dispose();
        }
    }

    void Pull()
    {
        var transform = _transform!;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var info = transform.GetOutputStreamInfo(0);
            if (info.Size > 0)
                _outputSize = (int)Math.Max(info.Size, 256);
            IMFSample? sample = null;
            if (!_outputProvidesSamples)
            {
                var memory = MediaFactory.MFCreateAlignedMemoryBuffer(Math.Max(_outputSize, 2048), _outputAlignment);
                sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(memory);
                memory.Dispose();
            }
            var data = new OutputDataBuffer { StreamID = 0, Sample = sample };
            Result result;
            try
            {
                result = transform.ProcessOutput(ProcessOutputFlags.None, 1, ref data, out _);
            }
            catch (SharpGenException ex) when ((uint)ex.HResult == MfGuids.NeedMoreInput || (uint)ex.ResultCode.Code == MfGuids.NeedMoreInput)
            {
                sample?.Dispose();
                return;
            }

            if (result.Failure)
            {
                sample?.Dispose();
                if ((uint)result.Code == MfGuids.NeedMoreInput)
                    return;
                if ((uint)result.Code == MfGuids.StreamChange)
                {
                    var updated = transform.GetOutputCurrentType(0);
                    transform.SetOutputType(0, updated, 0);
                    OutputType?.Dispose();
                    OutputType = Clone(updated);
                    updated.Dispose();
                    AppLog.Info("AAC output type updated after a stream change.");
                    continue;
                }
                AppLog.Error("AAC ProcessOutput failed: " + result.Code);
                return;
            }

            var produced = data.Sample ?? sample;
            if (produced != null)
            {
                using var contiguous = produced.ConvertToContiguousBuffer();
                var bytes = H264Encoder.ReadBuffer(contiguous);
                var duration = FrameSamples * 10_000_000L / SampleRate;
                var timestamp = produced.SampleTime;
                duration = produced.SampleDuration > 0 ? produced.SampleDuration : duration;
                if (bytes.Length > 0)
                {
                    Packet?.Invoke(new EncodedPacket
                    {
                        IsAudio = true,
                        Timestamp = timestamp,
                        Duration = duration,
                        Data = bytes
                    });
                }
                if (produced != null && sample != null && produced.NativePointer != sample.NativePointer)
                    produced.Dispose();
            }
            sample?.Dispose();
            if (data.Events != null)
                data.Events.Dispose();
        }
    }

    static IMFMediaType Clone(IMFMediaType source)
    {
        var copy = MediaFactory.MFCreateMediaType();
        source.CopyAllItems(copy);
        return copy;
    }

    static List<IMFActivate> Enumerate(RegisterTypeInfo output)
    {
        MediaFactory.MFTEnumEx(
            TransformCategoryGuids.AudioEncoder,
            (uint)EnumFlag.EnumFlagSyncmft | (uint)EnumFlag.EnumFlagSortandfilter,
            null,
            output,
            out var array,
            out var count);
        var list = new List<IMFActivate>((int)count);
        unsafe
        {
            var items = (nint*)array;
            for (uint i = 0; i < count; i++)
                list.Add(new IMFActivate(items[i]));
        }
        if (array != 0)
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(array);
        return list;
    }

    public void DrainAndStop()
    {
        if (_transform == null)
            return;
        if (_pendingBytes > 0)
        {
            var frameBytes = FrameSamples * Channels * 2;
            Array.Clear(_pending, _pendingBytes, frameBytes - _pendingBytes);
            Encode(_pending.AsSpan(0, frameBytes), _pendingTimestamp);
            _pendingBytes = 0;
        }
        _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
        _transform.ProcessMessage(TMessageType.MessageCommandDrain, UIntPtr.Zero);
        Pull();
    }

    public void Dispose()
    {
        OutputType?.Dispose();
        _transform?.Dispose();
        _transform = null;
    }
}
