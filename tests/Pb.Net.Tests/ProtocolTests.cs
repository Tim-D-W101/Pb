using System.Numerics;
using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Sim;
using Pb.Sim.Collision;
using Pb.Sim.Core;
using Pb.Sim.Data;
using Pb.Sim.Events;
using Pb.Sim.Gear;
using Pb.Sim.Match;
using Pb.Sim.Players;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>Phase 4 (M4.2): every message survives packing and unpacking, within what its quantising allows.</summary>
public class ProtocolTests
{
    private static readonly PositionQuant Yard = new(TestData.Data.Levels["rail_yard"].Bounds);

    [Fact]
    public void Bits_come_back_as_they_were_written()
    {
        var w = new BitWriter(4);
        var rng = new Pcg32(5);
        var written = new List<(uint Value, int Bits)>();
        for (int i = 0; i < 500; i++)
        {
            int bits = 1 + (int)(rng.NextUInt() % 32);
            uint value = bits == 32 ? rng.NextUInt() : rng.NextUInt() & ((1u << bits) - 1u);
            w.WriteBits(value, bits);
            written.Add((value, bits));
        }

        w.WriteVarUInt(0);
        w.WriteVarUInt(300);
        w.WriteVarUInt(uint.MaxValue);
        w.WriteVarInt(-1);
        w.WriteVarInt(int.MinValue);
        w.WriteSigned(-100, 8);
        w.WriteSigned(-1000, 8);
        w.WriteFloat(-0.15625f);
        w.WriteString("Ünïcode name");
        ReadOnlySpan<byte> packet = w.Finish();

        var r = new BitReader(packet);
        foreach ((uint value, int bits) in written)
        {
            Assert.Equal(value, r.ReadBits(bits));
        }

        Assert.Equal(0u, r.ReadVarUInt());
        Assert.Equal(300u, r.ReadVarUInt());
        Assert.Equal(uint.MaxValue, r.ReadVarUInt());
        Assert.Equal(-1, r.ReadVarInt());
        Assert.Equal(int.MinValue, r.ReadVarInt());
        Assert.Equal(-100, r.ReadSigned(8));
        Assert.Equal(-128, r.ReadSigned(8));
        Assert.Equal(-0.15625f, r.ReadFloat());
        Assert.Equal("Ünïcode name", r.ReadString());
        Assert.False(r.Overflowed);
    }

    [Fact]
    public void Reading_past_the_end_gives_zeros_and_says_so()
    {
        var r = new BitReader(new byte[] { 0xFF });
        Assert.Equal(0xFFu, r.ReadBits(8));
        Assert.Equal(0u, r.ReadBits(5));
        Assert.True(r.Overflowed);
        Assert.Equal("", new BitReader(new byte[] { 200 }).ReadString());
        Assert.Null(HelloMessage.Read(new byte[] { (byte)MessageType.Hello, 1 }));
        Assert.Null(RoundSetupMessage.Read(new byte[] { (byte)MessageType.RoundSetup }));
    }

    [Fact]
    public void Positions_angles_and_ranges_come_back_within_their_steps()
    {
        var rng = new Pcg32(9);
        Aabb b = TestData.Data.Levels["rail_yard"].Bounds;
        for (int i = 0; i < 2000; i++)
        {
            var p = new Vector3(rng.Range(b.Min.X, b.Max.X), rng.Range(-2f, 30f), rng.Range(b.Min.Z, b.Max.Z));
            Assert.True(Vector3.Distance(p, Yard.Snap(p)) <= PositionQuant.MaxStep, $"{p} came back as {Yard.Snap(p)}");
            float angle = rng.Range(-20f, 20f);
            float back = Quant.FromAngle(Quant.Angle(angle, 16), 16);
            float error = MathF.Abs(MathF.IEEERemainder(back - angle, 2f * MathF.PI));
            Assert.True(error <= 0.01f * MathF.PI / 180f, $"{angle} rad came back {error} rad off");
        }

        Assert.Equal(0f, Quant.FromRange(Quant.Range(-5f, 0f, 1f, 8), 0f, 1f, 8));
        Assert.Equal(1f, Quant.FromRange(Quant.Range(9f, 0f, 1f, 8), 0f, 1f, 8));
        Assert.Equal(0u, Quant.Range(float.NaN, 0f, 1f, 8));
    }

