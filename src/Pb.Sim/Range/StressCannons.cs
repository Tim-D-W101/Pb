using System.Numerics;
using Pb.Sim.Ballistics;
using Pb.Sim.Core;
using Pb.Sim.Events;

namespace Pb.Sim.Range;

/// <summary>
/// Phase 1 stress mode: invisible cannons that keep a target number of balls in the air so the
/// "1,000 live balls at 60 fps" check is easy to run. Cannon owners use negative ids.
/// </summary>
public sealed class StressCannons
{
    private uint _sequence;
    private int _next;

    public StressCannons(StressSettings settings)
    {
        Settings = settings;
        TargetLiveBalls = settings.TargetLiveBalls;
    }

    public StressSettings Settings { get; set; }

    public bool Enabled { get; set; }

    public int TargetLiveBalls { get; set; }

    public static int OwnerId(int cannonIndex) => -1 - cannonIndex;

    public void Update(BallisticsWorld balls, ulong matchSeed, float muzzleSpeed, float velocityVariance, int tick, float dt, SimEventQueue events)
    {
        IReadOnlyList<CannonSpec> cannons = Settings.Cannons;
        if (!Enabled || cannons.Count == 0)
        {
            return;
        }

        int wanted = Math.Min(Settings.MaxSpawnPerTick, TargetLiveBalls - balls.Pool.Count);
        for (int i = 0; i < wanted; i++)
        {
            int index = _next++ % cannons.Count;
            CannonSpec cannon = cannons[index];
            int owner = OwnerId(index);
            uint sequence = ++_sequence;
            var rng = new Pcg32(SeedHash.Shot(matchSeed, owner, sequence));

            float yaw = cannon.Yaw + rng.Symmetric(cannon.YawSpread);
            float pitch = rng.Range(cannon.PitchMin, cannon.PitchMax);
            float speed = muzzleSpeed + rng.Symmetric(velocityVariance);
            Vector3 velocity = ViewAngles.Forward(yaw, pitch) * speed;
            float firstStep = MathF.Max(1e-4f, dt * rng.NextFloat());

            if (!balls.Spawn(cannon.Position, velocity, owner, sequence, (byte)(index % 2), rng, firstStep, tick, events))
            {
                return;
            }
        }
    }
}
