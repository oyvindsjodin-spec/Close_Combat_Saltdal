using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>
/// A simple attacking commander. Each team advances on the nearest flag it does
/// not hold; when it meets the enemy it stops and fights for a while, then
/// creeps forward again. Shaken teams wait until they calm down.
/// </summary>
public sealed class AttackAI : ICommander
{
    public Side Side { get; }

    /// <summary>Enemies closer than this make a team stop and fight.</summary>
    public float EngageRange { get; set; } = 400f;
    /// <summary>How far a team moves in one bound before it stops to look and shoot.</summary>
    public float BoundLength { get; set; } = 150f;
    /// <summary>How far a team dashes when moving under fire.</summary>
    public float RushLength { get; set; } = 60f;
    /// <summary>Teams this close to their flag charge it.</summary>
    public float AssaultRange { get; set; } = 180f;

    readonly Dictionary<Team, float> _fightingUntil = new();
    readonly SupportPlanner _support = new(firstCall: 120f);
    float _lastContact = float.NegativeInfinity;

    public AttackAI(Side side) => Side = side;

    public void Update(Simulation sim)
    {
        if (sim.InPrep) return; // waiting out of sight until the attack begins
        var enemies = sim.VisibleEnemies(Side).Where(e => e.CanAct).ToList();
        _support.Update(sim, Side, enemies);
        // The whole force goes for one flag at a time: the open one nearest its centre
        var ours = sim.Soldiers.Where(s => s.Side == Side && s.IsEffective).ToList();
        var mass = ours.Count == 0 ? Vector2.Zero : ours.Aggregate(Vector2.Zero, (a, s) => a + s.Position) / ours.Count;
        var focus = ChooseFocus(sim, mass);
        foreach (var team in sim.Teams.Where(t => t.Side == Side && !t.IsDestroyed))
        {
            if (team.Morale >= MoraleState.Cowering)
                continue;
            if (_fightingUntil.TryGetValue(team, out var until) && sim.Time < until)
                continue;
            if (team.Soldiers.Any(s => s.Weapon.IsIndirect))
            {
                PlaceSupportTeam(sim, team, focus);
                continue;
            }
            if (team.IsHeadquarters)
            {
                FollowAttack(sim, team, mass, focus);
                continue;
            }

            var center = team.Center;
            var nearest = enemies.OrderBy(e => Vector2.DistanceSquared(e.Position, center)).FirstOrDefault();
            float enemyDist = nearest == null ? float.MaxValue : Vector2.Distance(nearest.Position, center);

            // Contact: stop and shoot it out, unless already creeping in under fire
            bool underFire = team.Active.Any(s => sim.Time - s.LastUnderFire < 3f);
            bool advancingCarefully = team.IsMoving && team.Order == TeamOrder.Sneak;
            if (nearest != null && (enemyDist < EngageRange || underFire) && !advancingCarefully && team.Order != TeamOrder.Defend)
            {
                sim.OrderDefend(team, nearest.Position);
                _fightingUntil[team] = sim.Time + sim.Rng.Range(25f, 40f);
                continue;
            }
            if (underFire && team.IsMoving && team.Order != TeamOrder.Sneak)
            {
                // Shot at by an enemy we cannot see: keep going, but low and slow
                sim.OrderMove(team, team.OrderPoint, MoveMode.Sneak, preferCover: true);
                continue;
            }
            if (team.IsMoving)
                continue;

            float avgStress = team.Active.Select(s => s.Stress).DefaultIfEmpty(0).Average();
            if (avgStress > 45f)
                continue;

            var objective = focus;
            if (objective == null)
            {
                // Everything taken: hold the nearest flag
                var hold = sim.Flags.OrderBy(f => Vector2.DistanceSquared(f.Position, center)).FirstOrDefault();
                if (hold == null || Vector2.Distance(hold.Position, center) < hold.Radius) continue;
                objective = hold;
            }

            // Spread the teams around the flag instead of piling onto it
            float angle = team.Id * 2.1f;
            var goal = objective.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 12f;

            // Close enough and steady enough: rush the flag
            float toFlag = Vector2.Distance(objective.Position, center);
            if (toFlag < AssaultRange && team.EffectiveCount >= 4 && avgStress < 35f)
            {
                sim.OrderMove(team, goal, MoveMode.MoveFast);
                _fightingUntil[team] = sim.Time + 20f;
                continue;
            }
            // In contact, move in short bounds through cover; out of contact, go all the way
            bool contact = underFire || enemyDist < EngageRange + 150f || sim.Time - _lastContact < 120f;
            if (underFire || nearest != null) _lastContact = sim.Time;
            // Under fire, dash from cover to cover; close to an enemy who has not
            // seen us, creep; otherwise walk
            var mode = underFire ? MoveMode.MoveFast : enemyDist < 250f ? MoveMode.Sneak : MoveMode.Move;
            float length = underFire ? RushLength : BoundLength;
            float bound = contact && Vector2.Distance(goal, center) > length * 1.5f ? length : float.PositiveInfinity;
            sim.OrderMove(team, goal, mode, preferCover: contact, maxDistance: bound);
        }
    }