    [Fact]
    public void A_command_arrives_exactly_as_it_was_predicted_with()
    {
        var rng = new Pcg32(3);
        var w = new BitWriter();
        for (int i = 0; i < 500; i++)
        {
            var c = new InputCommand
            {
                Move = new Vector2(rng.Range(-1.2f, 1.2f), rng.Range(-1.2f, 1.2f)), Yaw = rng.Range(-10f, 10f), Pitch = rng.Range(-2f, 2f),
                HeadYaw = i % 3 == 0 ? 0f : rng.Range(-1f, 1f), Buttons = (InputButtons)(rng.NextUInt() & 0xFFFF),
            };
            w.Reset();
            CommandCodec.Write(w, c, (uint)i);
            var r = new BitReader(w.Finish());
            InputCommand read = CommandCodec.Read(ref r, 42, out uint behind);
            InputCommand predicted = CommandCodec.Quantize(c);
            Assert.Equal(predicted.Move, read.Move);
            Assert.Equal(predicted.Yaw, read.Yaw);
            Assert.Equal(predicted.Pitch, read.Pitch);
            Assert.Equal(predicted.HeadYaw, read.HeadYaw);
            Assert.Equal(predicted.Buttons, read.Buttons);
            Assert.Equal(42, read.Tick);
            Assert.Equal(Math.Min((uint)i, 255u), behind);
            Assert.Equal(c.Buttons & CommandCodec.KnownButtons, read.Buttons);
            Assert.True(read.Move.X is >= -1f and <= 1f && read.Move.Y is >= -1f and <= 1f);
        }

        // Standing still is exactly still, a person's head looks where they aim, and junk becomes harmless.
        InputCommand idle = CommandCodec.Quantize(new InputCommand { Move = Vector2.Zero, Yaw = 1f });
        Assert.Equal(Vector2.Zero, idle.Move);
        Assert.Equal(0f, idle.HeadYaw);
        InputCommand junk = CommandCodec.Quantize(new InputCommand { Move = new Vector2(float.NaN, float.PositiveInfinity), Yaw = float.NaN, Pitch = 99f });
        Assert.True(float.IsFinite(junk.Yaw) && junk.Pitch <= MathF.PI / 2f && float.IsFinite(junk.Move.X) && junk.Move.Y <= 1f);
    }

    /// <summary>A player part-way through everything: moving, leaning, swapping, on a ladder, refilling, ramping.</summary>
    private static PlayerState Busy(SimWorld sim)
    {
        PlayerState p = sim.AddPlayer(3, 1, new Vector3(1.5f, 2.25f, -7f), 0.4f);
        p.Velocity = new Vector3(3f, -1f, 0.5f);
        p.Lean = -0.4f;
        p.LeanRoll = -0.12f;
        p.LeanOffset = new Vector3(-0.1f, -0.01f, 0.02f);
        p.Shoulder = -0.3f;
        p.ShoulderTarget = -1f;
        p.Stance = Stance.Crouching;
        p.EyeHeight = 1.1f;
        p.SlideCooldown = 0.25f;
        p.SprintRecovery = 0.1f;
        p.Tuck = 0.2f;
        p.PreviousButtons = InputButtons.Fire | InputButtons.LeanLeft;
        p.Ladder = 2;
        p.LadderPhase = LadderPhase.SteppingOff;
        p.LadderTime = 0.33f;
        p.Marker.Paint.Transfer(0, 0);
        p.Marker.Fire.Mode = FireMode.Ramping;
        return p;
    }

