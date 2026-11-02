using Spezi.Core;
using Spezi.Core.Recording;

var log = Path.Combine(Path.GetTempPath(), "spezi-probe.txt");
void Line(string text)
{
    Console.WriteLine(text);
    File.AppendAllText(log, text + Environment.NewLine);
}
File.WriteAllText(log, "");
AppDomain.CurrentDomain.UnhandledException += (_, e) => Line("UNHANDLED " + e.ExceptionObject);

var recorder = new Recorder();
recorder.Notice += (_, notice) => Line($"NOTICE {notice.Kind}: {notice.Message}");

Line("Encoders:");
foreach (var encoder in recorder.DetectEncoders())
    Line($"  {encoder.Name} hardware={encoder.IsHardware} vendor={encoder.Vendor}");
Line("Software available: " + recorder.SoftwareEncoderAvailable());

var displays = recorder.ListDisplays();
foreach (var display in displays)
    Line($"Display {display.Name} {display.Width}x{display.Height} {display.DeviceName}");

var settings = new AppSettings();
var native = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
AppSettings.ApplyProfile(settings, QualityProfile.Economy, native.Width, native.Height);
settings.AllowSoftwareEncoder = true;
settings.OutputDirectory = Path.Combine(Path.GetTempPath(), "SpeziProbe");
settings.CaptureCursor = false;
Directory.CreateDirectory(settings.OutputDirectory);

var request = new RecordingRequest
{
    Settings = settings,
    Capture = new CaptureSelection { Kind = CaptureKind.Monitor, MonitorDeviceName = native.DeviceName },
    ReplayDuration = TimeSpan.FromSeconds(8)
};

try
{
    Line("Starting replay...");
    recorder.StartReplay(request);
    Thread.Sleep(TimeSpan.FromSeconds(4));
    Line("Starting session...");
    recorder.StartSession(request);
    Thread.Sleep(TimeSpan.FromSeconds(6));
    var snapshot = recorder.GetSnapshot();
    Line($"Snapshot encoder={snapshot.EncoderName} software={snapshot.UsingSoftwareEncoder} captureFps={snapshot.CaptureFps:0.0} encodedFps={snapshot.EncodedFps:0.0} dropped={snapshot.DroppedFrames} replay={snapshot.ReplayBuffered} levels={snapshot.Levels.Count}");
    Line("Saving clip...");
    var clip = await recorder.SaveClipAsync();
    Line("Clip: " + clip);
    var session = await recorder.StopSessionAsync();
    Line("Session: " + session);
    recorder.StopReplay();
    foreach (var file in new[] { clip, session })
    {
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
        {
            Line("MISSING " + file);
            continue;
        }
        var info = new FileInfo(file);
        var probe = MediaInspector.Probe(file);
        Line($"{info.Name} bytes={info.Length} video={probe.HasVideo} audio={probe.HasAudio} duration={probe.Duration}");
    }
}
catch (Exception ex)
{
    Line("FAILED " + ex);
}
finally
{
    recorder.Dispose();
    Line("done");
}
