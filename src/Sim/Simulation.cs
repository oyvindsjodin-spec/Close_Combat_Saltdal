using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>One round fired, kept briefly so it can be drawn as a tracer.</summary>
public readonly record struct Shot(Vector2 From, Vector2 To, bool Hit, float Time, Side Side, int ShooterId);

/// <summary>Something that gives orders on its own each few seconds, such as the AI.</summary>
public interface ICommander
{
    void Update(Simulation sim);
}

/// <summary>
/// The whole battle state, advanced in fixed ticks. Nothing here knows about
/// Godot or drawing, so the same seed and orders always give the same result.
/// The work of a tick is split across the Simulation.*.cs files.
/// </summary>
public sealed partial class Simulation
{
    public const float TickSeconds = 0.1f;
    /// <summary>How long a spotted soldier stays shown after he drops out of sight.</summary>
    public const float SightMemorySeconds = 1.0f;

    public MapData Map { get; }
    public Pathfinder Pathfinder { get; }
    public Rng Rng { get; }
    public float Time { get; private set; }
    public long TickCount { get; private set; }

    readonly List<Team> _teams = new();
    public IReadOnlyList<Team> Teams => _teams;
    public IEnumerable<Soldier> Soldiers => _teams.SelectMany(t => t.Soldiers);

    public List<VictoryFlag> Flags { get; } = new();
    public List<ICommander> Commanders { get; } = new();

    readonly List<Shot> _shots = new();
    public IReadOnlyList<Shot> RecentShots => _shots;

    /// <summary>Game seconds until the battle ends on its own.</summary>
    public float TimeLimit { get; set; } = 30 * 60;
    public BattleResult? Result { get; private set; }
    public bool IsOver => Result != null;

    int _nextSoldierId = 1;

    public Simulation(MapData map, ulong seed = 1)
    {
        Map = map;
        Pathfinder = new Pathfinder(map);
        Rng = new Rng(seed);
    }

    public Team AddTeam(string name, Side side)
    {
        var team = new Team(_teams.Count + 1, name, side);
        _teams.Add(team);
        return team;
    }

    public Soldier AddSoldier(Team team, string name, Vector2 at, Weapon weapon, bool leader = false, float experience = 0.5f)
    {
        var s = new Soldier(_nextSoldierId++, name, at, weapon, leader, experience);
        team.Add(s);
        return s;
    }

    /// <summary>Create a team of riflemen spread around a point. Handy for tests.</summary>
    public Team SpawnTeam(string name, Side side, Vector2 at, string[] soldierNames, Weapon? weapon = null)
    {
        var team = AddTeam(name, side);
        weapon ??= side == Side.Allied ? Weapon.LeeEnfield : Weapon.Kar98k;
        for (int i = 0; i < soldierNames.Length; i++)
            AddSoldier(team, soldierNames[i], at + FormationOffset(i, soldierNames.Length), weapon, i == 0);
        return team;
    }

    public void Tick()
    {
        if (IsOver) return;
        if (TickCount % 5 == 0)
            UpdateVision();
        if (TickCount % 20 == 0)
            foreach (var c in Commanders)
                c.Update(this);
        UpdateMorale();
        foreach (var team in _teams)
            foreach (var s in team.Soldiers)
                MoveSoldier(s);
        UpdateCombat();
        UpdateFlags();
        _shots.RemoveAll(s => Time - s.Time > 0.6f);
        Time += TickSeconds;
        TickCount++;
        CheckForEnd();
    }

    public void CallCeasefire() => EndBattle("Ceasefire called");

    void CheckForEnd()
    {
        if (Time >= TimeLimit)
            EndBattle("Time ran out");
        else if (IsBroken(Side.Allied))
            EndBattle("The British force is broken");
        else if (IsBroken(Side.German))
            EndBattle("The German force is broken");
    }

    /// <summary>A side that has teams, none of them still fighting.</summary>
    bool IsBroken(Side side)
    {
        var teams = _teams.Where(t => t.Side == side).ToList();
        return teams.Count > 0 && teams.All(t => t.IsDestroyed);
    }

    void EndBattle(string reason)
    {
        if (IsOver) return;
        Result = BattleResult.From(this, reason);
    }

    /// <summary>Enemy soldiers this side can currently see.</summary>
    public IEnumerable<Soldier> VisibleEnemies(Side side) =>
        Soldiers.Where(s => s.Side != side && s.IsAlive && s.IsVisibleToEnemy(Time));
}
