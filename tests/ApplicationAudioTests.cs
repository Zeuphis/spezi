using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Spezi.Core;
using Spezi.Core.Recording;
using Vortice.MediaFoundation;

static class ApplicationAudioTests
{
    public static async Task Worker(string[] args)
    {
        var frequency = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        var duration = int.Parse(args[2]);
        Process? child = null;
        var elapsed = Stopwatch.StartNew();
        var firstStart = true;
        try
        {
            while (elapsed.Elapsed.TotalSeconds < duration)
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
                using var playback = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
                var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                playback.PlaybackStopped += (_, state) => stopped.TrySetResult(state.Exception);
                var signal = new SignalGenerator(48000, 2) { Type = SignalGeneratorType.Sin, Frequency = frequency, Gain = 0.035 };
                playback.Init(signal); playback.Play();
                if (firstStart)
                {
                    File.WriteAllText(args[3], Environment.ProcessId.ToString());
                    if (args.Length > 4) child = StartWorker(args[4], 660, duration, args[3] + ".child");
                    firstStart = false;
                }
                await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(Math.Max(0, duration - elapsed.Elapsed.TotalSeconds))));
                playback.Stop();
                if (stopped.Task.IsCompletedSuccessfully && stopped.Task.Result != null)
                    File.AppendAllText(args[3] + ".audio-error", DateTime.Now.ToString("O") + " " + stopped.Task.Result + Environment.NewLine);
                // Real apps recreate their render stream when Windows invalidates an endpoint.
                // The fixture must do the same rather than remain alive while silently stopped.
                if (elapsed.Elapsed.TotalSeconds < duration) await Task.Delay(200);
            }
        }
        finally { StopWorker(child); }
    }

    public static void CheckSettingsAndBuffer(Action<string> log)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); log("PASS " + message); }
        Check(SettingsStore.Deserialize("{\"ReplayMinutes\":1}").ReplaySeconds == 60 && SettingsStore.Deserialize("{\"rEpLaYmInUtEs\":5}").ReplaySeconds == 300, "legacy minute preferences migrate to seconds, including case-insensitive settings");
        Check(SettingsStore.Deserialize("{\"ReplaySeconds\":75,\"ReplayMinutes\":4}").ReplaySeconds == 75 && SettingsStore.Deserialize("{}").ReplaySeconds == 180, "second preferences take precedence and retain the existing default");
        Check(SettingsStore.Deserialize("{\"ReplaySeconds\":1}").ReplaySeconds == 15 && SettingsStore.Deserialize("{\"ReplaySeconds\":10000}").ReplaySeconds == 300 && SettingsStore.Deserialize("{\"ReplaySeconds\":79}").ReplaySeconds == 75, "replay duration clamps and snaps to 15-second steps");
        var settings = new AppSettings { ReplaySeconds = 45, AudioMode = AudioCaptureMode.Applications, AudioApplicationIds = [AudioApplicationEntry.DiscordId, AudioApplicationEntry.TeamSpeakId] };
        var restored = SettingsStore.Deserialize(System.Text.Json.JsonSerializer.Serialize(settings));
        Check(restored.ReplaySeconds == 45 && restored.AudioMode == AudioCaptureMode.Applications && restored.AudioApplicationIds.SequenceEqual(settings.AudioApplicationIds), "app audio selection and sub-minute replay survive saving and reopening");
        var buffer = new ReplayBuffer();
        for (var window = 15; window <= 300; window += 15)
        {
            buffer.Clear();
            buffer.Configure(TimeSpan.FromSeconds(window), 6000);
            for (var seconds = 0; seconds <= 320; seconds++)
                buffer.Add(new EncodedPacket { Timestamp = seconds * TimeSpan.TicksPerSecond, Duration = TimeSpan.TicksPerSecond, Data = [1], IsKeyframe = true });
            Check(buffer.Buffered == TimeSpan.FromSeconds(window), $"compressed replay buffer retains {window} seconds");
        }
        buffer.Configure(TimeSpan.FromSeconds(15), 6000);
        Check(buffer.Buffered == TimeSpan.FromSeconds(15), "reducing replay duration trims an already running buffer immediately");
    }

    public static async Task RunCapture(string root, Action<string> log)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); log("PASS " + message); }
        Check(ProcessLoopbackCapture.IsSupported, "Windows supports process audio capture");
        var directory = Path.GetDirectoryName(Environment.ProcessPath)!;
        var fixture = Path.Combine(directory, "Spezi.RecordingTests.exe");
        var includedPath = Path.Combine(directory, "SpeziAudioIncluded.exe");
        var excludedPath = Path.Combine(directory, "SpeziAudioExcluded.exe");
        var childPath = Path.Combine(directory, "SpeziAudioChild.exe");
        foreach (var target in new[] { includedPath, excludedPath, childPath }) File.Copy(fixture, target, true);
        Process? excluded = null, included = null;
        using var recorder = new Recorder(Path.Combine(root, "sessions"));
        try
        {
            excluded = StartWorker(excludedPath, 1600, 35, Path.Combine(root, "excluded.ready"));
            await WaitReady(Path.Combine(root, "excluded.ready"), excluded);
            var display = recorder.ListDisplays().First(d => d.IsPrimary);
            var settings = new AppSettings
            {
                OutputDirectory = root, ReplaySeconds = 15, Width = 640, Height = 360, FrameRate = 30, VideoBitrateKbps = 4000,
                CaptureCursor = false, CaptureSystemAudio = true, RecordMicrophone = false,
                AudioMode = AudioCaptureMode.Applications, AudioApplicationIds = ["exe:speziaudioincluded", "exe:speziaudiochild"]
            };
            var request = new RecordingRequest { Settings = settings, Capture = new() { Kind = CaptureKind.Monitor, MonitorDeviceName = display.DeviceName }, ReplayDuration = TimeSpan.FromSeconds(15), GameTitle = "Audio Test" };
            recorder.StartAudioTest(settings);
            Check(recorder.GetSnapshot().AudioTestActive, "audio test starts with selected-app sources");
            recorder.StartReplay(request);
            Check(!recorder.GetSnapshot().AudioTestActive && recorder.GetSnapshot().PipelineRunning, "starting replay closes the audio test and starts actual video capture");
            recorder.StartSession(request);
            await Task.Delay(3000);
            included = StartWorker(includedPath, 440, 30, Path.Combine(root, "included.ready"), childPath);
            await WaitReady(Path.Combine(root, "included.ready"), included);
            await WaitReady(Path.Combine(root, "included.ready.child"), included);
            await Task.Delay(3000);
            var targets = ApplicationAudioCatalog.ResolveTargets(settings.AudioApplicationIds, ApplicationAudioCatalog.Snapshot(), 0);
            Check(targets.Count == 1 && targets[0].Id == included.Id, "selected parent and child are captured once as a single process tree");
            Check(recorder.GetSnapshot().Levels.Any(l => l.DeviceId == "exe:speziaudioincluded" && l.Peak > 0.001), "an app started after recording is automatically attached and has a live level");
            settings.DeviceGains["exe:speziaudioincluded"] = 0.5;
            recorder.UpdateGains(settings);
            StopWorker(included); included = null;
            await Task.Delay(4000);
            var restartAt = recorder.GetSnapshot().SessionElapsed.TotalSeconds;
            included = StartWorker(includedPath, 440, 30, Path.Combine(root, "restart.ready"), childPath);
            await WaitReady(Path.Combine(root, "restart.ready"), included);
            await Task.Delay(9000);
            log("Restart levels: " + string.Join("; ", recorder.GetSnapshot().Levels.Select(l => $"{l.DeviceId} active={l.Active} peak={l.Peak:0.000000}")));
            log("Restart targets: " + string.Join("; ", ApplicationAudioCatalog.ResolveTargets(settings.AudioApplicationIds, ApplicationAudioCatalog.Snapshot(), 0).Select(t => $"{t.SourceId} PID={t.Id}")));
            Check(recorder.GetSnapshot().Levels.Any(l => l.DeviceId == "exe:speziaudioincluded" && l.Active), "restarting a selected app reattaches audio without restarting video capture");
            var clip = await recorder.SaveClipAsync();
            var session = await recorder.StopSessionAsync();
            recorder.StopReplay();
            Check(clip != null && session != null && File.Exists(clip) && File.Exists(session), "app-filtered replay and full session are saved");
            var clipProbe = MediaInspector.Probe(clip!);
            Check(clipProbe.HasVideo && clipProbe.HasAudio && clipProbe.Duration.TotalSeconds is >= 12 and <= 15.5, $"15-second replay is playable with audio ({clipProbe.Duration.TotalSeconds:0.00}s, keyframe aligned)");
            var silence = Analyze(session!, 0.5, 2.3);
            var quietGap = Analyze(session!, restartAt - 2.0, restartAt - 0.5);
            var sound = Analyze(session!, restartAt + 3, restartAt + 7);
            log($"Audio analysis: early RMS={silence.Rms:F6}, app-exit gap RMS={quietGap.Rms:F6}, selected RMS={sound.Rms:F6}, selected440={sound.Included:F6}, child660={sound.Child:F6}, excluded1600={sound.Excluded:F6}");
            Check(silence.Rms < 0.0002 && quietGap.Rms < 0.0002, "unselected app stays silent before the selected app starts and while it is closed");
            Check(sound.Included > 0.004 && sound.Child > 0.004 && sound.Excluded < sound.Included * 0.04, "MP4 includes selected app and child audio while excluding a simultaneous app on the same output device");
            Check(sound.Included is > 0.010 and < 0.024, "live per-app volume changes survive the app restarting");
            var replaySound = Analyze(clip!, 2, clipProbe.Duration.TotalSeconds - 1);
            Check(replaySound.Included > 0.001 && replaySound.Excluded < replaySound.Included * 0.04, "saved replay also excludes the unselected app");
            log("APP_AUDIO_CLIP=" + clip);
            log("APP_AUDIO_SESSION=" + session);
            log("APPLICATION_AUDIO_TEST_OK");
        }
        finally { StopWorker(included); StopWorker(excluded); }
    }

    static Process StartWorker(string executable, int frequency, int seconds, string ready, string? child = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "--audio-fixture", frequency.ToString(), seconds.ToString(), ready }) start.ArgumentList.Add(argument);
        if (child != null) start.ArgumentList.Add(child);
        return Process.Start(start)!;
    }
    static async Task WaitReady(string path, Process process)
    {
        var timeout = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (process.HasExited || timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new Exception("Audio fixture did not start: " + path);
            await Task.Delay(50);
        }
    }
    static void StopWorker(Process? process)
    {
        if (process == null) return;
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } }
        finally { process.Dispose(); }
    }

    internal static double ToneLevel(string path, double frequency, double begin, double end) => Analyze(path, begin, end, frequency).Included;

    static (double Rms, double Included, double Child, double Excluded) Analyze(string path, double begin, double end, double includedFrequency = 440)
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
        var frequencies = new[] { includedFrequency, 660, 1600 };
        var sine = new double[3]; var cosine = new double[3];
        double squares = 0; long frames = 0;
        while (true)
        {
            using var sample = reader.ReadSample((int)SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out var timestamp);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream) || timestamp / 1e7 >= end) break;
            if (sample == null) continue;
            using var buffer = sample.ConvertToContiguousBuffer();
            var pcm = H264Encoder.ReadBuffer(buffer);
            for (var i = 0; i + 3 < pcm.Length; i += 4)
            {
                var time = timestamp / 1e7 + i / 4d / 48000;
                if (time < begin || time >= end) continue;
                var value = BitConverter.ToInt16(pcm, i) / 32768d;
                squares += value * value; frames++;
                for (var f = 0; f < 3; f++) { var angle = 2 * Math.PI * frequencies[f] * time; sine[f] += value * Math.Sin(angle); cosine[f] += value * Math.Cos(angle); }
            }
        }
        if (frames == 0) throw new Exception("No decoded audio in test interval.");
        double Amplitude(int index) => 2 * Math.Sqrt(sine[index] * sine[index] + cosine[index] * cosine[index]) / frames;
        return (Math.Sqrt(squares / frames), Amplitude(0), Amplitude(1), Amplitude(2));
    }
}
