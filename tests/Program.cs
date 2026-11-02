using System.Diagnostics;
using Spezi.Core;
using Spezi.Core.Recording;
using Vortice.MediaFoundation;
using NAudio.Wave;
using NAudio.CoreAudioApi;

var report = new List<string>();
var root = Path.Combine(Path.GetTempPath(), "SpeziRecordingRegression", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(root);
// Keep test errors (e.g. intentionally blocked output folders) out of the user's real log.
Environment.SetEnvironmentVariable("SPEZI_LOG_DIR", root);
void Log(string text) { report.Add(text); Console.WriteLine(text); }
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
try
{
    if (args.Length >= 4 && args[0] == "--audio-fixture")
    {
        await ApplicationAudioTests.Worker(args);
        return 0;
    }
    MfBootstrap.Ensure();
    if (args.Contains("--microphone-mono-test"))
    {
        MicrophoneMonoTests.Run(root, Log, Check);
        Log("MICROPHONE_MONO_TEST_OK");
        return 0;
    }
    if (args.Contains("--replay-duration-test"))
    {
        ApplicationAudioTests.CheckSettingsAndBuffer(Log);
        Log("REPLAY_DURATION_TEST_OK");
        return 0;
    }
    if (args.Contains("--application-audio-test"))
    {
        await ApplicationAudioTests.RunCapture(root, Log);
        return 0;
    }
    if (args.Contains("--conversion-test"))
    {
        CheckConversions(Log, Check);
        Log("CONVERSION_TESTS_OK");
        return 0;
    }
    if (args.Contains("--performance-test"))
    {
        await MeasurePerformance(root, Log, Check);
        Log("PERFORMANCE_TEST_OK");
        return 0;
    }
    if (args.Contains("--library-test"))
    {
        var library = Path.Combine(root, "library");
        Directory.CreateDirectory(library);
        Check(Path.GetFileName(ClipLibrary.GameDirectory(library, "Overwatch")) == "Overwatch", "Overwatch folder failed");
        Check(Path.GetFileName(ClipLibrary.GameDirectory(library, "Rainbow Six Siege")) == "Rainbow Six Siege", "Rainbow Six folder failed");
        Check(Path.GetFileName(ClipLibrary.GameDirectory(library, null)) == "General", "Unknown game fallback failed");
        Check(Path.GetFileName(ClipLibrary.GameDirectory(library, "CON")) == "_CON", "Reserved Windows folder name failed");
        var unsafeFolder = ClipLibrary.GameDirectory(library, "..\\outside/../A:B*");
        Check(unsafeFolder.StartsWith(library + Path.DirectorySeparatorChar) && !Path.GetFileName(unsafeFolder).Any(c => Path.GetInvalidFileNameChars().Contains(c)), "Game folder escapes the library");
        var name = RecordingNames.Create("Clip", "Overwatch");
        var old = Path.Combine(library, name);
        File.WriteAllText(old, "original video bytes");
        var unrelated = Path.Combine(library, "my-other-video.mp4");
        File.WriteAllText(unrelated, "user video");
        Check(ClipLibrary.OrganizeLegacyFiles(library) == 1, "Generated old clip was not organized");
        var organized = Path.Combine(library, "Overwatch", name);
        Check(!File.Exists(old) && File.ReadAllText(organized) == "original video bytes" && File.Exists(unrelated), "Organization changes content or unrelated videos");
        Check(ClipLibrary.OrganizeLegacyFiles(library) == 0, "Organization repeats");
        File.WriteAllText(old, "different original video");
        Check(ClipLibrary.OrganizeLegacyFiles(library) == 0 && File.ReadAllText(old) == "different original video" && File.ReadAllText(organized) == "original video bytes", "A filename collision overwrites a clip");
        var pending = Path.Combine(library, "Overwatch", ".spezi-pending");
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(pending, "partial.mp4"), "unfinished");
        var visible = ClipLibrary.VideoFiles(library).ToArray();
        Check(visible.Contains(organized) && visible.Contains(unrelated) && visible.Length == 3 && visible.All(p => !p.Contains(".spezi-pending")), "Nested clips are missing or incomplete clips appear");
        var defaults = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Check(defaults.ToastDurationSeconds == 3.5 && defaults.AccentColor == AppSettings.DefaultAccentColor, "Existing settings lose their defaults");
        defaults.ToastDurationSeconds = double.NaN;
        defaults.AccentColor = "invalid";
        defaults.Clamp();
        Check(defaults.ToastDurationSeconds == 3.5 && defaults.AccentColor == AppSettings.DefaultAccentColor, "Malformed appearance preferences are unsafe");
        defaults.ToastDurationSeconds = 99;
        defaults.AccentColor = "#ac8bfa";
        defaults.Clamp();
        Check(defaults.ToastDurationSeconds == 15 && defaults.AccentColor == "#AC8BFA", "Appearance preferences are not normalized");
        Log("PASS game directories, reserved names, safe destinations, recursive gallery files, hidden partials, lossless legacy sorting, collisions, settings defaults and validation");
        return 0;
    }
    if (args.Length >= 2 && args[0] == "--naming-recovery-test")
    {
        Check(GameTitles.Resolve("Overwatch", "", true) == "Overwatch", "Overwatch title detection failed");
        Check(GameTitles.Resolve("RainbowSix_DX11", "", true) == "Rainbow Six Siege", "Rainbow Six title detection failed");
        Check(GameTitles.Resolve("chrome", "YouTube", true) == null, "Browser is mistaken for a game");
        Check(GameTitles.Resolve("NewGame", "A New Game", true) == "A New Game", "Unknown full-screen title is discarded");
        Check(RecordingNames.Create("Clip", "Rainbow Six Siege").StartsWith("Rainbow Six Siege_Clip_"), "Clip lacks game title");
        Check(!RecordingNames.Create("Recording", "A:B/C?D*").Any(c => Path.GetInvalidFileNameChars().Contains(c)), "Recording title is unsafe");
        var recoveryRoot = Path.Combine(root, "recovery");
        var oldSession = Path.Combine(recoveryRoot, "20261005_225704");
        var recoveryOutput = Path.Combine(root, "recovered");
        Directory.CreateDirectory(oldSession);
        Directory.CreateDirectory(recoveryOutput);
        File.Copy(args[1], Path.Combine(oldSession, "part-0000.mp4"));
        File.Copy(args[1], Path.Combine(oldSession, "part-0001.mp4"));
        File.WriteAllText(Path.Combine(oldSession, "part-0002.mp4"), "incomplete final MP4");
        var generalOutput = ClipLibrary.GameDirectory(recoveryOutput, null);
        Directory.CreateDirectory(generalOutput);
        var brokenOutput = Path.Combine(generalOutput, "Spezi_Wiederhergestellt_20261005_225704.mp4");
        File.WriteAllText(brokenOutput, "incomplete published output");
        var repaired = SessionRecovery.Recover(recoveryRoot, recoveryOutput);
        Check(repaired.Count == 1 && repaired[0] == brokenOutput, "Old invalid recovery is not replaced");
        Check(MediaInspector.Probe(repaired[0]).HasVideo, "Recovered video is not playable");
        Check(SessionRecovery.Recover(recoveryRoot, recoveryOutput).Count == 0, "Recovery repeats on every start");
        Check(Directory.GetFiles(oldSession, "part-*.mp4").Length == 3, "Original recording parts were discarded");
        Check(ClipLibrary.VideoFiles(recoveryOutput).Count() == 1, "Recovery creates duplicates");
        Check(Directory.GetFiles(Path.Combine(generalOutput, ".spezi-pending", "previous-recovery")).Length == 1, "Old invalid output was not backed up");
        var namedSession = Path.Combine(recoveryRoot, "named");
        Directory.CreateDirectory(namedSession);
        File.Copy(args[1], Path.Combine(namedSession, "part-0000.mp4"));
        File.WriteAllText(Path.Combine(namedSession, "game-title.txt"), "Overwatch");
        var named = SessionRecovery.Recover(recoveryRoot, recoveryOutput);
        Check(named.Count == 1 && Path.GetFileName(named[0]).StartsWith("Overwatch_Recovered_"), "Recovered game loses its title");
        Check(Path.GetFileName(Path.GetDirectoryName(named[0])) == "Overwatch", "Recovered game is outside its game folder");
        var invalidSession = Path.Combine(recoveryRoot, "invalid");
        Directory.CreateDirectory(invalidSession);
        File.WriteAllText(Path.Combine(invalidSession, "part-0000.mp4"), "not a video");
        Check(SessionRecovery.Recover(recoveryRoot, recoveryOutput).Count == 0, "Invalid recovery announces success");
        Check(SessionRecovery.Recover(recoveryRoot, recoveryOutput).Count == 0, "Invalid recovery repeats");
        File.Copy(args[1], Path.Combine(invalidSession, "part-0000.mp4"), true);
        Check(SessionRecovery.Recover(recoveryRoot, recoveryOutput).Count == 1, "Changed original cannot be recovered after a previous failure");
        Log("PASS game names, safe file names, playable recovery, partial final segment, original preservation and recovery idempotence");
        return 0;
    }
    if (args.Length >= 2 && args[0] == "--inspect-audio")
    {
        var audio = DecodeAudio(args[1]);
        Log($"Decoded audio {Path.GetFileName(args[1])}: peak={audio.Peak:F6} rms={audio.Rms:F6} samples={audio.Count}");
        return 0;
    }
    if (args.Contains("--audio-loopback-test"))
    {
        using var audioRecorder = new Recorder(Path.Combine(root, "sessions"));
        var audioSettings = SettingsStore.Load();
        audioSettings.OutputDirectory = root;
        audioSettings.RecordMicrophone = false;
        audioSettings.CaptureSystemAudio = true;
        audioSettings.CaptureCursor = false;
        audioSettings.DeviceGains.Clear();
        var devices = audioRecorder.ListAudioDevices();
        var selected = devices.FirstOrDefault(d => !d.IsMicrophone && audioSettings.OutputDeviceIds.Contains(d.Id))
            ?? devices.First(d => !d.IsMicrophone && d.IsDefault);
        audioSettings.OutputDeviceIds = [selected.Id];
        Log("Loopback endpoint: " + selected.Name);
        var audioDisplay = audioRecorder.ListDisplays().First(d => d.IsPrimary);
        audioRecorder.StartSession(new RecordingRequest
        {
            Settings = audioSettings,
            Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = audioDisplay.DeviceName }
        });
        using var devicesEnumerator = new MMDeviceEnumerator();
        using var device = devicesEnumerator.GetDevice(selected.Id);
        var waveform = new byte[7 * 48000 * 2 * 4];
        for (var frame = 0; frame < 7 * 48000; frame++)
        {
            var second = frame / 48000d;
            var value = second is >= 0.5 and < 2.0 or >= 4.5 and < 6.0
                ? (float)(0.06 * Math.Sin(2 * Math.PI * 523.25 * second)) : 0f;
            BitConverter.TryWriteBytes(waveform.AsSpan(frame * 8, 4), value);
            BitConverter.TryWriteBytes(waveform.AsSpan(frame * 8 + 4, 4), value);
        }
        using var source = new RawSourceWaveStream(new MemoryStream(waveform), WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        using var playback = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        playback.Init(source);
        await Task.Delay(500);
        playback.Play();
        await Task.Delay(7500);
        var path = await audioRecorder.StopSessionAsync();
        Check(path != null && File.Exists(path), "Audible regression clip was not saved");
        var decoded = DecodeAudio(path!);
        var early = DecodeAudio(path!, 1.1, 2.3);
        var silence = DecodeAudio(path!, 3.0, 4.5);
        var resumed = DecodeAudio(path!, 5.2, 6.5);
        var earlyTone = ApplicationAudioTests.ToneLevel(path!, 523.25, 1.1, 2.3);
        var quietTone = ApplicationAudioTests.ToneLevel(path!, 523.25, 3.0, 4.5);
        var resumedTone = ApplicationAudioTests.ToneLevel(path!, 523.25, 5.2, 6.5);
        Log($"Decoded AAC peak={decoded.Peak:F6} rms={decoded.Rms:F6}, first tone={early.Rms:F6}, silence={silence.Rms:F6}, resumed tone={resumed.Rms:F6}");
        Log($"523.25 Hz test tone amplitude: first={earlyTone:F6}, quiet={quietTone:F6}, resumed={resumedTone:F6}");
        Check(earlyTone > 0.005 && resumedTone > 0.005, "System audio is silent or fails to resume after silence");
        // Whole-device capture may include music or voice from other apps. Measure
        // the fixture tone rather than assuming the entire desktop becomes silent.
        Check(quietTone < earlyTone * 0.1, "Audio timestamps collapse a quiet interval");
        Check(MediaInspector.TryReadThumbnail(path!, out var pixels, out var width, out var height), "Thumbnail cannot be decoded");
        Check(pixels.Where((_, index) => index % 4 == 3).All(alpha => alpha == 255), "Thumbnail is transparent");
        Log($"PASS audible system audio survives AAC/MP4, pause and resume; opaque thumbnail {width}x{height}");
        Log("AUDIBLE_CLIP=" + path);
        return 0;
    }
    if (args.Length >= 2 && args[0] == "--remux-only")
    {
        var joined = Path.Combine(root, "remux.mp4");
        SegmentedSession.Remux([args[1], args[1]], joined);
        Log("Remux result: " + joined);
        Log("Timing: " + Inspect(joined));
        return 0;
    }
    var longUptime = 4L * 24 * 3600 * 10_000_000;
    Check(MediaClock.CounterToTicks(longUptime, 10_000_000) == longUptime, "QPC conversion overflows after long uptime");
    Check(MediaClock.CounterToTicks(123456789012345, 20_000_000) == 61728394506172, "QPC conversion loses units");
    var clock = new MediaClock();
    Thread.Sleep(30);
    Check(clock.Now() >= 200_000 && clock.Now() < 5_000_000, "Clock does not advance in real time");
    Log("PASS clock conversion, including four days uptime");

    foreach (var chunkFrames in new[] { 960, 9600 })
    {
        using var encoder = new AacEncoder();
        var packets = new List<EncodedPacket>();
        encoder.Packet += packets.Add;
        encoder.Start();
        var pcm = new byte[chunkFrames * 4];
        for (var frame = 0; frame < 48000; frame += chunkFrames)
            encoder.WritePcm16(pcm, 20_000_000L + frame * 10_000_000L / 48000);
        encoder.DrainAndStop();
        Check(packets.Count >= 46 && packets.Count <= 50, $"AAC count {packets.Count} for chunk {chunkFrames}");
        for (var i = 1; i < packets.Count; i++)
        {
            var delta = packets[i].Timestamp - packets[i - 1].Timestamp;
            Check(Math.Abs(delta - 1024 * 10_000_000L / 48000) < 10, $"AAC timestamp gap/duplicate: {delta}");
        }
        Log($"PASS AAC {chunkFrames}-sample chunks: {packets.Count} packets, continuous timestamps");
    }

    var replay = new ReplayBuffer();
    replay.Configure(TimeSpan.FromSeconds(5), 2000);
    for (var second = 0; second <= 20; second++)
    {
        replay.Add(new EncodedPacket { Timestamp = second * 10_000_000L, Duration = 10_000_000, IsKeyframe = second % 2 == 0, Data = [1] });
        replay.Add(new EncodedPacket { Timestamp = second * 10_000_000L, Duration = 10_000_000, IsAudio = true, Data = [1] });
    }
    var buffered = replay.Snapshot();
    Check(buffered.Video.Length > 1 && buffered.Video[0].IsKeyframe, "Replay loses keyframes after buffer rollover");
    Check(buffered.Buffered.TotalSeconds is >= 3 and <= 5, "Replay duration does not stay bounded");
    Check(buffered.Audio.All(p => p.Timestamp >= buffered.Video[0].Timestamp), "Replay retains audio before video origin");
    Log("PASS replay buffer rolls over and retains a decodable keyframe");

    using var recorder = new Recorder(Path.Combine(root, "sessions"));
    recorder.Notice += (_, notice) => Log($"NOTICE {notice.Kind}: {notice.Message}");
    var display = recorder.ListDisplays().First(d => d.IsPrimary);
    var settings = new AppSettings { OutputDirectory = root, Width = 1280, Height = 720, FrameRate = 30, VideoBitrateKbps = 6000, CaptureCursor = false, AllowSoftwareEncoder = false };
    if (args.Contains("--60fps"))
    {
        settings.Width = 1920;
        settings.Height = 1080;
        settings.FrameRate = 60;
        settings.VideoBitrateKbps = 15000;
    }
    var request = new RecordingRequest
    {
        Settings = settings,
        Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = display.DeviceName },
        ReplayDuration = TimeSpan.FromSeconds(5),
        GameTitle = "Overwatch"
    };
    recorder.StartReplay(request);
    await Task.Delay(2000);
    recorder.StartSession(request);
    await Task.Delay(12000);
    // The app refreshes the observed game while capturing. Simulate that observer
    // without requiring a real game to be opened during the regression test.
    recorder.UpdateGameTitle("Overwatch");
    var snapshot = recorder.GetSnapshot();
    Log($"LIVE encoder={snapshot.EncoderName}, capture={snapshot.CaptureFps:F1}, encoded={snapshot.EncodedFps:F1}, dropped={snapshot.DroppedFrames}, buffered={snapshot.ReplayBuffered}");
    Check(snapshot.ReplayBuffered.TotalSeconds >= 3, "Replay buffer empty after rollover");
    Check(snapshot.EncodedFps >= settings.FrameRate * 0.9, "Video recording stops when the desktop is unchanged");
    var clips = await Task.WhenAll(recorder.SaveClipAsync(), recorder.SaveClipAsync("Rainbow Six Siege"));
    Check(clips[0] != clips[1], "Concurrent clips overwrite one another");
    var session = await recorder.StopSessionAsync();
    Check(Path.GetFileName(clips[0]!).StartsWith("Overwatch_Clip_") && Path.GetFileName(clips[1]!).StartsWith("Rainbow Six Siege_Clip_"), "Saved replay lacks the captured game title");
    Check(Path.GetDirectoryName(clips[0]) == Path.Combine(root, "Overwatch") && Path.GetDirectoryName(clips[1]) == Path.Combine(root, "Rainbow Six Siege") && Path.GetDirectoryName(session) == Path.Combine(root, "Overwatch"), "Clips or recording are outside their game folders");
    Check(Path.GetFileName(session!).StartsWith("Overwatch_Recording_"), "Saved recording lacks the captured game title");
    recorder.StopReplay();
    foreach (var path in clips.Append(session))
    {
        Check(path != null && File.Exists(path), "Recording was not saved");
        var probe = MediaInspector.Probe(path!);
        Check(probe.HasVideo && probe.HasAudio, "Recording lacks audio/video");
        var timing = Inspect(path!);
        Check(timing.VideoCount >= (path == session ? 280 : 70), "Too few video frames");
        Check(timing.AudioCount >= (path == session ? 400 : 100), "Too few audio frames");
        Check(timing.VideoEnd > (path == session ? 10 : 2.5), "Video duration collapsed");
        Check(Math.Abs(timing.VideoEnd - timing.AudioEnd) < 0.2, "Audio/video duration mismatch");
        Check(timing.MaxVideoDelta < 0.12, "Video freezes or catches up in bursts");
        Check(timing.MaxAudioDelta < 0.025, "Audio timestamp gap or duplication");
        Check(MediaInspector.TryReadThumbnail(path!, out var pixels, out var width, out var height) && pixels.Length >= width * height * 4, "Thumbnail cannot decode the saved video");
        Log($"PASS {Path.GetFileName(path)} videoFrames={timing.VideoCount} audioFrames={timing.AudioCount} videoEnd={timing.VideoEnd:F3}s audioEnd={timing.AudioEnd:F3}s maxVideoGap={timing.MaxVideoDelta:F4}s maxAudioGap={timing.MaxAudioDelta:F4}s");
    }

    recorder.StartSession(request);
    await Task.Delay(3000);
    var standalone = await recorder.StopSessionAsync();
    Check(standalone != null && File.Exists(standalone), "Standalone session not saved");
    Check(MediaInspector.GetDuration(standalone!).TotalSeconds >= 2.7, "Standalone encoder drain loses the end of the recording");
    Check(!recorder.GetSnapshot().PipelineRunning, "Standalone session leaves capture running");
    Log("PASS standalone recording drains and stops capture");

    settings.CaptureSystemAudio = false;
    settings.RecordMicrophone = false;
    recorder.StartSession(request);
    await Task.Delay(3000);
    var silent = await recorder.StopSessionAsync();
    Check(silent != null && File.Exists(silent), "Video-only recording not saved");
    Check(MediaInspector.GetDuration(silent!).TotalSeconds >= 2.7, "Video-only duration collapsed");
    Log("PASS video-only session");

    var notices = new List<NoticeKind>();
    recorder.Notice += (_, notice) => { lock (notices) notices.Add(notice.Kind); };
    settings.CaptureSystemAudio = true;
    recorder.StartSession(request);
    await Task.Delay(1000);
    var blocked = Path.Combine(root, "blocked-output");
    File.WriteAllText(blocked, "A file cannot be used as a save directory.");
    settings.OutputDirectory = blocked;
    var failedSave = await recorder.StopSessionAsync();
    Check(failedSave == null, "Failed save returns a successful file path");
    lock (notices)
    {
        Check(notices.Contains(NoticeKind.Error), "Failed save is not reported");
        Check(!notices.Contains(NoticeKind.SessionStopped), "Failed save announces success");
    }
    settings.OutputDirectory = root;
    Log("PASS failed save reports an error and never claims success");

    Log("ALL_RECORDING_TESTS_PASSED");
    return 0;
}
catch (Exception ex)
{
    Log("FAILED " + ex);
    return 1;
}
finally
{
    File.WriteAllLines(Path.Combine(root, "result.txt"), report);
    Console.WriteLine("Report: " + Path.Combine(root, "result.txt"));
}

