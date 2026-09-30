namespace Pb.Sim.Gear;

public enum FireMode : byte
{
    Semi,
    Ramping,
}

public sealed class FireControlParams
{
    /// <summary>Hard cap on balls per second (default 10.5).</summary>
    public required float RateCap { get; init; }

    /// <summary>Ramping engages on the next fast pull after this many consecutive fast pulls.</summary>
    public required int RampAfterShots { get; init; }

    /// <summary>Pulls must come at least this often (Hz) to count as "fast" and to sustain ramping.</summary>
    public required float RampMinPullRate { get; init; }

    /// <summary>Pulls that arrive while the rate cap is blocking are queued up to this many.</summary>
    public required int BufferedPulls { get; init; }

    public required FireMode DefaultMode { get; init; }
}

/// <summary>
/// Trigger logic of an electronic marker. Works in continuous time so the cap is exact even though
/// 120 Hz ticks don't divide evenly by 10.5 bps: each tick it returns the shots that happen inside
/// [t0, t0 + dt) as offsets from t0.
/// <list type="bullet">
/// <item>Semi: one shot per pull (pulls faster than the cap are buffered).</item>
/// <item>Ramping: after <see cref="FireControlParams.RampAfterShots"/> fast pulls, the next fast
/// pull starts firing at the cap for as long as pulls keep coming at ≥ the minimum rate.</item>
/// </list>
/// </summary>
public sealed class FireControl
{
    private bool _previousTrigger;
    private double _nextShotTime = double.NegativeInfinity;
    private double _lastPullTime = double.NegativeInfinity;
    private int _fastPulls;
    private int _buffered;

    public FireControl(FireControlParams parameters)
    {
        Params = parameters;
        Mode = parameters.DefaultMode;
    }

    public FireControlParams Params { get; set; }

    public FireMode Mode { get; set; }

    public bool IsRamping { get; private set; }

    public void Reset()
    {
        _previousTrigger = false;
        _nextShotTime = double.NegativeInfinity;
        _lastPullTime = double.NegativeInfinity;
        _fastPulls = 0;
        _buffered = 0;
        IsRamping = false;
    }

    /// <param name="t0">Sim time at the start of this tick (s).</param>
    /// <param name="dt">Tick length (s).</param>
    /// <param name="trigger">Trigger held this tick.</param>
    /// <param name="blocked">Firing impossible (sprinting, refilling, empty, eliminated…).</param>
    /// <param name="shotOffsets">Receives each shot's time offset from <paramref name="t0"/>.</param>
    /// <returns>Number of shots this tick.</returns>
    public int Update(double t0, double dt, bool trigger, bool blocked, Span<float> shotOffsets)
    {
        bool pull = trigger && !_previousTrigger;
        _previousTrigger = trigger;

        if (blocked)
        {
            IsRamping = false;
            _fastPulls = 0;
            _buffered = 0;
            return 0;
        }

        double interval = 1.0 / Params.RateCap;
        double sustainWindow = 1.0 / Params.RampMinPullRate;
        double t1 = t0 + dt;

        if (pull)
        {
            bool fast = t0 - _lastPullTime <= sustainWindow + 1e-9;
            _fastPulls = fast ? _fastPulls + 1 : 1;
            _lastPullTime = t0;
            if (Mode == FireMode.Ramping && !IsRamping && _fastPulls > Params.RampAfterShots)
            {
                IsRamping = true;
            }

            if (!IsRamping)
            {
                _buffered = Math.Min(_buffered + 1, Math.Max(1, Params.BufferedPulls));
            }
        }

        int count = 0;
        if (IsRamping)
        {
            double sustainUntil = _lastPullTime + sustainWindow;
            if (Mode != FireMode.Ramping || t0 > sustainUntil)
            {
                IsRamping = false;
                _fastPulls = 0;
            }
            else
            {
                double shotTime = Math.Max(_nextShotTime, t0);
                double end = Math.Min(t1, sustainUntil + 1e-9);
                while (shotTime < end && count < shotOffsets.Length)
                {
                    shotOffsets[count++] = (float)(shotTime - t0);
                    shotTime += interval;
                }

                if (count > 0)
                {
                    _nextShotTime = shotTime;
                }

                _buffered = 0;
                return count;
            }
        }

        if (_buffered > 0 && shotOffsets.Length > 0)
        {
            double shotTime = Math.Max(_nextShotTime, t0);
            if (shotTime < t1)
            {
                shotOffsets[count++] = (float)(shotTime - t0);
                _nextShotTime = shotTime + interval;
                _buffered--;
            }
        }

        return count;
    }
}