    [Fact]
    public void Your_own_state_comes_back_to_the_bit()
    {
        var sim = new SimWorld(TestData.Config);
        PlayerState p = Busy(sim);
        // Fire a few shots and start a refill, so the marker has timers and counts to carry.
        var shots = new ShotRequest[8];
        var events = new SimEventQueue(64);
        for (int t = 0; t < 30; t++)
        {
            p.Marker.Update(t * NetRig.Dt, NetRig.Dt, new MarkerInput(t % 4 < 2, t == 20, false, false, true), shots, events, p.Id, p.Team, t);
        }

        PredictedState state = PredictedState.Capture(p, 30 * NetRig.Dt);
        var fields = new uint[OwnFields.Count];
        OwnFields.Capture(state, fields);
        var w = new BitWriter();
        Delta.Write(w, fields, new uint[OwnFields.Count], OwnFields.Widths);
        var read = new uint[OwnFields.Count];
        var r = new BitReader(w.Finish());
        Delta.Read(ref r, read, OwnFields.Widths);
        PredictedState back = OwnFields.Read(read);
        Assert.True(back.Matches(state, 0f));
        Assert.Equal(state.Position, back.Position);
        Assert.Equal(state.Marker.ShotSequence, back.Marker.ShotSequence);
        Assert.True(state.Marker.ShotSequence > 0);

        // Put back on another copy whose clock reads differently, it captures the same again.
        var other = new SimWorld(TestData.Config);
        PlayerState q = other.AddPlayer(3, 1, Vector3.Zero, 0f);
        back.Restore(q, 1234.5);
        Assert.True(PredictedState.Capture(q, 1234.5).Matches(state, 0f));
    }

    [Fact]
    public void Others_come_back_within_their_steps()
    {
        var sim = new SimWorld(TestData.Config);
        PlayerState p = Busy(sim);
        p.HeadYaw = 0.5f;
        p.Pitch = -0.3f;
        var fields = new uint[PuppetFields.Count];
        PuppetFields.Capture(p, Yard, fields);
        var copy = new SimWorld(TestData.Config);
        PlayerState q = copy.AddPlayer(3, 1, Vector3.Zero, 0f);
        PuppetFields.Pose(q, fields, fields, 0f, Yard, TestData.Config.Movement);
        Assert.True(Vector3.Distance(p.Position, q.Position) <= PositionQuant.MaxStep);
        Assert.True(Vector3.Distance(p.Velocity, q.Velocity) < 0.01f);
        Assert.True(MathF.Abs(p.Yaw - q.Yaw) < 0.0002f && MathF.Abs(p.Pitch - q.Pitch) < 0.0002f && MathF.Abs(p.HeadYaw - q.HeadYaw) < 0.001f);
        Assert.Equal(p.Stance, q.Stance);
        Assert.Equal(p.Ladder, q.Ladder);
        Assert.Equal(p.LadderPhase, q.LadderPhase);
        Assert.True(MathF.Abs(p.Lean - q.Lean) < 0.01f && MathF.Abs(p.Shoulder - q.Shoulder) < 0.01f && MathF.Abs(p.EyeHeight - q.EyeHeight) < 0.01f);
        Assert.Equal(p.Alive, q.Alive);

        // Half-way between two snapshots is half-way between where they were.
        var later = new uint[PuppetFields.Count];
        p.Position += new Vector3(0.1f, 0f, 0f);
        PuppetFields.Capture(p, Yard, later);
        PuppetFields.Pose(q, fields, later, 0.5f, Yard, TestData.Config.Movement);
        Assert.InRange(q.Position.X, 1.55f - 0.003f, 1.55f + 0.003f);
    }

    [Fact]
    public void A_difference_applied_to_its_baseline_gives_the_whole()
    {
        var rng = new Pcg32(11);
        var widths = new byte[40];
        for (int i = 0; i < widths.Length; i++)
        {
            widths[i] = (byte)(1 + rng.NextUInt() % 32);
        }

        var w = new BitWriter();
        for (int trial = 0; trial < 200; trial++)
        {
            var baseline = new uint[40];
            var now = new uint[40];
            for (int i = 0; i < 40; i++)
            {
                uint mask = widths[i] == 32 ? uint.MaxValue : (1u << widths[i]) - 1u;
                baseline[i] = rng.NextUInt() & mask;
                now[i] = rng.NextFloat() < 0.8f ? baseline[i] : rng.NextUInt() & mask;
            }

            w.Reset();
            Delta.Write(w, now, baseline, widths);
            var read = (uint[])baseline.Clone();
            var r = new BitReader(w.Finish());
            Delta.Read(ref r, read, widths);
            Assert.Equal(now, read);
        }

        // Nothing changed costs a bit.
        w.Reset();
        Delta.Write(w, new uint[40], new uint[40], widths);
        Assert.Equal(1, w.BitCount);
    }