static void CheckConversions(Action<string> log, Action<bool, string> check)
{
    var slotType = typeof(AudioEngine).GetNestedType("CaptureSlot", System.Reflection.BindingFlags.NonPublic)!;
    foreach (var bits in new[] { 8, 16, 24, 32 })
    {
        using var slot = (IDisposable)Activator.CreateInstance(slotType, "test", "PCM", false, 1d, new WaveFormat(48000, bits, 1))!;
        var append = slotType.GetMethod("Append")!.CreateDelegate<Action<byte[], int, long>>(slot);
        var read = slotType.GetMethod("Read")!.CreateDelegate<Func<long, int, float[], bool>>(slot);
        var bytes = new byte[960 * bits / 8];
        for (var frame = 0; frame < 960; frame++)
        {
            var offset = frame * bits / 8;
            if (bits == 8) bytes[offset] = 160;
            else if (bits == 16) BitConverter.TryWriteBytes(bytes.AsSpan(offset, 2), (short)8192);
            else if (bits == 24) bytes[offset + 2] = 32;
            else BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), 536870912);
        }
        append(bytes, bytes.Length, 0);
        Array.Clear(bytes);
        var samples = new float[1920];
        check(read(0, 960, samples) && samples.All(value => Math.Abs(value - 0.25f) < 0.00001), $"PCM{bits} mono conversion or buffer ownership is incorrect");
    }
    foreach (var rate in new[] { 44100, 48000, 96000 })
    {
        using var slot = (IDisposable)Activator.CreateInstance(slotType, "test", "Float", false, 1d, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2))!;
        var append = slotType.GetMethod("Append")!.CreateDelegate<Action<byte[], int, long>>(slot);
        var read = slotType.GetMethod("Read")!.CreateDelegate<Func<long, int, float[], bool>>(slot);
        var frames = rate / 100;
        var bytes = new byte[frames * 8];
        for (var frame = 0; frame < frames; frame++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(frame * 8, 4), 0.25f);
            BitConverter.TryWriteBytes(bytes.AsSpan(frame * 8 + 4, 4), -0.125f);
        }
        for (var block = 0; block < 100; block++) append(bytes, bytes.Length, block * 100_000L);
        var count = (int)slotType.GetField("_count", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(slot)!;
        check(Math.Abs(count - 48000) <= 1, $"Resampling {rate}Hz drifts across buffer boundaries: {count}");
        Array.Clear(bytes);
        var samples = new float[count * 2];
        check(read(0, count, samples), "Float audio cannot be read");
        for (var frame = 2; frame < count; frame++) check(Math.Abs(samples[frame * 2] - 0.25f) < 0.00001 && Math.Abs(samples[frame * 2 + 1] + 0.125f) < 0.00001, $"Float{rate} channel values or buffer ownership changed");
    }
    log("PASS PCM8/16/24/32 mono, float stereo 44.1/48/96kHz, resampling continuity and captured buffer ownership");
    var display = CaptureSources.ListDisplays().First(d => d.IsPrimary);
    using var graphics = GraphicsDevice.CreateForMonitor(display.MonitorHandle);
    using var converter = new ColorConverter(graphics.Device, graphics.Context);
    using var source = graphics.Device.CreateTexture2D(new Vortice.Direct3D11.Texture2DDescription { Width = 1920, Height = 1080, MipLevels = 1, ArraySize = 1, Format = Vortice.DXGI.Format.B8G8R8A8_UNorm, SampleDescription = new Vortice.DXGI.SampleDescription(1, 0), Usage = Vortice.Direct3D11.ResourceUsage.Default, BindFlags = Vortice.Direct3D11.BindFlags.RenderTarget | Vortice.Direct3D11.BindFlags.ShaderResource });
    using var target = graphics.Device.CreateRenderTargetView(source);
    converter.Ensure(1920, 1080, 1920, 1080);
    graphics.Context.ClearRenderTargetView(target, new Vortice.Mathematics.Color4(0.8f, 0.2f, 0.4f, 1));
    using var first = converter.Convert(source);
    var original = converter.ReadNv12(first);
    for (var i = 0; i < 12; i++)
    {
        graphics.Context.ClearRenderTargetView(target, new Vortice.Mathematics.Color4(0.1f, 0.8f, 0.2f, 1));
        using var later = converter.Convert(source);
        check(!original.SequenceEqual(converter.ReadNv12(later)), "GPU output does not change with the scene");
    }
    check(original.SequenceEqual(converter.ReadNv12(first)), "A retained GPU frame was overwritten by later frames");
    foreach (var size in new[] { (1280, 720), (640, 360), (1920, 1080) })
    {
        converter.Ensure(1920, 1080, size.Item1, size.Item2);
        using var resized = converter.Convert(source);
        check(converter.ReadNv12(resized).Length == size.Item1 * size.Item2 * 3 / 2, "GPU readback was not recreated after resizing");
    }
    log("PASS retained GPU frames remain independent; 1080p/720p/360p conversion and software readback resizing");
}

