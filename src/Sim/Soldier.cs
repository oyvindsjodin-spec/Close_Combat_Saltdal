using System.Collections.Generic;
using System.Numerics;

namespace Saltdal.Sim;

public enum Side
{
    Allied,
    German,
}

public enum MoveMode
{
    Sneak,
    Move,
    MoveFast,
}

public enum Health
{
    Healthy,
    Wounded,
    Incapacitated,
    Dead,
}

/// <summary>A soldier's state of mind, from calm to broken. Driven by stress.</summary>
public enum MoraleState
{
    Ready,
    Pinned,
    Cowering,
    Panicked,
    Routed,
    Surrendered,
}

public enum Posture
{
    Standing,
    Crouched,
    Prone,
}

/// <summary>One man on the map.</summary>
public sealed class Soldier
{
    public int Id { get; }
    public string Name { get; }
    public Team Team { get; internal set; } = null!;
    public Side Side => Team.Side;

    public Weapon Weapon { get; }
    public int Ammo { get; internal set; }
    public bool IsLeader { get; }
    /// <summary>How far he has dug his trench, from 0 to 1.</summary>
    public float DigProgress { get; internal set; }
    /// <summary>0 is a green recruit, 1 a veteran.</summary>
    public float Experience { get; }

    public Health Health { get; internal set; } = Health.Healthy;
    /// <summary>0 is calm; 25 pinned, 50 cowering, 75 panicked, 100 routed.</summary>
    public float Stress { get; internal set; }
    public MoraleState Morale { get; internal set; } = MoraleState.Ready;
    public Posture Posture { get; internal set; } = Posture.Crouched;

    public Vector2 Position { get; internal set; }
    /// <summary>Position at the start of the last tick, for smooth drawing between ticks.</summary>
    public Vector2 PreviousPosition { get; internal set; }
    /// <summary>Direction the soldier faces, in radians (0 = east).</summary>
    public float Facing { get; internal set; }

    /// <summary>Who he is shooting at, if anyone.</summary>
    public Soldier? Target { get; internal set; }
    /// <summary>A point he is putting suppressive fire on, when there is no man to aim at.</summary>
    public Vector2? AreaTarget { get; internal set; }

    /// <summary>Last time any enemy soldier saw him, and where.</summary>
    public float LastSeenByEnemy { get; internal set; } = float.NegativeInfinity;
    public Vector2 LastKnownPosition { get; internal set; }

    internal readonly Queue<Vector2> Path = new();
    public IEnumerable<Vector2> Waypoints => Path;
    public bool IsMoving => Path.Count > 0;

    internal float FireCooldown;
    internal int BurstLeft;
    /// <summary>Bombs still to fire at a Fire order's point (mortars only).</summary>
    internal int BarrageLeft;
    internal float LastUnderFire = float.NegativeInfinity;
    internal float LastFired = float.NegativeInfinity;
    /// <summary>When he last fired, for drawing muzzle flashes.</summary>
    public float LastShotTime => LastFired;
    /// <summary>When he was put out of action, for the falling animation; -1 while he is on his feet.</summary>
    public float DownAt { get; internal set; } = -1;
    internal bool Fleeing;
    /// <summary>Getting out of a burning house: he goes even if frightened.</summary>
    internal bool EscapingFire;

    public Soldier(int id, string name, Vector2 position, Weapon weapon, bool isLeader = false, float experience = 0.5f)
    {
        Id = id;
        Name = name;
        Position = position;
        PreviousPosition = position;
        Weapon = weapon;
        Ammo = weapon.Ammo;
        IsLeader = isLeader;
        Experience = experience;
    }

    public bool IsAlive => Health != Health.Dead;
    /// <summary>Can move and act at all.</summary>
    public bool CanAct => Health <= Health.Wounded && Morale != MoraleState.Surrendered;
    /// <summary>Still counts as a fighting man: for flags and for the battle's end.</summary>
    public bool IsEffective => CanAct && Morale < MoraleState.Routed;
    public bool WillFire => CanAct && Morale <= MoraleState.Pinned && Ammo > 0;

    public bool IsVisibleToEnemy(float now) => now - LastSeenByEnemy < Simulation.SightMemorySeconds;
}