    public static IEnumerable<object[]> Events() =>
    [
        [new SimEvent { Type = SimEventType.ShotFired, Tick = 95, PlayerId = 4, ShotSequence = 300, Team = 3, Position = new Vector3(10f, 1.5f, -3f),
            Velocity = new Vector3(80f, 2f, -30f), Value = 0.0031f, Extra = 3, TargetId = -1, ColliderId = -1 }],
        [new SimEvent { Type = SimEventType.BallBroke, Tick = 99, PlayerId = 1, ShotSequence = 7, Team = 1, Surface = new SurfaceId(5),
            Position = new Vector3(-20f, 0.3f, 15f), Normal = Vector3.Normalize(new Vector3(0.3f, 0.9f, 0.1f)), Velocity = new Vector3(-50f, -3f, 1f),
            Value = 51.2f, TargetId = 1002, ColliderId = -1, Lethal = true, Extra = (int)HitboxPart.Mask }],
        [new SimEvent { Type = SimEventType.BallBounced, Tick = 100, PlayerId = 0, ShotSequence = 2, Team = 0, Surface = new SurfaceId(2),
            Position = new Vector3(3f, 0f, 3f), Normal = Vector3.UnitY, Velocity = new Vector3(10f, 4f, 0f), Value = 12f, TargetId = -1, ColliderId = 17 }],
        [new SimEvent { Type = SimEventType.PlayerEliminated, Tick = 100, PlayerId = 2, TargetId = 5, Team = 1, ShotSequence = 44,
            Position = new Vector3(1f, 1.6f, 1f), Normal = -Vector3.UnitZ, Extra = (int)HitboxPart.Torso, ColliderId = -1 }],
        [new SimEvent { Type = SimEventType.DoorMoved, Tick = 90, PlayerId = 3, TargetId = 12, ColliderId = 230, Position = new Vector3(5f, 1f, 5f),
            Surface = new SurfaceId(7), Value = 18f, Extra = 1 }],
        [new SimEvent { Type = SimEventType.RoundEnded, Tick = 100, TargetId = 1, Extra = (int)RoundEnd.LastStanding, Value = 1f, PlayerId = -1, ColliderId = -1 }],
        [new SimEvent { Type = SimEventType.HoldChanged, Tick = 98, Team = 1, Position = new Vector3(0f, 4f, 2f), Value = 33.5f, Extra = 2, PlayerId = -1,
            TargetId = -1, ColliderId = -1 }],
        [new SimEvent { Type = NetEventTypes.Callout, Tick = 97, PlayerId = 6, TargetId = 2, Extra = 4, Position = new Vector3(9f, 0f, 9f), ColliderId = -1 }],
    ];

    [Theory]
    [MemberData(nameof(Events))]
    public void Every_event_the_network_carries_comes_back(SimEvent e)
    {
        Assert.True(EventCodec.Carried(e.Type));
        var w = new BitWriter();
        EventCodec.Write(w, e, 100, Yard);
        var r = new BitReader(w.Finish());
        Assert.True(EventCodec.Read(ref r, 100, Yard, out SimEvent back));
        Assert.Equal(e.Type, back.Type);
        Assert.Equal(e.Tick, back.Tick);
        Assert.Equal(e.PlayerId, back.PlayerId);
        Assert.Equal(e.TargetId, back.TargetId);
        Assert.Equal(e.ColliderId, back.ColliderId);
        Assert.Equal(e.ShotSequence, back.ShotSequence);
        Assert.Equal(e.Team, back.Team);
        Assert.Equal(e.Surface, back.Surface);
        Assert.Equal(e.Extra, back.Extra);
        Assert.Equal(e.Value, back.Value);
        Assert.Equal(e.Lethal, back.Lethal);
        Assert.True(Vector3.Distance(e.Position, back.Position) <= PositionQuant.MaxStep);
        Assert.True(Vector3.Distance(e.Velocity, back.Velocity) < 0.01f);
        if (e.Normal != Vector3.Zero)
        {
            Assert.True(Vector3.Dot(e.Normal, back.Normal) > 0.999f);
        }
    }

