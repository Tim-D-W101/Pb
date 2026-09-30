namespace Pb.Sim.Core;

/// <summary>Deterministic seed derivation (SplitMix64 finaliser).</summary>
public static class SeedHash
{
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public static ulong Combine(ulong a, ulong b) => Mix(a ^ Mix(b));

    /// <summary>
    /// Seed for one shot. Server and clients derive the same value from data they both know
    /// (match seed, shooter id, shooter's shot counter), so no seed needs to be sent.
    /// </summary>
    public static ulong Shot(ulong matchSeed, int shooterId, uint shotSequence) =>
        Combine(Combine(matchSeed, unchecked((ulong)shooterId)), shotSequence);
}
