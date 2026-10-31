using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>
/// A defending commander. Every rifle team has a post at one of the flags and
/// holds it facing the enemy. Teams at quiet flags reinforce a flag under heavy
/// attack, and a lost flag is counter-attacked when the enemy holding it is weak
/// enough. Guns are called on the thickest group of attackers in sight.
/// </summary>
public sealed class DefendAI : ICommander
{
    public Side Side { get; }

    /// <summary>Enemies this close to a flag count as attacking it.</summary>
    public float ThreatRange { get; set; } = 300f;
    /// <summary>A counter-attack stops this far short of the flag before the final rush.</summary>
    public float StagingDistance { get; set; } = 100f;

    readonly Dictionary<Team, Post> _posts = new();
    readonly Dictionary<Team, float> _busyUntil = new();
    readonly SupportPlanner _support = new(firstCall: 60f) { Cooldown = 300f };
    bool _started;

    sealed record Post(VictoryFlag Flag, Vector2 Spot);

    public DefendAI(Side side) => Side = side;

    public void Update(Simulation sim)
    {
        var enemies = sim.VisibleEnemies(Side).Where(e => e.CanAct).ToList();
        _support.Update(sim, Side, enemies);
        var teams = sim.Teams.Where(t => t.Side == Side && !t.IsDestroyed).ToList();
        if (!_started)
        {
            AssignPosts(sim, teams);
            _started = true;
        }

        // How hard each flag is being pressed, and by how many of ours it is held
        var threat = sim.Flags.ToDictionary(f => f, f => enemies.Count(e => Vector2.Distance(e.Position, f.Position) < ThreatRange));
        var held = sim.Flags.ToDictionary(f => f, f => _posts.Where(p => p.Value.Flag == f && !p.Key.IsDestroyed)
                                                              .Sum(p => p.Key.EffectiveCount));

        CounterAttack(sim, teams, enemies, threat);
        Reinforce(sim, teams, threat, held);

        foreach (var team in teams)
        {
            if (team.Morale >= MoraleState.Cowering) continue;
            if (_busyUntil.TryGetValue(team, out var until) && sim.Time < until) continue;
            if (!_posts.TryGetValue(team, out var post)) continue;
            if (team.IsMoving)
            {
                CallOffIfStalled(sim, team, post, enemies);
                continue;
            }

            var nearest = enemies.OrderBy(e => Vector2.DistanceSquared(e.Position, team.Center)).FirstOrDefault();
            float toPost = Vector2.Distance(team.Center, post.Spot);
            if (toPost > 25f)
            {
                GoTo(sim, team, post, nearest);
                continue;
            }
            // At the post: lie down facing the nearest enemy, or where the enemy comes from
            var facing = nearest?.Position ?? post.Spot + EnemyDirection(sim) * 200f;
            float nearestDist = nearest == null ? float.MaxValue : Vector2.Distance(nearest.Position, team.Center);
            // Dig in while the enemy is far off; stop digging and fight when he comes close
            if (team.Order == TeamOrder.DigIn)
            {
                if (nearestDist < 200f) sim.OrderDefend(team, facing);
                continue;
            }
            int diggable = team.Active.Count(s => !sim.HasTrench(s.Position) && Simulation.CanDig(sim.Map.TerrainAt(s.Position)));
            if (diggable * 2 > team.EffectiveCount && nearestDist > 300f)
            {
                sim.OrderDigIn(team, post.Spot + EnemyDirection(sim) * 200f);
                continue;
            }
            bool turnToFace = nearest != null && team.Order == TeamOrder.Defend
                              && Angle(team.OrderPoint - team.Center, facing - team.Center) > 1.2f;
            if (team.Order != TeamOrder.Defend || turnToFace)
                sim.OrderDefend(team, facing);
        }
    }

    /// <summary>Each rifle team holds the flag nearest to where it starts; support teams stay where they are.</summary>
    void AssignPosts(Simulation sim, List<Team> teams)
    {
        if (sim.Flags.Count == 0) return;
        foreach (var team in teams)
        {
            var flag = sim.Flags.OrderBy(f => Vector2.DistanceSquared(f.Position, team.Center)).First();
            bool support = team.IsHeadquarters || team.Soldiers.Any(s => s.Weapon.IsIndirect);
            // A team placed close to its flag keeps its place; others move onto the flag
            var spot = support || Vector2.Distance(team.Center, flag.Position) < 60f
                ? team.Center
                : SpotAt(flag, team);
            _posts[team] = new Post(flag, spot);
        }
    }

    /// <summary>
    /// Retake a flag the enemy holds when the enemy there is weaker than the
    /// teams that can be spared for it.
    /// </summary>
    void CounterAttack(Simulation sim, List<Team> teams, List<Soldier> enemies, Dictionary<VictoryFlag, int> threat)
    {
        foreach (var flag in sim.Flags.Where(f => f.Owner != Side))
        {
            // Count everyone seen around the flag, not just on it: a counter-attack
            // into a wood full of the enemy only feeds him men
            int enemyOnFlag = enemies.Count(e => Vector2.Distance(e.Position, flag.Position) < flag.Radius + 120f);
            var free = teams.Where(t => CanSpare(sim, t, threat))
                .OrderBy(t => Vector2.DistanceSquared(t.Center, flag.Position))
                .Take(2).ToList();
            int strength = free.Sum(t => t.EffectiveCount);
            if (free.Count == 0 || strength < 4 || enemyOnFlag * 2f > strength) continue;
            foreach (var team in free)
            {
                _posts[team] = new Post(flag, SpotAt(flag, team));
                _busyUntil.Remove(team);
                sim.Stop(team);
            }
            sim.Log(Side, $"Counter-attack on {flag.Name}.");
        }
    }