    [Fact]
    public void Footsteps_and_the_like_are_left_to_each_copy()
    {
        Assert.False(EventCodec.Carried(SimEventType.Footstep));
        Assert.False(EventCodec.Carried(SimEventType.DryFire));
        Assert.False(EventCodec.Carried(SimEventType.RefillStarted));
        // A joining copy raises this for itself (its own splat the server says didn't happen); it's never sent.
        Assert.False(EventCodec.Carried(SimEventType.SplatWithdrawn));
    }

    [Fact]
    public void The_reliable_messages_come_back()
    {
        var w = new BitWriter();
        new HelloMessage { Build = "b", DataHash = "h", Name = "Ada", Password = "pw", Look = 2, Key = "offline-0123abcd" }.Write(w);
        HelloMessage hello = HelloMessage.Read(w.Finish())!;
        Assert.Equal(("b", "h", "Ada", "pw", (byte)2, NetProtocol.Version), (hello.Build, hello.DataHash, hello.Name, hello.Password, hello.Look, hello.Protocol));
        Assert.Equal("offline-0123abcd", hello.Key);

        // A hello from version 2 (no id after the character) is still read, so its copy can be told to update.
        w.Reset();
        w.WriteByte((byte)MessageType.Hello);
        w.WriteVarUInt(2);
        w.WriteString("b", 64);
        w.WriteString("h", 80);
        w.WriteString("Old", 48);
        w.WriteString("", 64);
        w.WriteByte(1);
        HelloMessage old = HelloMessage.Read(w.Finish())!;
        Assert.Equal((2, "Old", ""), (old.Protocol, old.Name, old.Key));

        w.Reset();
        var setup = new RoundSetupMessage
        {
            Round = 3, LevelId = "rail_yard", PlaceId = "tracks", ModeId = "teams", Size = 5, Objective = ObjectiveKind.Hold, TierId = "hard",
            Seed = 0xFEEDFACE12345678UL, TimeLimit = 600f, StartPods = 2, BotPods = 3, Pickups = true, Attackers = 1, EndWhenPeopleOut = true,
            YourPlayerId = 4,
        };
        setup.Roster.Add(new RosterEntry { PlayerId = 0, Team = 0, Name = "Ada", Person = true, Look = 1, Position = new Vector3(1f, 2f, 3f), Yaw = 0.5f });
        setup.Roster.Add(new RosterEntry { PlayerId = 4, Team = 1, Name = "Kestrel", Person = false, Position = new Vector3(-1f, 0f, 9f), Yaw = -2f });
        setup.Write(w);
        RoundSetupMessage s = RoundSetupMessage.Read(w.Finish())!;
        Assert.Equal((3, "rail_yard", "tracks", "teams", 5, ObjectiveKind.Hold, "hard"), (s.Round, s.LevelId, s.PlaceId, s.ModeId, s.Size, s.Objective, s.TierId));
        Assert.Equal((0xFEEDFACE12345678UL, 600f, 2, 3, true, (byte)1, true, 4),
            (s.Seed, s.TimeLimit, s.StartPods, s.BotPods, s.Pickups, s.Attackers, s.EndWhenPeopleOut, s.YourPlayerId));
        Assert.Equal(setup.Roster, s.Roster);

        w.Reset();
        var over = new RoundOverMessage { Round = 3, Result = new MatchResult(RoundEnd.Held, 1), Elapsed = 123.5f, EndTick = 9000 };
        over.Stats.Add(new StatsEntry(0, 40, 3, 2, 1, 100.5f, -1));
        over.Stats.Add(new StatsEntry(4, 10, 0, 0, 0, 30f, 4000));
        over.Write(w);
        RoundOverMessage o = RoundOverMessage.Read(w.Finish())!;
        Assert.Equal((over.Round, over.Result, over.Elapsed, over.EndTick), (o.Round, o.Result, o.Elapsed, o.EndTick));
        Assert.Equal(over.Stats, o.Stats);
    }

