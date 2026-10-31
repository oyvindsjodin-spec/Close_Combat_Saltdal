using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>A victory location. Whoever holds it, with no enemy near, owns it.</summary>
public sealed class VictoryFlag
{
    public string Name { get; }
    public Vector2 Position { get; }
    public float Radius { get; }
    public int Value { get; }
    public Side? Owner { get; internal set; }

    public VictoryFlag(string name, Vector2 position, Side? owner, int value = 1, float radius = 30f)
    {
        Name = name;
        Position = position;
        Owner = owner;
        Value = value;
        Radius = radius;
    }
}

public sealed partial class Simulation
{
    void UpdateFlags()
    {
        if (TickCount % 5 != 0) return;
        foreach (var f in Flags)
        {
            bool allied = false, german = false;
            foreach (var s in Soldiers)
            {
                if (!s.IsEffective) continue;
                if (Vector2.DistanceSquared(s.Position, f.Position) > f.Radius * f.Radius) continue;
                if (s.Side == Side.Allied) allied = true; else german = true;
            }
            if (allied && !german) f.Owner = Side.Allied;
            else if (german && !allied) f.Owner = Side.German;
        }
    }
}

/// <summary>How a battle ended, and what it cost each side.</summary>
public sealed record BattleResult(
    string Reason,
    Side? Winner,
    int AlliedFlagValue,
    int GermanFlagValue,
    Losses AlliedLosses,
    Losses GermanLosses)
{
    internal static BattleResult From(Simulation sim, string reason)
    {
        int allied = sim.Flags.Where(f => f.Owner == Side.Allied).Sum(f => f.Value);
        int german = sim.Flags.Where(f => f.Owner == Side.German).Sum(f => f.Value);
        Side? winner = allied > german ? Side.Allied : german > allied ? Side.German : null;
        return new BattleResult(reason, winner, allied, german,
            Losses.Count(sim, Side.Allied), Losses.Count(sim, Side.German));
    }
}

public sealed record Losses(int Men, int Killed, int Incapacitated, int Wounded, int Surrendered, int Routed)
{
    internal static Losses Count(Simulation sim, Side side)
    {
        var men = sim.Soldiers.Where(s => s.Side == side).ToList();
        return new Losses(
            men.Count,
            men.Count(s => s.Health == Health.Dead),
            men.Count(s => s.Health == Health.Incapacitated),
            men.Count(s => s.Health == Health.Wounded),
            men.Count(s => s.Morale == MoraleState.Surrendered),
            men.Count(s => s.CanAct && s.Morale == MoraleState.Routed));
    }
}
