namespace Pb.Sim.Gear;

public sealed class LoaderParams
{
    public required int Capacity { get; init; }

    public required int PodCount { get; init; }

    public required int PodCapacity { get; init; }

    /// <summary>Seconds to refill the loader from one pod.</summary>
    public required float RefillTime { get; init; }

    public required bool CancelOnFire { get; init; }

    public required bool CancelOnSprint { get; init; }

    /// <summary>If true, a cancelled refill transfers balls in proportion to elapsed time.</summary>
    public required bool PartialOnInterrupt { get; init; }
}

/// <summary>Paint on the marker plus the pods on the player's harness.</summary>
public sealed class PaintSupply
{
    private int[] _pods;

    public PaintSupply(LoaderParams parameters)
    {
        Params = parameters;
        _pods = new int[parameters.PodCount];
        Fill();
    }

    public LoaderParams Params { get; private set; }

    public int Loader { get; internal set; }

    public ReadOnlySpan<int> Pods => _pods;

    public int PodsRemaining
    {
        get
        {
            int total = 0;
            foreach (int p in _pods)
            {
                total += p;
            }

            return total;
        }
    }

    public bool LoaderFull => Loader >= Params.Capacity;

    /// <summary>Index of the pod the next refill will use, or -1 if all are empty.</summary>
    public int NextPod()
    {
        for (int i = 0; i < _pods.Length; i++)
        {
            if (_pods[i] > 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Moves up to <paramref name="maxBalls"/> from a pod into the loader. Returns balls moved.</summary>
    public int Transfer(int pod, int maxBalls)
    {
        int moved = Math.Min(Math.Min(_pods[pod], Params.Capacity - Loader), Math.Max(0, maxBalls));
        _pods[pod] -= moved;
        Loader += moved;
        return moved;
    }

    public void Fill()
    {
        Loader = Params.Capacity;
        Array.Fill(_pods, Params.PodCapacity);
    }

    /// <summary>Applies new parameters (hot reload) and refills.</summary>
    public void Reconfigure(LoaderParams parameters)
    {
        Params = parameters;
        _pods = new int[parameters.PodCount];
        Fill();
    }
}

/// <summary>The 2.5 s pod-to-loader refill action (spec §1.4). Blocks firing while active.</summary>
public sealed class Refill
{
    private int _pod = -1;

    public bool Active { get; private set; }

    public float Elapsed { get; private set; }

    public float Progress(LoaderParams p) => Active ? Math.Clamp(Elapsed / p.RefillTime, 0f, 1f) : 0f;

    public bool TryStart(PaintSupply supply)
    {
        if (Active || supply.LoaderFull)
        {
            return false;
        }

        _pod = supply.NextPod();
        if (_pod < 0)
        {
            return false;
        }

        Active = true;
        Elapsed = 0f;
        return true;
    }

    /// <returns>Balls transferred if the refill finished this tick, otherwise 0.</returns>
    public int Advance(PaintSupply supply, float dt, out bool completed)
    {
        completed = false;
        if (!Active)
        {
            return 0;
        }

        Elapsed += dt;

        // Finish on the tick closest to the configured duration (robust to float accumulation).
        if (Elapsed < supply.Params.RefillTime - dt * 0.5f)
        {
            return 0;
        }

        Active = false;
        completed = true;
        return supply.Transfer(_pod, int.MaxValue);
    }

    /// <returns>Balls transferred by a partial refill (0 unless PartialOnInterrupt).</returns>
    public int Cancel(PaintSupply supply)
    {
        if (!Active)
        {
            return 0;
        }

        Active = false;
        if (!supply.Params.PartialOnInterrupt)
        {
            return 0;
        }

        float fraction = Math.Clamp(Elapsed / supply.Params.RefillTime, 0f, 1f);
        int space = supply.Params.Capacity - supply.Loader;
        return supply.Transfer(_pod, (int)MathF.Floor(space * fraction));
    }

    public void Reset()
    {
        Active = false;
        Elapsed = 0f;
        _pod = -1;
    }
}
