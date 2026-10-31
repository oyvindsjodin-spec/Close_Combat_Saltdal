using System;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

public sealed partial class Simulation
{
    void UpdateCombat()
    {
        foreach (var team in _teams)
            foreach (var s in team.Soldiers)
                UpdateShooter(s);
    }

    void UpdateShooter(Soldier s)
    {
        if (!s.WillFire)
        {
            s.Target = null;
            s.AreaTarget = null;
            s.BurstLeft = 0;
            return;
        }
        s.FireCooldown -= TickSeconds;
        if (s.BurstLeft == 0)
        {
            if (s.FireCooldown > 0) return;
            ChooseTarget(s);
            if (s.Target == null && s.AreaTarget == null) return;
            s.BurstLeft = s.Weapon.Burst;
            s.FireCooldown = 0;
        }
        else if ((s.Target == null && s.AreaTarget == null)
                 || (s.Target != null && (!s.Target.CanAct || s.Target.Morale == MoraleState.Surrendered)))
        {
            // Target went down mid-burst; stop and pick a new one
            s.BurstLeft = 0;
            s.Target = null;
            return;
        }

        while (s.BurstLeft > 0 && s.FireCooldown <= 0 && s.Ammo > 0)
        {
            FireRound(s);
            s.BurstLeft--;
            s.FireCooldown += s.Weapon.RoundInterval;
        }
        if (s.BurstLeft == 0 || s.Ammo == 0)
        {
            s.BurstLeft = 0;
            s.FireCooldown = s.Weapon.AimTime * Rng.Range(0.8f, 1.2f) * (1 + s.Stress / 100f);
        }
    }

    void ChooseTarget(Soldier s)
    {
        s.AreaTarget = null;
        var order = s.Team.Order;
        float range = s.Weapon.Range;
        if (s.IsMoving)
        {
            if (s.Team.Mode == MoveMode.MoveFast || s.Fleeing)
            {
                s.Target = null;
                return;
            }
            range = MathF.Min(range, 150f);
        }
        if (order == TeamOrder.Hide)
            range = MathF.Min(range, 40f);

        float eye = EyeHeight(s.Posture);
        bool CanHit(Soldier e) =>
            Vector2.Distance(e.Position, s.Position) <= range
            && LineOfSight(s.Position, eye, e.Position, EyeHeight(e.Posture));

        var candidates = VisibleEnemies(s.Side)
            .Where(e => e.CanAct)
            .OrderBy(e => Vector2.DistanceSquared(e.Position, s.Position));

        if (order == TeamOrder.Fire)
        {
            var near = candidates.Where(e => Vector2.Distance(e.Position, s.Team.OrderPoint) < 25f).Take(4).FirstOrDefault(CanHit);
            if (near != null)
            {
                s.Target = near;
                return;
            }
            s.Target = null;
            float d = Vector2.Distance(s.Position, s.Team.OrderPoint);
            if (d <= s.Weapon.Range && LineOfSight(s.Position, eye, s.Team.OrderPoint, 0.5f))
                s.AreaTarget = s.Team.OrderPoint;
            return;
        }

        if (s.Target != null && s.Target.CanAct && s.Target.IsVisibleToEnemy(Time) && CanHit(s.Target))
            return;
        s.Target = candidates.Take(5).FirstOrDefault(CanHit);
    }

    void FireRound(Soldier s)
    {
        s.Ammo--;
        s.LastFired = Time;
        var aim = s.Target?.Position ?? s.AreaTarget!.Value;
        float d = Vector2.Distance(s.Position, aim);
        var delta = aim - s.Position;
        if (delta.LengthSquared() > 0.01f) s.Facing = MathF.Atan2(delta.Y, delta.X);

        bool hit = s.Target != null && Rng.Chance(HitChance(s, s.Target, d));
        Vector2 impact;
        if (hit)
            impact = s.Target!.Position;
        else
        {
            float angle = Rng.Range(0, MathF.Tau);
            float spread = Rng.Range(0.5f, 1.5f + d * 0.03f);
            impact = aim + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * spread;
        }
        _shots.Add(new Shot(s.Position, impact, hit, Time, s.Side, s.Id));

        // Rounds cracking past frighten everyone near where they land
        float r = s.Weapon.SuppressionRadius;
        foreach (var e in Soldiers)
        {
            if (e.Side == s.Side || !e.CanAct) continue;
            float dd = Vector2.Distance(e.Position, impact);
            if (dd > r) continue;
            float k = 1 - dd / r;
            if (e.Posture == Posture.Prone) k *= 0.6f;
            AddStress(e, s.Weapon.Suppression * k);
            e.LastUnderFire = Time;
        }
        if (s.Target != null)
        {
            AddStress(s.Target, 1f);
            s.Target.LastUnderFire = Time;
        }
        if (hit)
            ApplyHit(s.Target!);
    }

    /// <summary>Chance that one round from a shooter hits a target at a distance.</summary>
    public float HitChance(Soldier shooter, Soldier target, float distance)
    {
        float p = shooter.Weapon.Accuracy * MathF.Pow(100f / MathF.Max(distance, 20f), 1.6f);
        p *= target.Posture switch
        {
            Posture.Prone => 0.35f,
            Posture.Crouched => 0.7f,
            _ => 1f,
        };
        p *= Map.TerrainAt(target.Position) switch
        {
            TerrainType.Forest => 0.5f,
            TerrainType.Rock => 0.45f,
            _ => 1f,
        };
        if (target.IsMoving) p *= 0.7f;
        if (shooter.IsMoving) p *= shooter.Team.Mode == MoveMode.Sneak ? 0.6f : 0.4f;
        if (shooter.Posture == Posture.Prone) p *= 1.15f;
        if (shooter.Health == Health.Wounded) p *= 0.7f;
        p *= 1 - MathF.Min(shooter.Stress, 75f) / 110f;
        p *= 0.75f + 0.5f * shooter.Experience;
        return Math.Clamp(p, 0f, 0.9f);
    }

    void ApplyHit(Soldier t)
    {
        float roll = Rng.NextFloat();
        var before = t.Health;
        if (t.Health == Health.Wounded)
            t.Health = roll < 0.5f ? Health.Dead : Health.Incapacitated;
        else
            t.Health = roll < 0.35f ? Health.Dead : roll < 0.65f ? Health.Incapacitated : Health.Wounded;

        if (t.Health == Health.Wounded)
        {
            AddStress(t, 20f);
            return;
        }
        if (before <= Health.Wounded)
        {
            t.Path.Clear();
            t.Fleeing = false;
            t.Target = null;
            t.BurstLeft = 0;
            OnCasualty(t);
        }
    }
}
