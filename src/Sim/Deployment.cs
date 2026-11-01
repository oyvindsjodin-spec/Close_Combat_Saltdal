using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>The shapes a team can be told to take up.</summary>
public enum Formation
{
    /// <summary>A line across the way the team faces: the most rifles to the front.</summary>
    Line = 1,
    /// <summary>An arrowhead pointing forward: good all round, for moving into the unknown.</summary>
    Wedge = 2,
    /// <summary>Two files one behind the other: narrow, for roads and gaps.</summary>
    Column = 3,
}

/// <summary>
/// How the player wants a team to stand when it gets where it is going, set by
/// holding the right mouse button and dragging: which way the men face (radians),
/// the shape, and the metres between neighbours.
/// </summary>
public readonly record struct Deployment(float Facing, Formation Shape, float Spacing)
{
    public const float MinSpacing = 2f, MaxSpacing = 12f;

    /// <summary>
    /// The deployment for a drag from one point to another: the men face along
    /// the drag, and the longer the drag, the wider they spread.
    /// </summary>
    public static Deployment FromDrag(Vector2 from, Vector2 to, Formation shape, int count)
    {
        var d = to - from;
        float facing = MathF.Atan2(d.Y, d.X);
        float spacing = Math.Clamp(d.Length() / Math.Max(1, count - 1), MinSpacing, MaxSpacing);
        return new Deployment(facing, shape, spacing);
    }

    public Vector2 Forward => new(MathF.Cos(Facing), MathF.Sin(Facing));

    /// <summary>
    /// Where each man stands, relative to the middle of the formation. Man 0 is
    /// the leader when there is one; he stays a little behind the others in a
    /// line or wedge, and in front of a column.
    /// </summary>
    public Vector2[] Offsets(int count, bool leaderFirst)
    {
        var local = new Vector2[count]; // X across (to the right), Y forward
        bool leader = leaderFirst && count > 2;
        int m = leader ? count - 1 : count;
        float s = Spacing;
        for (int k = 0; k < m; k++)
        {
            int rank = (k + 1) / 2;
            float side = k % 2 == 1 ? -1 : 1;
            local[leader ? k + 1 : k] = Shape switch
            {
                Formation.Wedge => new Vector2(side * rank * s * 0.7f, -rank * s * 0.7f),
                Formation.Column => new Vector2((k % 2 == 0 ? -1 : 1) * Math.Min(s, 4f) * 0.5f, -(k / 2) * s),
                // Staggered a little so the men do not stand in a row an enemy could rake along
                _ => new Vector2((k - (m - 1) / 2f) * s, k % 2 == 0 ? 1f : -1f),
            };
        }
        if (leader)
            local[0] = Shape == Formation.Column ? new Vector2(0, s) : new Vector2(0, -Math.Max(s, 4f));
        // Centre the shape on the spot the player chose
        var mid = local.Aggregate(Vector2.Zero, (a, p) => a + p) / Math.Max(1, count);
        var forward = Forward;
        var across = new Vector2(-forward.Y, forward.X);
        return local.Select(p => across * (p.X - mid.X) + forward * (p.Y - mid.Y)).ToArray();
    }

    /// <summary>Metres from the leftmost man to the rightmost, for showing the player.</summary>
    public float Width(int count, bool leaderFirst)
    {
        var across = new Vector2(-Forward.Y, Forward.X);
        var xs = Offsets(count, leaderFirst).Select(p => Vector2.Dot(p, across)).ToList();
        return xs.Count == 0 ? 0 : xs.Max() - xs.Min();
    }
}

public sealed partial class Simulation
{
    /// <summary>
    /// Places in a deployment around a point, each matched to the man who can
    /// reach it without crossing the others: the men are sorted left to right
    /// (front to back for a column), and so are the places. The leader keeps his own place.
    /// </summary>
    List<(Soldier Man, Vector2 Place)> Assign(IReadOnlyList<Soldier> men, Vector2 from, Vector2 around, Deployment d, bool mustDig)
    {
        bool leaderFirst = men.Count > 2 && men[0].IsLeader;
        var offsets = d.Offsets(men.Count, leaderFirst);
        // Left to right, or front to back for a column
        var axis = d.Shape == Formation.Column ? d.Forward : new Vector2(-d.Forward.Y, d.Forward.X);
        var other = d.Shape == Formation.Column ? new Vector2(-d.Forward.Y, d.Forward.X) : d.Forward;
        var result = new List<(Soldier, Vector2)>();
        int skip = leaderFirst ? 1 : 0;
        if (leaderFirst) result.Add((men[0], Place(around + offsets[0], mustDig)));
        var byAcross = men.Skip(skip).OrderBy(s => Vector2.Dot(s.Position - from, axis)).ThenBy(s => s.Id).ToList();
        var places = Enumerable.Range(skip, men.Count - skip)
            .OrderBy(i => Vector2.Dot(offsets[i], axis)).ThenBy(i => Vector2.Dot(offsets[i], other)).ToList();
        for (int i = 0; i < byAcross.Count; i++)
            result.Add((byAcross[i], Place(around + offsets[places[i]], mustDig)));
        return result;
    }
}
