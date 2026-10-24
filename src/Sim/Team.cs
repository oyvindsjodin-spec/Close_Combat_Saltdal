using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>A squad or team: the unit the player gives orders to.</summary>
public sealed class Team
{
    public int Id { get; }
    public string Name { get; }
    public Side Side { get; }
    public MoveMode Mode { get; internal set; } = MoveMode.Move;

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

    public Vector2 Center => _soldiers.Count == 0
        ? Vector2.Zero
        : _soldiers.Aggregate(Vector2.Zero, (sum, s) => sum + s.Position) / _soldiers.Count;

    public bool IsMoving => _soldiers.Any(s => s.IsMoving);
}
