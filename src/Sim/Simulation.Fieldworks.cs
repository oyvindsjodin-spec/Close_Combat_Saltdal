using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

public enum HouseState
{
    Standing,
    Burning,
    Ruined,
}

/// <summary>One house, barn or shed: a group of touching Building cells.</summary>
public sealed class House
{
    public int Id { get; }
    /// <summary>Map cell indexes (y * width + x).</summary>
    public IReadOnlyList<int> Cells { get; }
    public Vector2 Center { get; }
    public HouseState State { get; internal set; } = HouseState.Standing;
    public float BurningSince { get; internal set; }
    public float BurnsOutAt { get; internal set; }

    internal House(int id, List<int> cells, Vector2 center)
    {
        Id = id;
        Cells = cells;
        Center = center;
    }
}

/// <summary>A puff of smoke from a burning house, drifting with the wind. It blocks sight.</summary>
public readonly record struct SmokePuff(Vector2 Origin, float Born, float Life, float StartRadius, float EndRadius)
{
    public float Age(float now) => now - Born;
    public Vector2 PositionAt(float now, Vector2 wind) => Origin + wind * Age(now);
    public float RadiusAt(float now) => StartRadius + (EndRadius - StartRadius) * MathF.Min(1, Age(now) / Life);
}

/// <summary>A trench dug by one man: a cell of the map and the way he faced.</summary>
public readonly record struct Trench(int Cell, Vector2 At, float Facing, Side Side);

/// <summary>
/// Houses men can hold and that can burn, smoke that hides, trenches dug before
/// and during the battle, and the quiet spell before the attack begins.
/// </summary>
public sealed partial class Simulation
{
    /// <summary>Seconds one man needs to dig himself a trench.</summary>
    public const float DigSeconds = 90f;
    /// <summary>How long a house burns before it falls in.</summary>
    public const float BurnSeconds = 300f;
    public const float SmokeLife = 45f;

    List<House>? _houses;
    int[]? _houseAt;
    readonly List<SmokePuff> _smoke = new();
    readonly List<Trench> _trenches = new();

    public IReadOnlyList<House> Houses { get { BuildHouses(); return _houses!; } }
    public IReadOnlyList<SmokePuff> Smoke => _smoke;
    public IReadOnlyList<Trench> Trenches => _trenches;
    /// <summary>The steady breeze that carries smoke, in metres per second.</summary>
    public Vector2 Wind { get; set; } = new(1.5f, -0.8f);

    // ---------------------------------------------------------------- the quiet before the attack

    /// <summary>
    /// Until this time the attackers wait out of sight while the defenders get
    /// ready and dig in. No one fires and no guns can be called.
    /// </summary>
    public float PrepUntil { get; set; }
    public bool InPrep => Time < PrepUntil;
    /// <summary>The side that waits during the preparation: whoever holds no flags at the start.</summary>
    public Side Attacker => Flags.Any(f => f.Owner == Side.German) && !Flags.Any(f => f.Owner == Side.Allied) ? Side.Allied : Side.German;

    /// <summary>Starts the attack now instead of waiting for the preparation time to run out.</summary>
    public void EndPrep()
    {
        if (!InPrep) return;
        TimeLimit -= PrepUntil - Time;
        PrepUntil = Time;
        Log(null, "The attack begins!");
    }

    // ---------------------------------------------------------------- houses

