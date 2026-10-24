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

/// <summary>One man on the map. Morale, weapons and health arrive in Phase 1.</summary>
public sealed class Soldier
{
    public int Id { get; }
    public string Name { get; }
    public Team Team { get; internal set; } = null!;

    public Vector2 Position { get; internal set; }
    /// <summary>Position at the start of the last tick, for smooth drawing between ticks.</summary>
    public Vector2 PreviousPosition { get; internal set; }
    /// <summary>Direction the soldier faces, in radians (0 = east).</summary>
    public float Facing { get; internal set; }

    internal readonly Queue<Vector2> Path = new();
    public IEnumerable<Vector2> Waypoints => Path;
    public bool IsMoving => Path.Count > 0;

    public Soldier(int id, string name, Vector2 position)
    {
        Id = id;
        Name = name;
        Position = position;
        PreviousPosition = position;
    }
}