static async Task MeasurePerformance(string directory, Action<string> log, Action<bool, string> check)
{
    var slotType = typeof(AudioEngine).GetNestedType("CaptureSlot", System.Reflection.BindingFlags.NonPublic)!;
    using (var slot = (IDisposable)Activator.CreateInstance(slotType, "benchmark", "Benchmark", false, 1d, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2))!)
    {
        var append = slotType.GetMethod("Append")!.CreateDelegate<Action<byte[], int, long>>(slot);
        var read = slotType.GetMethod("Read")!.CreateDelegate<Func<long, int, float[], bool>>(slot);
        var samples = new byte[960 * 8];
        var destination = new float[960 * 2];
        for (var i = 0; i < 960; i++) { BitConverter.TryWriteBytes(samples.AsSpan(i * 8, 4), 0.125f); BitConverter.TryWriteBytes(samples.AsSpan(i * 8 + 4, 4), -0.0625f); }
        for (var i = 0; i < 64; i++) { append(samples, samples.Length, i * 200_000L); read(i * 200_000L, 960, destination); }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 64; i < 1064; i++) { append(samples, samples.Length, i * 200_000L); read(i * 200_000L, 960, destination); }
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        check(Math.Abs(destination[0] - 0.125f) < 0.00001 && Math.Abs(destination[1] + 0.0625f) < 0.00001, "Audio conversion changes channel values");
        log($"audioBlocks=1000 elapsedMs={clock.Elapsed.TotalMilliseconds:F2} managedAllocatedBytes={allocated}");
    }
    using (var aac = new AacEncoder())
    {
        aac.Start();
        var pcm = new byte[1024 * 4];
        for (var i = 0; i < 64; i++) aac.WritePcm16(pcm, i * 1024L * TimeSpan.TicksPerSecond / 48000);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 64; i < 564; i++) aac.WritePcm16(pcm, i * 1024L * TimeSpan.TicksPerSecond / 48000);
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        aac.DrainAndStop();
        log($"aacFrames=500 elapsedMs={clock.Elapsed.TotalMilliseconds:F2} managedAllocatedBytes={allocated}");
    }
    var display = CaptureSources.ListDisplays().First(d => d.IsPrimary);
    using (var graphics = GraphicsDevice.CreateForMonitor(display.MonitorHandle))
    using (var converter = new ColorConverter(graphics.Device, graphics.Context))
    using (var source = graphics.Device.CreateTexture2D(new Vortice.Direct3D11.Texture2DDescription { Width = 1920, Height = 1080, MipLevels = 1, ArraySize = 1, Format = Vortice.DXGI.Format.B8G8R8A8_UNorm, SampleDescription = new Vortice.DXGI.SampleDescription(1, 0), Usage = Vortice.Direct3D11.ResourceUsage.Default, BindFlags = Vortice.Direct3D11.BindFlags.RenderTarget | Vortice.Direct3D11.BindFlags.ShaderResource }))
    using (var target = graphics.Device.CreateRenderTargetView(source))
    {
        converter.Ensure(1920, 1080, 1920, 1080);
        void Frame(int index, bool sync)
        {
            graphics.Context.ClearRenderTargetView(target, new Vortice.Mathematics.Color4(index % 2 == 0 ? 0.8f : 0.1f, 0.2f, 0.4f, 1));
            using var converted = converter.Convert(source);
            if (sync) check(converter.ReadNv12(converted).Length == 1920 * 1080 * 3 / 2, "NV12 conversion has incorrect dimensions");
        }
        for (var i = 0; i < 30; i++) Frame(i, i == 29);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 300; i++) Frame(i, i % 30 == 29);
        clock.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        log($"gpuConversions=300 elapsedMs={clock.Elapsed.TotalMilliseconds:F2} managedAllocatedBytes={allocated}");
    }
    GC.Collect();
    GC.WaitForPendingFinalizers();
    using var recorder = new Recorder(Path.Combine(directory, "sessions"));
    recorder.StartReplay(new RecordingRequest { Settings = new AppSettings { OutputDirectory = directory, Width = 1920, Height = 1080, FrameRate = 60, VideoBitrateKbps = 15000, CaptureCursor = false }, Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = display.DeviceName }, ReplayDuration = TimeSpan.FromMinutes(1), GameTitle = "Leistungstest" });
    await Task.Delay(2000);
    using var process = Process.GetCurrentProcess();
    var cpu = process.TotalProcessorTime;
    var allocations = GC.GetTotalAllocatedBytes(true);
    var elapsed = Stopwatch.StartNew();
    await Task.Delay(8000);
    elapsed.Stop();
    process.Refresh();
    var snapshot = recorder.GetSnapshot();
    log($"capture1920x1080fps60 cpuPercent={(process.TotalProcessorTime - cpu).TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100:F3} workingSetMB={process.WorkingSet64 / 1048576d:F1} privateMB={process.PrivateMemorySize64 / 1048576d:F1} allocatedMBperSecond={(GC.GetTotalAllocatedBytes(true) - allocations) / elapsed.Elapsed.TotalSeconds / 1048576d:F3} replayMB={snapshot.ReplayBytes / 1048576d:F2} encodedFps={snapshot.EncodedFps:F1} dropped={snapshot.DroppedFrames}");
    var clip = await recorder.SaveClipAsync();
    recorder.StopReplay();
    check(clip != null && MediaInspector.Probe(clip).HasVideo && MediaInspector.GetDuration(clip).TotalSeconds > 8, "Performance recording is invalid");
    check(snapshot.EncodedFps >= 58 && snapshot.DroppedFrames == 0, "Performance capture loses frames");
}

