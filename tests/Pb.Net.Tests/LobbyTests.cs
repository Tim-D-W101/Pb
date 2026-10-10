using Pb.Net.Client;
using Pb.Net.Lobby;
using Pb.Net.Packing;
using Pb.Net.Protocol;
using Pb.Sim.Match;
using Pb.Sim.Tests;

namespace Pb.Net.Tests;

/// <summary>Phase 4 (M4.4): the lobby: sides, ready and the countdown, chat, removing someone, the vote and the session's score.</summary>
public class LobbyTests
{
    private const int Second = 120;

    private static (NetRig Rig, LobbyHost Lobby) Game(string mode = "teams", bool balance = true, bool vote = false)
    {
        var rig = new NetRig();
        var lobby = new LobbyHost(rig.Server, rig.Settings, TestData.Config.Rules,
            new LobbyChoices { LevelId = "oxbarrow_works", ModeId = mode, Size = 3, Balance = balance, Vote = vote }, () => rig.Now,
            new LobbyMember { Name = "Host" });
        return (rig, lobby);
    }

    private static void Run(NetRig rig, LobbyHost lobby, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            rig.Tick();
            lobby.Update();
        }
    }

    private static int IdOf(RigClient c) => c.Net.Welcome!.ClientId;

    [Fact]
    public void Newcomers_go_on_the_smaller_side_and_balance_refuses_a_lopsided_switch()
    {
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A");
        Run(rig, lobby, 10);
        RigClient b = rig.Join("B");
        Run(rig, lobby, 10);
        RigClient c = rig.Join("C");
        Run(rig, lobby, 10);
        Assert.Equal(new[] { 0, 1, 0, 1 }, lobby.State.Members.Select(m => m.Side));

        // Everyone sees the same lobby.
        LobbyState seen = c.Net.Lobby!;
        Assert.Equal(new[] { "Host", "A", "B", "C" }, seen.Members.Select(m => m.Name));
        Assert.Equal(new[] { 0, 1, 0, 1 }, seen.Members.Select(m => m.Side));
        Assert.True(seen.Members[0].Host);

        // Two a side: A crossing would make it three against one.
        a.Net.Ask(LobbyAsk.Side, 0);
        Run(rig, lobby, 10);
        Assert.Equal(1, lobby.State.Find(IdOf(a))!.Side);

        // With balance off it may, up to half the round's places a side.
        lobby.SetChoices(lobby.State.Choices with { Balance = false });
        a.Net.Ask(LobbyAsk.Side, 0);
        c.Net.Ask(LobbyAsk.Side, 0);
        Run(rig, lobby, 10);
        Assert.Equal(new[] { 0, 0, 0, 0 }, lobby.State.Members.Select(m => m.Side));
        Assert.Equal(new[] { 0, 0, 0, 0 }, b.Net.Lobby!.Members.Select(m => m.Side));
        RigClient d = rig.Join("D"), e = rig.Join("E");
        Run(rig, lobby, 10);
        Assert.Equal(1, lobby.State.Find(IdOf(d))!.Side);
        d.Net.Ask(LobbyAsk.Side, 0);
        Run(rig, lobby, 10);
        Assert.Equal(0, lobby.State.Find(IdOf(d))!.Side);
        e.Net.Ask(LobbyAsk.Side, 0);
        Run(rig, lobby, 10);
        Assert.Equal(5, lobby.State.Members.Count(m => m.Side == 0));
        Assert.Equal(1, lobby.State.Find(IdOf(e))!.Side);

        // Out of teams nobody has a side.
        lobby.SetChoices(lobby.State.Choices with { ModeId = "solo" });
        Assert.All(lobby.State.Members, m => Assert.Equal(-1, m.Side));
    }

    [Fact]
    public void Everyone_ready_starts_the_countdown_and_someone_unreadying_stops_it()
    {
        (NetRig rig, LobbyHost lobby) = Game(mode: "solo");
        RigClient a = rig.Join("A"), b = rig.Join("B");
        Run(rig, lobby, 10);
        int finished = 0;
        lobby.CountdownFinished += () => finished++;

        lobby.Ask(LobbyHost.HostId, LobbyAsk.Ready, 1);
        a.Net.Ask(LobbyAsk.Ready, 1);
        Run(rig, lobby, 10);
        Assert.Equal(LobbyPhase.Lobby, lobby.Phase);
        b.Net.Ask(LobbyAsk.Ready, 1);
        Run(rig, lobby, 10);
        Assert.Equal(LobbyPhase.Countdown, lobby.Phase);
        Assert.Equal(LobbyPhase.Countdown, a.Net.Lobby!.Phase);
        Assert.InRange(a.Net.Lobby.TimeLeft, 0.5f, rig.Settings.Countdown);

        a.Net.Ask(LobbyAsk.Ready, 0);
        Run(rig, lobby, 10);
        Assert.Equal(LobbyPhase.Lobby, lobby.Phase);

        a.Net.Ask(LobbyAsk.Ready, 1);
        Run(rig, lobby, (int)((rig.Settings.Countdown + 0.5f) * Second));
        Assert.Equal(1, finished);
        Assert.Equal(LobbyPhase.Loading, lobby.Phase);
        Assert.Equal(LobbyPhase.Loading, b.Net.Lobby!.Phase);

        // The host may start without waiting: then someone unreadying doesn't stop it.
        lobby.BackToLobby();
        lobby.Start();
        a.Net.Ask(LobbyAsk.Ready, 0);
        Run(rig, lobby, (int)((rig.Settings.Countdown + 0.5f) * Second));
        Assert.Equal(2, finished);
    }

    [Fact]
    public void Chat_goes_to_everyone_or_one_side_cut_to_length_and_limited()
    {
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A"), b = rig.Join("B");
        Run(rig, lobby, 10);
        // Host on 0, A on 1, B on 0.
        a.Net.Say("hello all", teamOnly: false);
        Run(rig, lobby, 10);
        Assert.Equal("hello all", Assert.Single(b.Net.TakeChat()).Text);
        Assert.Equal("A", Assert.Single(lobby.TakeChat()).Name);
        Assert.Single(a.Net.TakeChat());

        b.Net.Say("push left", teamOnly: true);
        Run(rig, lobby, 10);
        Assert.Empty(a.Net.TakeChat());
        ChatLine team = Assert.Single(lobby.TakeChat());
        Assert.True(team.TeamOnly);
        Assert.Equal(0, team.Side);

        b.Net.Say(new string('x', 300) + "\u0007", teamOnly: false);
        Run(rig, lobby, 10);
        Assert.Equal(rig.Settings.ChatMostChars, Assert.Single(a.Net.TakeChat()).Text.Length);

        // Rapid lines: only so many in so long.
        Run(rig, lobby, (int)(rig.Settings.ChatPer * Second) + 10);
        b.Net.TakeChat();
        for (int i = 0; i < 8; i++)
        {
            a.Net.Say($"spam {i}", teamOnly: false);
        }

        Run(rig, lobby, 10);
        Assert.Equal(rig.Settings.ChatLines, b.Net.TakeChat().Count);
        Run(rig, lobby, (int)(rig.Settings.ChatPer * Second) + 10);
        a.Net.Say("later", teamOnly: false);
        Run(rig, lobby, 10);
        Assert.Equal("later", Assert.Single(b.Net.TakeChat()).Text);
    }

    [Fact]
    public void The_host_removes_someone_and_they_are_told_why()
    {
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A");
        Run(rig, lobby, 10);
        lobby.Remove(IdOf(a));
        Run(rig, lobby, 10);
        Assert.Equal(ClientState.Gone, a.Net.State);
        Assert.Contains("removed", a.Net.GoneReason);
        Assert.Single(lobby.State.Members);
    }

    [Fact]
    public void The_vote_picks_the_place_with_the_most_votes()
    {
        (NetRig rig, LobbyHost lobby) = Game(vote: true);
        RigClient a = rig.Join("A"), b = rig.Join("B");
        Run(rig, lobby, 10);
        lobby.BeginLoading();
        lobby.RoundStarted();
        lobby.RoundOver(new MatchResult(RoundEnd.LastStanding, 0), Array.Empty<PersonInRound>(), Array.Empty<StatsEntry>());
        var options = new[]
        {
            new VoteOption("oxbarrow_works", "yard", "Oxbarrow Works: the yard"), new VoteOption("rail_yard", null, "The Rail Yard"),
            new VoteOption("cold_store", "store", "The Cold Store: the store"),
        };
        lobby.BackToLobby(options);
        Run(rig, lobby, 10);
        Assert.Equal(LobbyPhase.Vote, a.Net.Lobby!.Phase);
        Assert.Equal(3, a.Net.Lobby.VoteOptions.Count);
        lobby.Ask(LobbyHost.HostId, LobbyAsk.Vote, 0);
        a.Net.Ask(LobbyAsk.Vote, 1);
        b.Net.Ask(LobbyAsk.Vote, 1);
        Run(rig, lobby, (int)((rig.Settings.VoteTime + 0.5f) * Second));
        Assert.Equal(LobbyPhase.Lobby, lobby.Phase);
        Assert.Equal("rail_yard", lobby.State.Choices.LevelId);
        Assert.Null(lobby.State.Choices.PlaceId);
        Assert.Equal("rail_yard", b.Net.Lobby!.Choices.LevelId);
    }

    [Fact]
    public void The_session_score_counts_rounds_won_and_each_persons_eliminations()
    {
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A");
        Run(rig, lobby, 10);
        var people = new[] { new PersonInRound(LobbyHost.HostId, 0, 0), new PersonInRound(IdOf(a), 1, 1) };
        var stats = new[] { new StatsEntry(0, 10, 3, 2, 0, 50f, -1), new StatsEntry(1, 4, 1, 1, 0, 30f, 900) };
        lobby.RoundOver(new MatchResult(RoundEnd.LastStanding, 0), people, stats);
        lobby.BackToLobby();
        lobby.RoundOver(new MatchResult(RoundEnd.TimeUp, -1), people, stats);
        lobby.BackToLobby();
        lobby.RoundOver(new MatchResult(RoundEnd.LastStanding, 1), people, stats);
        Run(rig, lobby, 10);
        LobbyState seen = a.Net.Lobby!;
        Assert.Equal(3, seen.RoundsPlayed);
        Assert.Equal(new[] { 1, 1 }, seen.SideWins);
        Assert.Equal(1, seen.Members[0].RoundsWon);
        Assert.Equal(6, seen.Members[0].Eliminations);
        Assert.Equal(1, seen.Find(IdOf(a))!.RoundsWon);
        Assert.Equal(3, seen.Find(IdOf(a))!.Eliminations);
        Assert.Equal(LobbyPhase.Summary, seen.Phase);
    }

    [Fact]
    public void The_host_going_back_to_the_lobby_mid_round_drops_nobody_for_sending_nothing()
    {
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A");
        Run(rig, lobby, 10);
        rig.StartRound("teams", 900f, (0, 0, false, 0f, 0f), (1, 1, true, 5f, -20f));
        lobby.RoundStarted();
        rig.GoLive();
        Run(rig, lobby, Second);

        // The round hasn't ended when the host takes everyone back to the lobby, where A sends nothing for a while.
        lobby.BackToLobby();
        a.Silent = true;
        Run(rig, lobby, (int)(rig.Settings.DropAfter * 2f * Second));
        Assert.Single(rig.Server.Clients, c => c.Welcomed);
        Assert.Equal(2, lobby.State.Members.Count);
    }

    [Fact]
    public void Someone_who_leaves_and_comes_back_has_their_session_score_again()
    {
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A", key: "offline-a");
        RigClient b = rig.Join("B", key: "offline-b");
        Run(rig, lobby, 10);
        var people = new[] { new PersonInRound(IdOf(a), 1, 0), new PersonInRound(IdOf(b), 2, 1) };
        var stats = new[] { new StatsEntry(1, 10, 3, 2, 0, 50f, -1), new StatsEntry(2, 4, 1, 1, 0, 30f, 900) };
        lobby.RoundOver(new MatchResult(RoundEnd.LastStanding, 0), people, stats);
        lobby.BackToLobby();
        Run(rig, lobby, 10);
        a.Net.Leave();
        b.Net.Leave();
        Run(rig, lobby, 30);
        Assert.Single(lobby.State.Members);

        // A comes back as they were; someone new under B's name but another id starts from nothing.
        RigClient again = rig.Join("A", key: "offline-a");
        RigClient other = rig.Join("B", key: "offline-c");
        Run(rig, lobby, 10);
        LobbyMember back = lobby.State.Find(IdOf(again))!;
        Assert.Equal((2, 1), (back.Eliminations, back.RoundsWon));
        LobbyMember fresh = lobby.State.Find(IdOf(other))!;
        Assert.Equal((0, 0), (fresh.Eliminations, fresh.RoundsWon));
        Assert.Equal((2, 1), (again.Net.Lobby!.Find(IdOf(again))!.Eliminations, again.Net.Lobby.Find(IdOf(again))!.RoundsWon));
    }

    [Fact]
    public void A_second_copy_with_the_same_id_doesnt_take_the_others_score()
    {
        // Two copies on one computer share the id in its profile.
        (NetRig rig, LobbyHost lobby) = Game();
        RigClient a = rig.Join("A", key: "offline-pc");
        RigClient twin = rig.Join("Twin", key: "offline-pc");
        Run(rig, lobby, 10);
        lobby.RoundOver(new MatchResult(RoundEnd.LastStanding, 0), new[] { new PersonInRound(IdOf(a), 1, 0) },
            new[] { new StatsEntry(1, 10, 3, 2, 0, 50f, -1) });
        lobby.BackToLobby();
        a.Net.Leave();
        Run(rig, lobby, 30);
        RigClient again = rig.Join("A", key: "offline-pc");
        Run(rig, lobby, 10);
        Assert.Equal((0, 0), (lobby.State.Find(IdOf(again))!.Eliminations, lobby.State.Find(IdOf(again))!.RoundsWon));
        Assert.Equal(0, lobby.State.Find(IdOf(twin))!.Eliminations);
    }

    [Fact]
    public void The_lobby_comes_out_as_it_went_in()
    {
        var state = new LobbyState
        {
            ServerName = "Ada's game", Phase = LobbyPhase.Vote, TimeLeft = 7.5f, Forced = true, RoundsPlayed = 4, MaxPeople = 10,
            Choices = new LobbyChoices
            {
                LevelId = "rail_yard", PlaceId = "engine_shed", ModeId = "teams", Size = 4, ObjectiveId = "hold", TierId = "hard", Balance = false,
                Vote = true,
            },
        };
        state.SideWins[0] = 3;
        state.SideWins[1] = 1;
        state.Members.Add(new LobbyMember { Id = 0, Name = "Ada", Look = 2, Side = 1, Ready = true, Host = true, Ping_ms = 0, Eliminations = 9, RoundsWon = 3, Vote = 1 });
        state.Members.Add(new LobbyMember { Id = 7, Name = "Bo ☂", Look = 1, Side = 0, Ping_ms = 143, Vote = -1 });
        state.VoteOptions.Add(new VoteOption("cold_store", null, "The Cold Store"));
        state.VoteOptions.Add(new VoteOption("hospital_wing", "wings", "The Hospital Wing: the wings"));
        var w = new BitWriter(4096);
        state.Write(w);
        LobbyState back = LobbyState.Read(w.Finish())!;
        Assert.Equal(state.Choices, back.Choices);
        Assert.Equal((state.ServerName, state.Phase, state.TimeLeft, state.Forced, state.RoundsPlayed, state.MaxPeople),
            (back.ServerName, back.Phase, back.TimeLeft, back.Forced, back.RoundsPlayed, back.MaxPeople));
        Assert.Equal(state.SideWins, back.SideWins);
        Assert.Equal(state.VoteOptions, back.VoteOptions);
        Assert.Equal(state.Members.Select(Describe), back.Members.Select(Describe));
        static string Describe(LobbyMember m) => $"{m.Id} {m.Name} {m.Look} {m.Side} {m.Ready} {m.Host} {m.Ping_ms} {m.Eliminations} {m.RoundsWon} {m.Vote}";
        Assert.Null(LobbyState.Read(w.Finish()[..^3]));
    }
}
