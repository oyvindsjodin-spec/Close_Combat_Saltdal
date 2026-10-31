using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>
/// How a computer commander uses its guns and aircraft: on the thickest group
/// of enemies in sight, as long as none of its own men are close to it, and
/// saving aircraft for a big target.
/// </summary>
public sealed class SupportPlanner
{
    float _nextCall;
    public float Cooldown { get; set; } = 90f;
    public int MinimumGroup { get; set; } = 3;

    public SupportPlanner(float firstCall) => _nextCall = firstCall;

    public void Update(Simulation sim, Side side, List<Soldier> enemies)
    {
        if (sim.Time < _nextCall || enemies.Count == 0) return;
        var assets = sim.Support.Where(a => a.Side == side && a.IsAvailable(sim.Time)).ToList();
        if (assets.Count == 0) return;

        Soldier? best = null;
        int bestCount = 0;
        foreach (var e in enemies)
        {
            int count = enemies.Count(o => Vector2.DistanceSquared(o.Position, e.Position) < 20 * 20);
            if (count > bestCount)
            {
                bestCount = count;
                best = e;
            }
        }
        if (best == null || bestCount < MinimumGroup) return;

        var asset = bestCount >= MinimumGroup + 1 ? assets.FirstOrDefault(a => a.IsAircraft) : null;
        asset ??= assets.FirstOrDefault(a => !a.IsAircraft) ?? assets[0];
        float danger = asset.Explosive.WoundRadius * 2 + asset.Spread;
        bool friendsClose = sim.Soldiers.Any(s => s.Side == side && s.IsEffective
                                                  && Vector2.Distance(s.Position, best.Position) < danger);
        if (friendsClose) return;

        sim.CallSupport(asset, best.Position);
        _nextCall = sim.Time + Cooldown;
    }
}
