namespace Spezi.Core.Recording;

// Counts capture problems per second of media time so a saved clip can report
// what happened inside its own range. Only written when something goes wrong,
// so a healthy capture never takes the lock and never allocates.
internal sealed class FrameStats
{
    // More seconds than the longest replay window (300 s).
    const int Slots = 512;

    readonly object _gate = new();
    readonly long[] _second = new long[Slots];
    readonly int[] _duplicated = new int[Slots];
    readonly int[] _late = new int[Slots];
    readonly int[] _encoderDropped = new int[Slots];
    long _totalDuplicated;
    long _totalLate;
    long _totalEncoderDropped;

    public void Add(long timestamp, int duplicated, int late, int encoderDropped)
    {
        var second = Math.Max(0, timestamp) / TimeSpan.TicksPerSecond;
        var slot = (int)(second % Slots);
        lock (_gate)
        {
            // Store second + 1 so that zero marks an unused slot.
            if (_second[slot] != second + 1)
            {
                _second[slot] = second + 1;
                _duplicated[slot] = 0;
                _late[slot] = 0;
                _encoderDropped[slot] = 0;
            }
            _duplicated[slot] += duplicated;
            _late[slot] += late;
            _encoderDropped[slot] += encoderDropped;
            _totalDuplicated += duplicated;
            _totalLate += late;
            _totalEncoderDropped += encoderDropped;
        }
    }

    public (int Duplicated, int Late, int EncoderDropped) Sum(long from, long to)
    {
        var first = Math.Max(0, from) / TimeSpan.TicksPerSecond;
        var last = Math.Max(0, to) / TimeSpan.TicksPerSecond;
        if (last - first >= Slots)
            first = last - Slots + 1;
        int duplicated = 0, late = 0, encoderDropped = 0;
        lock (_gate)
        {
            for (var second = first; second <= last; second++)
            {
                var slot = (int)(second % Slots);
                if (_second[slot] != second + 1)
                    continue;
                duplicated += _duplicated[slot];
                late += _late[slot];
                encoderDropped += _encoderDropped[slot];
            }
        }
        return (duplicated, late, encoderDropped);
    }

    public (long Duplicated, long Late, long EncoderDropped) Totals
    {
        get { lock (_gate) return (_totalDuplicated, _totalLate, _totalEncoderDropped); }
    }
}