    VictoryFlag? _focus;
    float _focusChosenAt = float.NegativeInfinity;

    /// <summary>
    /// The open flag that is quickest to reach on foot from the middle of the
    /// force, so a river or a cliff in the way counts against it. Re-chosen
    /// every minute, or at once when the current one is taken.
    /// </summary>
    VictoryFlag? ChooseFocus(Simulation sim, Vector2 mass)
    {
        bool stillOpen = _focus != null && _focus.Owner != Side;
        if (stillOpen && sim.Time - _focusChosenAt < 60f) return _focus;
        _focusChosenAt = sim.Time;
        _focus = null;
        float best = float.MaxValue;
        foreach (var f in sim.Flags.Where(f => f.Owner != Side))
        {
            var path = sim.Pathfinder.FindPath(mass, f.Position);
            float length = path == null ? float.MaxValue / 2 : PathLength(mass, path);
            // A big prize is worth a longer walk
            length /= 1f + 0.25f * (f.Value - 1);
            if (length < best)
            {
                best = length;
                _focus = f;
            }
        }
        return _focus;
    }

    static float PathLength(Vector2 from, List<Vector2> path)
    {
        float total = 0;
        var prev = from;
        foreach (var p in path)
        {
            total += Vector2.Distance(prev, p);
            prev = p;
        }
        return total;
    }

    /// <summary>
    /// The headquarters keeps behind the middle of the attack, close enough for
    /// its leaders to steady the squads and to see where the guns are needed.
    /// </summary>
    static void FollowAttack(Simulation sim, Team team, Vector2 mass, VictoryFlag? focus)
    {
        if (team.IsMoving || focus == null) return;
        var back = Vector2.Normalize(mass - focus.Position + new Vector2(0, 0.01f));
        var spot = mass + back * 80f;
        if (Vector2.Distance(spot, team.Center) < 60f)
        {
            if (team.Order != TeamOrder.Defend) sim.OrderDefend(team, focus.Position);
            return;
        }
        sim.OrderMove(team, spot, MoveMode.Move, preferCover: true);
    }

    /// <summary>
    /// Mortar teams stay back: they set up behind the attack, within range of
    /// the flag it is going for, and fire on whatever the rest of the force can see.
    /// </summary>
    static void PlaceSupportTeam(Simulation sim, Team team, VictoryFlag? focus)
    {
        if (focus == null || team.IsMoving) return;
        var mortar = team.Soldiers.First(s => s.Weapon.IsIndirect);
        float stand = Math.Clamp(mortar.Weapon.Range * 0.35f, mortar.Weapon.MinRange + 100f, 700f);
        var center = team.Center;
        float d = Vector2.Distance(center, focus.Position);
        if (d > stand * 0.7f && d < stand * 1.3f)
        {
            if (team.Order != TeamOrder.Defend)
                sim.OrderDefend(team, focus.Position);
            return;
        }
        var back = Vector2.Normalize(center - focus.Position + new Vector2(0, 0.01f));
        sim.OrderMove(team, focus.Position + back * stand, MoveMode.Move, preferCover: true);
    }
}
