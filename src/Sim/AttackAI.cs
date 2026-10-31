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
    /// <summary>Teams this close to their flag charge it.</summary>
    public float AssaultRange { get; set; } = 180f;

    readonly Dictionary<Team, float> _fightingUntil = new();
    float _lastContact = float.NegativeInfinity;

    public AttackAI(Side side) => Side = side;

    public void Update(Simulation sim)
    {
        var enemies = sim.VisibleEnemies(Side).Where(e => e.CanAct).ToList();
        // The whole force goes for one flag at a time: the open one nearest its centre
        var ours = sim.Soldiers.Where(s => s.Side == Side && s.IsEffective).ToList();
        var mass = ours.Count == 0 ? Vector2.Zero : ours.Aggregate(Vector2.Zero, (a, s) => a + s.Position) / ours.Count;
        var focus = sim.Flags.Where(f => f.Owner != Side)
            .OrderBy(f => Vector2.DistanceSquared(f.Position, mass)).FirstOrDefault();
        foreach (var team in sim.Teams.Where(t => t.Side == Side && !t.IsDestroyed))
        {
            if (team.Morale >= MoraleState.Cowering)
                continue;
            if (_fightingUntil.TryGetValue(team, out var until) && sim.Time < until)
                continue;

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
            // Creep only when the enemy is close or shooting; otherwise walk the bound
            var mode = underFire || enemyDist < 250f ? MoveMode.Sneak : MoveMode.Move;
            float bound = contact && Vector2.Distance(goal, center) > BoundLength * 1.5f ? BoundLength : float.PositiveInfinity;
            sim.OrderMove(team, goal, mode, preferCover: contact, maxDistance: bound);
        }
    }
}
