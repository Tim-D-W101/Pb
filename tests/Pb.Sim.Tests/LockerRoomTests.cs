using System.Numerics;
using Pb.Sim.Level;

namespace Pb.Sim.Tests;

/// <summary>Phase 5 (M5.2): the gear locker's room (levels/locker_room.jsonc), which the game builds like any level.</summary>
public class LockerRoomTests
{
    [Fact]
    public void The_locker_room_has_room_round_its_turntable()
    {
        // The locker stands you on a turntable at the room's one spawn: nothing within 1.4 m of it from the knees to
        // over your head, so the camera can come round you close up from any side.
        LevelLayout room = TestData.Data.LockerRoom;
        var sim = new SimWorld(TestData.Config);
        sim.LoadLevel(room);
        SpawnPoint spawn = Assert.Single(room.PlayerSpawns);
        for (int k = 0; k < 16; k++)
        {
            float a = k * MathF.Tau / 16f;
            var away = new Vector3(MathF.Cos(a) * 1.4f, 0f, MathF.Sin(a) * 1.4f);
            foreach (float y in new[] { 0.5f, 1.2f, 2.0f })
            {
                Vector3 at = spawn.Position + new Vector3(0f, y, 0f);
                Assert.False(sim.Collision.SweepSphere(at, at + away, 0.1f, out _), $"something within 1.4 m {k * 22.5f}° round, {y} m up");
            }
        }

        // And a floor under it, and a ceiling over it for the lamp to hang from.
        Assert.True(sim.Collision.SweepSphere(spawn.Position + new Vector3(0f, 1f, 0f), spawn.Position - new Vector3(0f, 1f, 0f), 0f, out var floor));
        Assert.InRange(floor.Point.Y, spawn.Position.Y - 0.05f, spawn.Position.Y + 0.05f);
        Assert.True(sim.Collision.SweepSphere(spawn.Position + new Vector3(0f, 1f, 0f), spawn.Position + new Vector3(0f, 6f, 0f), 0f, out var ceiling));
        Assert.InRange(ceiling.Point.Y - floor.Point.Y, 2.7f, 4f);
    }
}
