using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>The orders a team can be given, as in Close Combat.</summary>
public enum TeamOrder
{
    None,
    Move,
    MoveFast,
    Sneak,
    Fire,
    Defend,
    Hide,
}

/// <summary>A squad or team: the unit the player gives orders to.</summary>
public sealed class Team
{
    public int Id { get; }
    public string Name { get; }
    public Side Side { get; }

    public TeamOrder Order { get; internal set; } = TeamOrder.None;
    /// <summary>Where to shoot for Fire, or which way to face for Defend.</summary>
    public Vector2 OrderPoint { get; internal set; }

    readonly List<Soldier> _soldiers = new();
    public IReadOnlyList<Soldier> Soldiers => _soldiers;

    public Team(int id, string name, Side side)
    {
        Id = id;
        Name = name;
        Side = side;
    }

    public void Add(Soldier s)
    {
        s.Team = this;
        _soldiers.Add(s);
    }

    public MoveMode Mode => Order switch
    {
        TeamOrder.MoveFast => MoveMode.MoveFast,
        TeamOrder.Sneak => MoveMode.Sneak,
        _ => MoveMode.Move,
    };

    public IEnumerable<Soldier> Active => _soldiers.Where(s => s.CanAct);

    /// <summary>Centre of the men still able to act, or of everyone if none are.</summary>
    public Vector2 Center
    {
        get
        {
            var men = _soldiers.Where(s => s.CanAct).ToList();
            if (men.Count == 0) men = _soldiers;
            return men.Count == 0 ? Vector2.Zero : men.Aggregate(Vector2.Zero, (sum, s) => sum + s.Position) / men.Count;
        }
    }

    public bool IsMoving => _soldiers.Any(s => s.CanAct && s.IsMoving);
    public int EffectiveCount => _soldiers.Count(s => s.IsEffective);
    public bool IsDestroyed => EffectiveCount == 0;

    /// <summary>The worst state among men still on their feet, for the team panel.</summary>
    public MoraleState Morale
    {
        get
        {
            var active = _soldiers.Where(s => s.Health <= Health.Wounded).ToList();
            if (active.Count == 0) return MoraleState.Routed;
            // Most men decide what the team does; report the state of the median man
            var states = active.Select(s => s.Morale).OrderBy(m => m).ToList();
            return states[states.Count / 2];
        }
    }

    public int AmmoPercent
    {
        get
        {
            int carried = _soldiers.Where(s => s.CanAct).Sum(s => s.Ammo);
            int full = _soldiers.Where(s => s.CanAct).Sum(s => s.Weapon.Ammo);
            return full == 0 ? 0 : carried * 100 / full;
        }
    }
}
