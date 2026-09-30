namespace Pb.Sim.Core;

/// <summary>
/// PCG-XSH-RR 64/32 random generator. Integer-only arithmetic, so a given seed produces the
/// same sequence on every platform. That is what lets clients reproduce a server shot from
/// its seed (dispersion, velocity variance, break rolls).
/// </summary>
public struct Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private ulong _increment;

    public Pcg32(ulong seed, ulong stream = 0xDA3E39CB94B95BDBUL)
    {
        _state = 0;
        _increment = (stream << 1) | 1UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Uniform float in [0, 1) with 24 bits of precision.</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>Uniform float in [min, max).</summary>
    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Uniform float in [-halfWidth, +halfWidth).</summary>
    public float Symmetric(float halfWidth) => (NextFloat() * 2f - 1f) * halfWidth;
}