    void BuildHouses()
    {
        if (_houses != null) return;
        _houses = new List<House>();
        _houseAt = new int[Map.Width * Map.Height];
        Array.Fill(_houseAt, -1);
        var stack = new Stack<int>();
        for (int i = 0; i < _houseAt.Length; i++)
        {
            if (Map.Terrain[i] != TerrainType.Building || _houseAt[i] >= 0) continue;
            var cells = new List<int>();
            int id = _houses.Count;
            stack.Push(i);
            _houseAt[i] = id;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                cells.Add(c);
                int cx = c % Map.Width, cy = c / Map.Width;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (!Map.InBounds(nx, ny)) continue;
                        int n = ny * Map.Width + nx;
                        if (Map.Terrain[n] != TerrainType.Building || _houseAt[n] >= 0) continue;
                        _houseAt[n] = id;
                        stack.Push(n);
                    }
            }
            var center = cells.Aggregate(Vector2.Zero, (a, c) => a + Map.CellCenter(c % Map.Width, c / Map.Width)) / cells.Count;
            _houses.Add(new House(id, cells, center));
        }
    }

    /// <summary>The house at a point, or null.</summary>
    public House? HouseAt(Vector2 p)
    {
        BuildHouses();
        var (x, y) = Map.WorldToCell(p);
        if (!Map.InBounds(x, y)) return null;
        int id = _houseAt![y * Map.Width + x];
        return id < 0 ? null : _houses![id];
    }

    /// <summary>
    /// Where the men of a team go inside a house: spread over its rooms, along
    /// the walls, so every window has someone at it.
    /// </summary>
    internal Vector2[] PlacesInHouse(House house, int men)
    {
        var cells = house.Cells.Select(c => Map.CellCenter(c % Map.Width, c / Map.Width))
            .OrderBy(p => MathF.Atan2(p.Y - house.Center.Y, p.X - house.Center.X)).ToList();
        var places = new Vector2[men];
        float r = Map.CellSize * 0.3f;
        for (int i = 0; i < men; i++)
        {
            var c = cells[i % cells.Count];
            int round = i / cells.Count;
            // Several men in one room stand apart, towards its outer walls
            var outward = c - house.Center;
            outward = outward.LengthSquared() > 0.01f ? Vector2.Normalize(outward) : new Vector2(1, 0);
            var side = new Vector2(-outward.Y, outward.X);
            places[i] = c + outward * r + side * r * (round % 2 == 0 ? round * 0.5f : -(round + 1) * 0.5f);
        }
        return places;
    }

    // ---------------------------------------------------------------- fire and smoke

    /// <summary>Shells and bombs can set houses near where they land on fire.</summary>
    void TryIgnite(Vector2 at, Explosive e)
    {
        BuildHouses();
        float reach = e.KillRadius + 3f;
        float chance = MathF.Min(0.6f, e.KillRadius * 0.035f);
        foreach (var house in _houses!)
        {
            if (house.State != HouseState.Standing) continue;
            if (Vector2.Distance(house.Center, at) > reach + 30f) continue;
            bool direct = HouseAt(at) == house;
            bool close = direct || house.Cells.Any(c => Vector2.Distance(Map.CellCenter(c % Map.Width, c / Map.Width), at) < reach);
            if (close && Rng.Chance(direct ? chance * 2 : chance))
                SetOnFire(house);
        }
    }

    public void SetOnFire(House house)
    {
        if (house.State != HouseState.Standing) return;
        house.State = HouseState.Burning;
        house.BurningSince = Time;
        house.BurnsOutAt = Time + BurnSeconds * Rng.Range(0.8f, 1.2f);
        foreach (var c in house.Cells) Map.Terrain[c] = TerrainType.BurningHouse;
        Log(null, "A house is on fire!");
    }

    void UpdateFire()
    {
        if (_houses == null) return;
        foreach (var house in _houses)
        {
            if (house.State != HouseState.Burning) continue;
            if (Time >= house.BurnsOutAt)
            {
                house.State = HouseState.Ruined;
                foreach (var c in house.Cells) Map.Terrain[c] = TerrainType.Ruin;
                continue;
            }
            // Smoke rises from the fire; more of it as the fire takes hold
            if (TickCount % 25 == house.Id % 25)
            {
                float heat = MathF.Min(1, (Time - house.BurningSince) / 30f);
                float size = 6f + MathF.Sqrt(house.Cells.Count) * Map.CellSize * 0.6f;
                _smoke.Add(new SmokePuff(house.Center + new Vector2(Rng.Range(-3, 3), Rng.Range(-3, 3)), Time, SmokeLife,
                    size * (0.5f + 0.5f * heat), size * 2.2f));
            }
            // Sparks set the houses next door alight now and then
            if (TickCount % 100 == house.Id % 100)
                foreach (var other in _houses)
                    if (other.State == HouseState.Standing && Vector2.Distance(other.Center, house.Center) < 14f + MathF.Sqrt(other.Cells.Count) * 3f
                        && Rng.Chance(0.05f))
                        SetOnFire(other);
        }
        _smoke.RemoveAll(p => p.Age(Time) > p.Life);

        // Men caught in a burning house suffer and get out, however frightened they are
        foreach (var team in _teams)
        {
            bool escaping = false;
            foreach (var s in team.Soldiers)
            {
                if (!s.IsAlive || Map.TerrainAt(s.Position) != TerrainType.BurningHouse) continue;
                if (s.CanAct) AddStress(s, 3f * TickSeconds);
                if (s.Health <= Health.Wounded && Rng.Chance(0.005f)) ApplyHit(s);
                if (s.CanAct && !s.EscapingFire && EscapeFire(s)) escaping = true;
            }
            if (escaping && TickCount - team.LastFireWarning > 100)
            {
                team.LastFireWarning = TickCount;
                Log(team.Side, $"{team.Name}: the house is burning, getting out!");
            }
        }
    }

    /// <summary>Sends one man out of a burning house to the nearest open ground away from the fire.</summary>
    bool EscapeFire(Soldier s)
    {
        var house = HouseAt(s.Position);
        var from = house?.Center ?? s.Position;
        var away = s.Position - from;
        if (away.LengthSquared() < 0.25f) away = new Vector2(0, s.Side == Side.Allied ? -1 : 1);
        away = Vector2.Normalize(away);
        // Try straight out first, then turning a little more each time
        for (int turn = 0; turn < 12; turn++)
        {
            float angle = (turn % 2 == 0 ? 1 : -1) * (turn + 1) / 2 * MathF.PI / 6;
            var dir = new Vector2(away.X * MathF.Cos(angle) - away.Y * MathF.Sin(angle), away.X * MathF.Sin(angle) + away.Y * MathF.Cos(angle));
            for (float d = 6; d <= 40; d += 4)
            {
                var spot = s.Position + dir * d;
                var (x, y) = Map.WorldToCell(spot);
                if (!Map.InBounds(x, y) || !Map.IsPassable(x, y)) continue;
                var t = Map.TerrainAt(x, y);
                if (t is TerrainType.BurningHouse or TerrainType.Building) continue;
                var route = Pathfinder.FindPath(s.Position, spot);
                if (route == null) continue;
                s.Path.Clear();
                foreach (var p in route) s.Path.Enqueue(p);
                s.Fleeing = false;
                s.EscapingFire = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether drifting smoke lies across the line between two points.</summary>
    bool SmokeBlocks(Vector2 a, Vector2 b)
    {
        foreach (var p in _smoke)
        {
            var c = p.PositionAt(Time, Wind);
            float r = p.RadiusAt(Time) * 0.6f;
            if (DistanceToSegment(c, a, b) < r) return true;
        }
        return false;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len = ab.LengthSquared();
        float t = len < 1e-4f ? 0 : Math.Clamp(Vector2.Dot(p - a, ab) / len, 0, 1);
        return Vector2.Distance(p, a + ab * t);
    }

    // ---------------------------------------------------------------- digging in

    /// <summary>
    /// Dig in where the team stands, facing a point. Each man digs his own
    /// trench, which takes a minute and a half; then the team defends from them.
    /// </summary>
    /// <summary>
    /// Dig in: the team spreads out into a line of foxholes about five metres
    /// apart across the way it faces, and each man digs his own.
    /// </summary>
    public void OrderDigIn(Team team, Vector2 facing)
    {
        SetStationaryOrder(team, TeamOrder.DigIn, facing);
        TakeUpLine(team, facing, mustDig: true);
    }

    /// <summary>Dig in here, in the shape and facing the player dragged out.</summary>
    public void OrderDigIn(Team team, Deployment deploy)
    {
        SetStationaryOrder(team, TeamOrder.DigIn, FacingPoint(team, deploy));
        team.Deployment = deploy;
        TakeUpLine(team, team.OrderPoint, mustDig: true);
    }

    public static bool CanDig(TerrainType t) => t is TerrainType.Grass or TerrainType.Field or TerrainType.Forest or TerrainType.Snow;

    /// <summary>The trench at a cell, if one has been dug there.</summary>
    public bool HasTrench(Vector2 p) => Map.TerrainAt(p) == TerrainType.Trench;

    void UpdateDigging()
    {
        foreach (var team in _teams)
        {
            if (team.Order != TeamOrder.DigIn) continue;
            bool anyDigging = false;
            foreach (var s in team.Soldiers)
            {
                if (!s.CanAct) continue;
                // A man still on his way to his place digs when he gets there
                if (s.IsMoving) { anyDigging = true; continue; }
                var (x, y) = Map.WorldToCell(s.Position);
                if (!Map.InBounds(x, y)) continue;
                var t = Map.TerrainAt(x, y);
                if (t == TerrainType.Trench || !CanDig(t)) { s.DigProgress = 0; continue; }
                if (s.Morale >= MoraleState.Pinned) { anyDigging = true; continue; }
                float rate = s.Stress > 40 ? 0.4f : 1f;
                s.DigProgress += TickSeconds * rate / DigSeconds;
                anyDigging = true;
                if (s.DigProgress >= 1f)
                {
                    s.DigProgress = 0;
                    int cell = y * Map.Width + x;
                    Map.Terrain[cell] = TerrainType.Trench;
                    _trenches.Add(new Trench(cell, s.Position, s.Facing, team.Side));
                }
            }
            if (!anyDigging)
            {
                if (team.Deployment is { } dep) OrderDefend(team, dep);
                else OrderDefend(team, team.OrderPoint);
                Log(team.Side, team.Active.Any(s => HasTrench(s.Position))
                    ? $"{team.Name} has dug in."
                    : $"{team.Name} cannot dig here: the ground is too hard or built on.");
            }
        }
    }

    /// <summary>How far the team's digging has got, from 0 to 1, for showing on its card.</summary>
    public float DigProgress(Team team)
    {
        var men = team.Active.ToList();
        if (men.Count == 0) return 0;
        return men.Average(s => HasTrench(s.Position) ? 1f : s.DigProgress);
    }

    // ---------------------------------------------------------------- going back into foxholes

    /// <summary>Whether a man is down in a foxhole: in a dug cell and not climbing out of it.</summary>
    public bool InFoxhole(Soldier s) => !s.IsOnTheMove && Map.TerrainAt(s.Position) == TerrainType.Trench;

    int CellIndex(Vector2 p)
    {
        var (x, y) = Map.WorldToCell(p);
        return Map.InBounds(x, y) ? y * Map.Width + x : -1;
    }

    /// <summary>
    /// A foxhole is taken while a steady man of its side stays in it, or is on
    /// his way to it. The men of <paramref name="leaving"/> do not count: they are
    /// being given a new order and will climb out.
    /// </summary>
    bool FoxholeTaken(Trench t, Team? leaving) => Soldiers.Any(s =>
        s.CanAct && s.Side == t.Side && s.Team != leaving && !s.Fleeing &&
                (s.Path.Count == 0 ? CellIndex(s.Position) == t.Cell : CellIndex(s.Path.Last()) == t.Cell));

    /// <summary>Metres from a point within which a team looks for empty foxholes of its own side.</summary>
    public const float FoxholeReach = 10f;

    /// <summary>
    /// The empty foxholes of a side around a point: those within
    /// <see cref="FoxholeReach"/> of it, and the rest of the line they belong
    /// to, nearest first, at most <paramref name="count"/>.
    /// </summary>
    internal List<Trench> FreeFoxholesAround(Side side, Vector2 at, int count, Team? leaving)
    {
        var free = _trenches.Where(t => t.Side == side && Map.Terrain[t.Cell] == TerrainType.Trench && !FoxholeTaken(t, leaving)).ToList();
        var picked = free.Where(t => Vector2.Distance(t.At, at) <= FoxholeReach).ToList();
        if (picked.Count == 0) return picked;
        // Follow the line on from hole to hole, so a click on one end fills it all
        bool grew = true;
        while (grew && picked.Count < count + 4)
        {
            grew = false;
            foreach (var t in free)
                if (!picked.Contains(t) && picked.Any(q => Vector2.Distance(q.At, t.At) <= FoxholeSpacing * 2.2f))
                {
                    picked.Add(t);
                    grew = true;
                }
        }
        return picked.OrderBy(t => Vector2.DistanceSquared(t.At, at)).Take(count).ToList();
    }

    /// <summary>
    /// Pairs men with places so nobody walks far: the closest man and place
    /// first, then the next closest of those left. Returns, for each man, the
    /// index of his place, or -1 if there were not enough places.
    /// </summary>
    internal static int[] MatchNearest(IReadOnlyList<Vector2> men, IReadOnlyList<Vector2> places)
    {
        var result = Enumerable.Repeat(-1, men.Count).ToArray();
        var pairs = new List<(float D, int Man, int Place)>();
        for (int i = 0; i < men.Count; i++)
            for (int j = 0; j < places.Count; j++)
                pairs.Add((Vector2.DistanceSquared(men[i], places[j]), i, j));
        var used = new bool[places.Count];
        foreach (var (_, i, j) in pairs.OrderBy(p => p.D))
        {
            if (result[i] >= 0 || used[j]) continue;
            result[i] = j;
            used[j] = true;
        }
        return result;
    }

    void UpdateFieldworks()
    {
        UpdateFire();
        UpdateDigging();
    }
}
