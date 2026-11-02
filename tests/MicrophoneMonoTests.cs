using System.Reflection;
using NAudio.Wave;
using Spezi.Core.Recording;
using Vortice.MediaFoundation;

static class MicrophoneMonoTests
{
    static readonly Type SlotType = typeof(AudioEngine).GetNestedType("CaptureSlot", BindingFlags.NonPublic)!;

    public static void Run(string directory, Action<string> log, Action<bool, string> check)
    {
        foreach (var rate in new[] { 44100, 48000, 96000 })
        foreach (var format in new[]
        {
            new WaveFormat(rate, 8, 2), new WaveFormat(rate, 16, 2),
            new WaveFormat(rate, 24, 2), new WaveFormat(rate, 32, 2),
            WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)
        })
        foreach (var levels in new[] { new[] { .25f, 0f }, new[] { 0f, .25f }, new[] { .25f, .25f }, new[] { .25f, -.125f } })
        {
            var samples = Convert(format, true, Constant(format, levels));
            var expected = (levels[0] + levels[1]) / 2;
            check(samples.Length >= 9598, "Resampling unexpectedly drops microphone samples");
            for (var frame = 2; frame < samples.Length / 2; frame++)
                check(Math.Abs(samples[frame * 2] - expected) < .0001f && samples[frame * 2] == samples[frame * 2 + 1],
                    $"Microphone is not centered for {format}, input {levels[0]}/{levels[1]}");
        }
        log("PASS Left-only, right-only and two-channel microphones are centered: PCM8/16/24/32 and float at 44.1/48/96kHz");
        foreach (var channels in new[] { 1, 6 })
        {
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, channels);
            var levels = Enumerable.Repeat(.25f, channels).ToArray();
            var samples = Convert(format, true, Constant(format, levels));
            check(samples.All(s => Math.Abs(s - .25f) < .00001f), "Mono/multichannel microphone gain is wrong");
        }
        var extensible = new WaveFormatExtensible(48000, 32, 2);
        var extendedSamples = Convert(extensible, true, Constant(extensible, [.25f, 0]));
        check(extendedSamples.All(s => Math.Abs(s - .125f) < .00001f), "WASAPI extensible float bypasses microphone centering");
        var stereoFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var stereo = Convert(stereoFormat, false, Constant(stereoFormat, [.25f, -.125f]));
        for (var frame = 0; frame < stereo.Length / 2; frame++)
            check(stereo[frame * 2] == .25f && stereo[frame * 2 + 1] == -.125f, "Game/system stereo was changed by microphone centering");
        log("PASS Mono, multichannel and WASAPI extensible microphones; game and system audio retain separate stereo channels");

        var input = new byte[48000 * 8];
        for (var frame = 0; frame < 48000; frame++)
            BitConverter.TryWriteBytes(input.AsSpan(frame * 8, 4), (float)(.3 * Math.Sin(2 * Math.PI * 440 * frame / 48000)));
        var centered = Convert(stereoFormat, true, input);
        using var encoder = new AacEncoder();
        var packets = new List<EncodedPacket>();
        encoder.Packet += packets.Add;
        encoder.Start();
        var pcm = new byte[centered.Length * 2];
        for (var i = 0; i < centered.Length; i++)
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2, 2), (short)Math.Clamp((int)(centered[i] * 32767), short.MinValue, short.MaxValue));
        encoder.WritePcm16(pcm, 0);
        encoder.DrainAndStop();
        var path = Path.Combine(directory, "centered-microphone.mp4");
        using (var writer = MediaFactory.MFCreateSinkWriterFromURL(path, null!, null!))
        {
            var stream = writer.AddStream(encoder.OutputType!);
            writer.SetInputMediaType(stream, encoder.OutputType!, null!);
            writer.BeginWriting();
            foreach (var packet in packets)
            {
                using var buffer = MediaFactory.MFCreateMemoryBuffer(packet.Data.Length);
                H264Encoder.WriteBuffer(buffer, packet.Data);
                using var sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                sample.SampleTime = packet.Timestamp;
                sample.SampleDuration = packet.Duration;
                writer.WriteSample(stream, sample);
            }
            writer.Finalize();
        }
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null!);
        using var decodedType = MediaFactory.MFCreateMediaType();
        decodedType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        decodedType.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        decodedType.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48000u);
        decodedType.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        decodedType.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        reader.SetCurrentMediaType((int)SourceReaderIndex.FirstAudioStream, decodedType);
        double leftPower = 0, rightPower = 0, difference = 0;
        long frames = 0;
        for (var i = 0; i < 1000; i++)
        {
            using var sample = reader.ReadSample((int)SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out _);
            if (flags.HasFlag(SourceReaderFlag.EndOfStream)) break;
            if (sample == null) continue;
            using var buffer = sample.ConvertToContiguousBuffer();
            var decoded = H264Encoder.ReadBuffer(buffer);
            for (var offset = 0; offset + 3 < decoded.Length; offset += 4)
            {
                var left = BitConverter.ToInt16(decoded, offset) / 32768d;
                var right = BitConverter.ToInt16(decoded, offset + 2) / 32768d;
                leftPower += left * left; rightPower += right * right;
                difference += (left - right) * (left - right); frames++;
            }
        }
        check(frames > 40000 && leftPower / frames > .005 && rightPower / frames > .005,
            "Encoded microphone is inaudible on one side");
        check(Math.Abs(leftPower / rightPower - 1) < .01 && difference / Math.Max(leftPower, rightPower) < .001,
            "AAC/MP4 does not preserve the centered microphone");
        log($"PASS AAC/MP4 round trip: {frames} decoded frames; left RMS={Math.Sqrt(leftPower / frames):F4}, right RMS={Math.Sqrt(rightPower / frames):F4}");
    }

    static float[] Convert(WaveFormat format, bool microphone, byte[] bytes)
    {
        using var slot = (IDisposable)Activator.CreateInstance(SlotType, ["fixture", "Fixture", microphone, 1d, format, null])!;
        var append = SlotType.GetMethod("Append")!.CreateDelegate<Action<byte[], int, long>>(slot);
        var read = SlotType.GetMethod("Read")!.CreateDelegate<Func<long, int, float[], bool>>(slot);
        append(bytes, bytes.Length, 0);
        var count = (int)SlotType.GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slot)!;
        var samples = new float[count * 2];
        if (!read(0, count, samples)) throw new Exception("No converted microphone samples");
        return samples;
    }

    static byte[] Constant(WaveFormat format, float[] values)
    {
        var bytesPerSample = format.BitsPerSample / 8;
        var bytes = new byte[format.SampleRate / 10 * format.Channels * bytesPerSample];
        for (var offset = 0; offset < bytes.Length; offset += bytesPerSample)
        {
            var value = values[offset / bytesPerSample % format.Channels];
            if (format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible)
                BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);
            else if (bytesPerSample == 1) bytes[offset] = (byte)(128 + value * 128);
            else if (bytesPerSample == 2) BitConverter.TryWriteBytes(bytes.AsSpan(offset, 2), (short)(value * 32768));
            else if (bytesPerSample == 3)
            {
                var sample = (int)(value * 8388608);
                bytes[offset] = (byte)sample; bytes[offset + 1] = (byte)(sample >> 8); bytes[offset + 2] = (byte)(sample >> 16);
            }
            else BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), (int)(value * 2147483648));
        }
        return bytes;
    }
}
