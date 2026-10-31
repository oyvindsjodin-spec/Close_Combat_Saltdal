using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Saltdal.Sim;

/// <summary>One order the player gave, and the moment (in ticks) it was given.</summary>
public sealed record Command(long Tick, string Kind, int Team = -1, float X = 0, float Y = 0, int Value = 0);

/// <summary>
/// A battle in progress, kept so it can be saved and picked up again. The
/// simulation is deterministic: the same battle, seed and forces, given the
/// same orders at the same moments, plays out exactly the same. So instead of
/// writing down every soldier, a save holds how the battle began and every
/// order the player gave, and loading plays it all through again quickly.
/// All player orders go through here so none are missed.
/// </summary>
public sealed class SavedBattle
{
    public string BattleId { get; }
    public ulong Seed { get; }
    public Side PlayerSide { get; }
    public List<string>? Picks { get; }
    /// <summary>The operation as it stood when the battle began, or null for a single battle.</summary>
    public string? Operation { get; }
    public IReadOnlyList<Command> Commands => _commands;
    public Simulation Sim { get; private set; } = null!;

    readonly List<Command> _commands = new();

    public SavedBattle(string battleId, ulong seed, Side playerSide, List<string>? picks, string? operation)
    {
        BattleId = battleId;
        Seed = seed;
        PlayerSide = playerSide;
        Picks = picks?.ToList();
        Operation = operation;
    }

    /// <summary>Set up the battle from the start.</summary>
    public Simulation Begin(Scenario scenario, MapData map)
    {
        Sim = scenario.Build(map, Seed, PlayerSide, Picks);
        return Sim;
    }

    // ---------------------------------------------------------------- the player's orders

    public void Move(Team team, Vector2 to, MoveMode mode) => Give(new Command(Sim.TickCount, "move", team.Id, to.X, to.Y, (int)mode));
    public void Fire(Team team, Vector2 at) => Give(new Command(Sim.TickCount, "fire", team.Id, at.X, at.Y));
    public void Defend(Team team, Vector2 facing) => Give(new Command(Sim.TickCount, "defend", team.Id, facing.X, facing.Y));
    public void Hide(Team team) => Give(new Command(Sim.TickCount, "hide", team.Id));
    public void Stop(Team team) => Give(new Command(Sim.TickCount, "stop", team.Id));
    public void DigIn(Team team, Vector2 facing) => Give(new Command(Sim.TickCount, "dig", team.Id, facing.X, facing.Y));
    public void Support(Team team, int asset, Vector2 at) => Give(new Command(Sim.TickCount, "support", team.Id, at.X, at.Y, asset));
    public void EndPrep() => Give(new Command(Sim.TickCount, "endprep"));
    public void AskCeasefire() => Give(new Command(Sim.TickCount, "ceasefire"));

    void Give(Command c)
    {
        _commands.Add(c);
        Apply(c);
    }

    void Apply(Command c)
    {
        var team = c.Team >= 0 ? Sim.Teams.FirstOrDefault(t => t.Id == c.Team) : null;
        var at = new Vector2(c.X, c.Y);
        switch (c.Kind)
        {
            case "move" when team != null: Sim.OrderMove(team, at, (MoveMode)c.Value); break;
            case "fire" when team != null: Sim.OrderFire(team, at); break;
            case "defend" when team != null: Sim.OrderDefend(team, at); break;
            case "hide" when team != null: Sim.OrderHide(team); break;
            case "stop" when team != null: Sim.Stop(team); break;
            case "dig" when team != null: Sim.OrderDigIn(team, at); break;
            case "support" when team != null && c.Value >= 0 && c.Value < Sim.Support.Count:
                if (Sim.OrderSupport(team, Sim.Support[c.Value], at) is { } problem) Sim.Log(PlayerSide, problem);
                break;
            case "endprep": Sim.EndPrep(); break;
            case "ceasefire":
                if (Sim.RequestCeasefire(PlayerSide) is { } refusal) Sim.Log(PlayerSide, refusal);
                break;
        }
    }

    // ---------------------------------------------------------------- saving and loading

    sealed record SaveFile(int Version, string Battle, ulong Seed, string Side, List<string>? Picks,
        string? Operation, long Tick, List<Command> Commands);

    const int Version = 1;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string Save() => JsonSerializer.Serialize(
        new SaveFile(Version, BattleId, Seed, PlayerSide.ToString(), Picks, Operation, Sim.TickCount, _commands), Json);

    /// <summary>Read a save. Call <see cref="Replay"/> with the battle's scenario to bring it back.</summary>
    public static SavedBattle Load(string json, out long tick)
    {
        var f = JsonSerializer.Deserialize<SaveFile>(json, Json) ?? throw new GameDataException("The saved battle is empty.");
        if (f.Version != Version) throw new GameDataException("The saved battle is from another version of the game.");
        var saved = new SavedBattle(f.Battle, f.Seed, Enum.Parse<Side>(f.Side), f.Picks, f.Operation);
        saved._commands.AddRange(f.Commands);
        tick = f.Tick;
        return saved;
    }

    /// <summary>Play the battle from the start up to the given tick, giving each order when it was given.</summary>
    public Simulation Replay(Scenario scenario, MapData map, long untilTick)
    {
        Begin(scenario, map);
        int next = 0;
        while (Sim.TickCount < untilTick && !Sim.IsOver)
        {
            while (next < _commands.Count && _commands[next].Tick <= Sim.TickCount) Apply(_commands[next++]);
            Sim.Tick();
        }
        while (next < _commands.Count && _commands[next].Tick <= Sim.TickCount) Apply(_commands[next++]);
        return Sim;
    }
}
