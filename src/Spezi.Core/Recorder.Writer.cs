using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Spezi.Core.Recording;

namespace Spezi.Core;

// MP4 writing helpers for saved replay clips (split out of Recorder.cs, unchanged).
public sealed partial class Recorder
{
    static void WritePackets(string path, IMFMediaType videoType, IMFMediaType? audioType, EncodedPacket[] video, EncodedPacket[] audio)
    {
        using var attributes = SegmentedSession.CreateWriterAttributes();
        var writer = MediaFactory.MFCreateSinkWriterFromURL(path, null!, attributes);
        try
        {
            var videoStream = writer.AddStream(videoType);
            writer.SetInputMediaType(videoStream, videoType, null!);
            var audioStream = -1;
            if (audioType != null && audio.Length > 0)
            {
                audioStream = writer.AddStream(audioType);
                writer.SetInputMediaType(audioStream, audioType, null!);
            }
            writer.BeginWriting();
            var origin = video[0].Timestamp;
            var merged = new List<(EncodedPacket Packet, int Stream)>(video.Length + audio.Length);
            foreach (var packet in video)
                merged.Add((packet, videoStream));
            if (audioStream >= 0)
            {
                foreach (var packet in audio)
                    merged.Add((packet, audioStream));
            }
            merged.Sort((a, b) => a.Packet.Timestamp.CompareTo(b.Packet.Timestamp));
            var last = new long[8];
            for (var i = 0; i < last.Length; i++)
                last[i] = -1;
            foreach (var item in merged)
            {
                var time = item.Packet.Timestamp - origin;
                if (time < 0 || item.Stream >= last.Length || time <= last[item.Stream])
                    continue;
                last[item.Stream] = time;
                WriteOne(writer, item.Stream, item.Packet, time);
            }
            writer.Finalize();
        }
        finally
        {
            writer.Dispose();
        }
    }

    static IMFMediaType? CloneType(IMFMediaType? source)
    {
        if (source == null)
            return null;
        var clone = MediaFactory.MFCreateMediaType();
        source.CopyAllItems(clone);
        return clone;
    }

    static string PendingPath(string path)
    {
        var directory = Path.Combine(Path.GetDirectoryName(path)!, ".spezi-pending");
        System.IO.Directory.CreateDirectory(directory);
        return Path.Combine(directory, Path.GetFileName(path));
    }

    static void WriteOne(IMFSinkWriter writer, int stream, EncodedPacket packet, long timestamp)
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
        writer.WriteSample(stream, sample);
        sample.Dispose();
    }
}
