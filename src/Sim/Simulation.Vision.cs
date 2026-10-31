using System;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

public sealed partial class Simulation
{
    /// <summary>No one spots anything farther than this.</summary>
    public const float MaxSpotRange = 900f;
    /// <summary>Line of sight survives this many metres of forest, below the canopy.</summary>
    public const float MaxForestSight = 40f;
    public const float TreeHeight = 10f;

    public static float EyeHeight(Posture p) => p switch
    {
        Posture.Standing => 1.7f,
        Posture.Crouched => 1.1f,
        _ => 0.35f,
    };

    void UpdateVision()
    {
        var observers = Soldiers.Where(s => s.CanAct && s.Morale < MoraleState.Panicked).ToList();
        foreach (var target in Soldiers)
        {
            if (!target.IsAlive) continue;
            foreach (var obs in observers)
            {
                if (obs.Side == target.Side) continue;
                float d = Vector2.Distance(obs.Position, target.Position);
                if (d > SpotRange(obs, target)) continue;
                if (!LineOfSight(obs.Position, EyeHeight(obs.Posture), target.Position, EyeHeight(target.Posture)))
                    continue;
                target.LastSeenByEnemy = Time;
                target.LastKnownPosition = target.Position;
                break;
            }
        }
    }

    /// <summary>How far away an observer can pick out this target.</summary>
    public float SpotRange(Soldier observer, Soldier target)
    {
        float range = 700f;
        range *= target.Posture switch
        {
            Posture.Prone => 0.35f,
            Posture.Crouched => 0.7f,
            _ => 1f,
        };
        var terrain = Map.TerrainAt(target.Position);
        if (terrain == TerrainType.Forest) range *= 0.35f;
        else if (terrain == TerrainType.Rock) range *= 0.6f;
        if (target.IsMoving && target.Team.Mode == MoveMode.MoveFast) range *= 1.4f;
        if (Time - target.LastFired < 2f) range = MathF.Max(range * 2.5f, 750f); // muzzle flash and noise
        range *= 1f - MathF.Min(observer.Stress, 100f) / 200f;
        return MathF.Min(range, MaxSpotRange);
    }

    /// <summary>
    /// Whether an eye at height ha above point a can see a point hb above b.
    /// Hills block the line; so does more than MaxForestSight of forest where
    /// the line runs below the treetops.
    /// </summary>
    public bool LineOfSight(Vector2 a, float ha, Vector2 b, float hb)
    {
        float dist = Vector2.Distance(a, b);
        if (dist < 1f) return true;
        float za = Map.HeightAt(a) + ha, zb = Map.HeightAt(b) + hb;
        float step = Map.CellSize * 0.5f;
        int n = (int)(dist / step);
        float forest = 0;
        for (int i = 1; i < n; i++)
        {
            float t = i / (float)n;
            var p = Vector2.Lerp(a, b, t);
            float z = za + (zb - za) * t;
            float ground = Map.HeightAt(p);
            if (ground > z)
                return false;
            float along = t * dist;
            bool nearEnds = along < 3f || dist - along < 3f;
            if (!nearEnds && z - ground < TreeHeight && Map.TerrainAt(p) == TerrainType.Forest)
            {
                forest += step;
                if (forest > MaxForestSight)
                    return false;
            }
        }
        return true;
    }
}