    [Fact]
    public void A_kit_goes_in_the_hello_and_the_roster_and_comes_back()
    {
        // Phase 5 (M5.3): what you picked in the gear locker, item by item in its three colours.
        GearCatalog gear = TestData.Data.Gear;
        Loadout kit = gear.Deal(42, 3, character: 2);
        kit[GearSlot.Jersey] = kit[GearSlot.Jersey] with { Colours = new GearColours(0x123456, 0xABCDEF, 0x0F0F0F) };
        var w = new BitWriter();
        new HelloMessage { Build = "b", DataHash = "h", Name = "Ada", Look = 2, Kit = kit }.Write(w);
        Assert.True(HelloMessage.Read(w.Finish())!.Kit!.SameAs(kit));
        w.Reset();
        new HelloMessage { Build = "b", DataHash = "h", Name = "Bo" }.Write(w);
        Assert.Null(HelloMessage.Read(w.Finish())!.Kit);

        // A hello from version 3 (no kit after the id) is still read, so its copy can be told to update.
        w.Reset();
        w.WriteByte((byte)MessageType.Hello);
        w.WriteVarUInt(3);
        w.WriteString("b", 64);
        w.WriteString("h", 80);
        w.WriteString("Old", 48);
        w.WriteString("", 64);
        w.WriteByte(1);
        w.WriteString("offline-1", 64);
        HelloMessage old = HelloMessage.Read(w.Finish())!;
        Assert.Equal((3, "Old", "offline-1"), (old.Protocol, old.Name, old.Key));
        Assert.Null(old.Kit);

        // In the round's roster, a person's kit; a bot's is dealt on every copy.
        w.Reset();
        var setup = new RoundSetupMessage { Round = 1, LevelId = "oxbarrow_works", ModeId = "teams", Size = 2, TierId = "normal", Seed = 7 };
        setup.Roster.Add(new RosterEntry { PlayerId = 0, Team = 0, Name = "Ada", Person = true, Look = 2, Kit = kit, Position = Vector3.One, Yaw = 1f });
        setup.Roster.Add(new RosterEntry { PlayerId = 1, Team = 1, Name = "Kestrel", Person = false, Position = Vector3.Zero, Yaw = 0f });
        setup.Write(w);
        RoundSetupMessage s = RoundSetupMessage.Read(w.Finish())!;
        Assert.True(s.Roster[0].Kit!.SameAs(kit));
        Assert.Null(s.Roster[1].Kit);
        Assert.Equal((Vector3.One, 1f, "Kestrel"), (s.Roster[0].Position, s.Roster[0].Yaw, s.Roster[1].Name));

        // A change in the locker on its own; cut short, it isn't read.
        w.Reset();
        KitMessage.Write(w, kit);
        Assert.True(KitMessage.Read(w.Finish())!.SameAs(kit));
        Assert.Null(KitMessage.Read(w.Finish()[..^2]));
    }

    [Fact]
    public void A_search_on_the_network_is_answered_with_the_game()
    {
        var w = new BitWriter(256);
        Pb.Net.Discovery.DiscoveryMessage.WriteSearch(w);
        byte[] search = w.Finish().ToArray();
        Assert.True(Pb.Net.Discovery.DiscoveryMessage.IsSearch(search));
        Assert.False(Pb.Net.Discovery.DiscoveryMessage.IsSearch(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Null(Pb.Net.Discovery.DiscoveryMessage.ReadAnswer(search));

        var game = new Pb.Net.Discovery.GameAnnouncement
        {
            Name = "Ada's game", Port = 47820, Where = "Oxbarrow Works: the warehouse", How = "Teams · 3 v 3 · Normal", People = 4, MaxPeople = 10,
            Build = "515146a", Password = true, InRound = true,
        };
        w.Reset();
        Pb.Net.Discovery.DiscoveryMessage.WriteAnswer(w, game);
        byte[] answer = w.Finish().ToArray();
        Assert.Equal(game, Pb.Net.Discovery.DiscoveryMessage.ReadAnswer(answer));
        Assert.False(Pb.Net.Discovery.DiscoveryMessage.IsSearch(answer));
        Assert.Null(Pb.Net.Discovery.DiscoveryMessage.ReadAnswer(answer.AsSpan(0, answer.Length - 3)));
    }
}