static (int VideoCount, int AudioCount, double VideoEnd, double AudioEnd, double MaxVideoDelta, double MaxAudioDelta) Inspect(string path)
{
    using var attrs = MediaFactory.MFCreateAttributes(1);
    attrs.Set(SinkWriterAttributeKeys.ReadwriteDisableConverters, 1u);
    using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attrs);
    var video = -1;
    var audio = -1;
    var streams = new HashSet<int>();
    for (var i = 0; i < 4; i++)
    {
        IMFMediaType type;
        try { type = reader.GetNativeMediaType(i, 0); }
        catch { break; }
        using (type)
        {
            type.GetGUID(MediaTypeAttributeKeys.MajorType, out var major);
            reader.SetCurrentMediaType(i, type);
            if (major == MediaTypeGuids.Video) video = i;
            if (major == MediaTypeGuids.Audio) audio = i;
            streams.Add(i);
        }
    }
    var ended = new HashSet<int>();
    var count = new int[4];
    var end = new long[4];
    var previous = Enumerable.Repeat(-1L, 4).ToArray();
    var maxDelta = new long[4];
    for (var attempt = 0; attempt < 20000 && ended.Count < streams.Count; attempt++)
    {
        using var sample = reader.ReadSample((int)SourceReaderIndex.AnyStream, SourceReaderControlFlag.None, out var stream, out var flags, out var timestamp);
        if (flags.HasFlag(SourceReaderFlag.Error)) throw new Exception("Saved video cannot be read");
        if (flags.HasFlag(SourceReaderFlag.EndOfStream)) ended.Add(stream);
        if (sample == null) continue;
        if (previous[stream] >= 0)
        {
            if (timestamp <= previous[stream]) throw new Exception("Non-monotonic saved timestamps");
            maxDelta[stream] = Math.Max(maxDelta[stream], timestamp - previous[stream]);
        }
        previous[stream] = timestamp;
        end[stream] = timestamp + sample.SampleDuration;
        count[stream]++;
    }
    return (video < 0 ? 0 : count[video], audio < 0 ? 0 : count[audio], video < 0 ? 0 : end[video] / 1e7, audio < 0 ? 0 : end[audio] / 1e7, video < 0 ? 0 : maxDelta[video] / 1e7, audio < 0 ? 0 : maxDelta[audio] / 1e7);
}

static (double Peak, double Rms, long Count) DecodeAudio(string path, double begin = 0, double finish = double.MaxValue)
{
    using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null!);
    reader.SetStreamSelection((int)SourceReaderIndex.AllStreams, false);
    reader.SetStreamSelection((int)SourceReaderIndex.FirstAudioStream, true);
    using var type = MediaFactory.MFCreateMediaType();
    type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
    type.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
    type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
    type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
    type.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
    reader.SetCurrentMediaType((int)SourceReaderIndex.FirstAudioStream, type);
    double peak = 0, sum = 0;
    long count = 0;
    for (var block = 0; block < 200000; block++)
    {
        using var sample = reader.ReadSample((int)SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out var timestamp);
        if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
        if (sample == null) continue;
        if (timestamp / 1e7 >= finish) break;
        if (timestamp / 1e7 < begin) continue;
        using var buffer = sample.ConvertToContiguousBuffer();
        var pcm = H264Encoder.ReadBuffer(buffer);
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var value = BitConverter.ToInt16(pcm, i) / 32768d;
            peak = Math.Max(peak, Math.Abs(value));
            sum += value * value;
            count++;
        }
    }
    return (peak, count == 0 ? 0 : Math.Sqrt(sum / count), count);
}