    /// <summary>Move one team from a quiet flag to a flag under heavy attack.</summary>
    void Reinforce(Simulation sim, List<Team> teams, Dictionary<VictoryFlag, int> threat, Dictionary<VictoryFlag, int> held)
    {
        var pressed = sim.Flags.Where(f => f.Owner == Side && threat[f] >= 4 && threat[f] > held[f])
            .OrderByDescending(f => f.Value * threat[f]).FirstOrDefault();
        if (pressed == null) return;
        var helper = teams.Where(t => CanSpare(sim, t, threat) && _posts[t].Flag != pressed && threat[_posts[t].Flag] <= 1)
            .OrderBy(t => Vector2.DistanceSquared(t.Center, pressed.Position)).FirstOrDefault();
        if (helper == null) return;
        _posts[helper] = new Post(pressed, SpotAt(pressed, helper));
        held[pressed] += helper.EffectiveCount;
        sim.Stop(helper);
        sim.Log(Side, $"{helper.Name} moves up to reinforce {pressed.Name}.");
    }

    /// <summary>
    /// A counter-attack that has gone to ground under fire is called off: the
    /// team holds where it is and fights from there, instead of getting up
    /// again and again into the same fire.
    /// </summary>
    void CallOffIfStalled(Simulation sim, Team team, Post post, List<Soldier> enemies)
    {
        if (post.Flag.Owner == Side) return;
        bool underFire = team.Active.Any(s => sim.Time - s.LastUnderFire < 3f);
        int pinned = team.Active.Count(s => s.Morale >= MoraleState.Pinned);
        if (!underFire || pinned * 3 < team.EffectiveCount) return;
        var nearest = enemies.OrderBy(e => Vector2.DistanceSquared(e.Position, team.Center)).FirstOrDefault();
        sim.Stop(team);
        sim.OrderDefend(team, nearest?.Position ?? post.Flag.Position);
        _busyUntil[team] = sim.Time + 90f;
        sim.Log(Side, $"{team.Name}: the counter-attack on {post.Flag.Name} is pinned down; holding here.");
    }

    /// <summary>A steady rifle team whose own flag can do without it.</summary>
    bool CanSpare(Simulation sim, Team team, Dictionary<VictoryFlag, int> threat)
    {
        if (team.IsHeadquarters || team.Soldiers.Any(s => s.Weapon.IsIndirect)) return false;
        if (!_posts.TryGetValue(team, out var post)) return false;
        if (team.Morale > MoraleState.Ready || team.EffectiveCount < 3) return false;
        if (team.Active.Select(s => s.Stress).DefaultIfEmpty(0).Average() > 35f) return false;
        if (post.Flag.Owner != Side) return false; // already going for a lost flag
        // Do not strip the last team from a flag that is being attacked
        bool lastHolder = !_posts.Any(p => p.Key != team && !p.Key.IsDestroyed && p.Value.Flag == post.Flag
                                           && !p.Key.IsHeadquarters);
        return !(lastHolder && threat[post.Flag] > 0);
    }

    /// <summary>
    /// Walk to a post. Near the enemy, go through cover and stop short of the
    /// flag, then rush the last stretch.
    /// </summary>
    void GoTo(Simulation sim, Team team, Post post, Soldier? nearest)
    {
        float enemyDist = nearest == null ? float.MaxValue : Vector2.Distance(nearest.Position, team.Center);
        bool underFire = team.Active.Any(s => sim.Time - s.LastUnderFire < 3f);
        float toPost = Vector2.Distance(team.Center, post.Spot);
        bool enemyAtFlag = post.Flag.Owner != Side;
        if (enemyAtFlag && toPost <= StagingDistance + 30f)
        {
            sim.OrderMove(team, post.Spot, MoveMode.MoveFast);
            _busyUntil[team] = sim.Time + 20f;
            return;
        }
        if (enemyAtFlag)
        {
            sim.OrderMove(team, post.Spot, MoveMode.Move, preferCover: true, maxDistance: toPost - StagingDistance);
            return;
        }
        var mode = underFire ? MoveMode.MoveFast : enemyDist < 250f ? MoveMode.Sneak : MoveMode.Move;
        sim.OrderMove(team, post.Spot, mode, preferCover: enemyDist < 600f || underFire);
    }

    /// <summary>A place inside the flag's circle, spread so teams do not pile up on one spot.</summary>
    static Vector2 SpotAt(VictoryFlag flag, Team team)
    {
        float angle = team.Id * 2.1f;
        return flag.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * flag.Radius * 0.5f;
    }

    Vector2 EnemyDirection(Simulation sim)
    {
        float ours = Side == Side.Allied ? sim.AlliedRetreatY : sim.GermanRetreatY;
        float theirs = Side == Side.Allied ? sim.GermanRetreatY : sim.AlliedRetreatY;
        return new Vector2(0, MathF.Sign(theirs - ours));
    }

    static float Angle(Vector2 a, Vector2 b)
    {
        if (a.LengthSquared() < 1 || b.LengthSquared() < 1) return 0;
        float d = Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b));
        return MathF.Acos(Math.Clamp(d, -1f, 1f));
    }
}
