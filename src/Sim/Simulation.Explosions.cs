using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>A blast on the map, kept briefly so it can be drawn.</summary>
public readonly record struct Blast(Vector2 At, Explosive Explosive, float Time);

/// <summary>A shell or bomb in the air, due to land at a point.</summary>
readonly record struct IncomingRound(Vector2 At, Explosive Explosive, float LandsAt, Side FiredBy);

/// <summary>An aircraft crossing the map on a bombing run, for drawing.</summary>
public readonly record struct Aircraft(Vector2 From, Vector2 To, float Start, float Duration, Side Side)
{
    public Vector2 PositionAt(float time) => Vector2.Lerp(From, To, Math.Clamp((time - Start) / Duration, 0f, 1f));
    public bool IsFlying(float time) => time >= Start && time <= Start + Duration;
}

/// <summary>A line in the battle log, such as "German mortars falling near 2 Section".</summary>
public readonly record struct BattleMessage(float Time, Side? ForSide, string Text);

public sealed partial class Simulation
{
    readonly List<IncomingRound> _incoming = new();
    /// <summary>Where and when shells and bombs now in the air will land, for the sound of them coming.</summary>
    public IEnumerable<(Vector2 At, float LandsAt)> Incoming => _incoming.Select(r => (r.At, r.LandsAt));
    readonly List<Blast> _blasts = new();
    readonly List<Aircraft> _aircraft = new();
    readonly List<BattleMessage> _messages = new();

    public IReadOnlyList<Blast> RecentBlasts => _blasts;
    public IReadOnlyList<Aircraft> AircraftOverhead => _aircraft;
    public IReadOnlyList<BattleMessage> Messages => _messages;

    /// <summary>Off-map guns, mortars and aircraft each side can call on.</summary>
    public List<SupportAsset> Support { get; } = new();

    public void Log(Side? forSide, string text) => _messages.Add(new BattleMessage(Time, forSide, text));

    void UpdateExplosions()
    {
        for (int i = _incoming.Count - 1; i >= 0; i--)
        {
            var r = _incoming[i];
            if (r.LandsAt > Time) continue;
            _incoming.RemoveAt(i);
            Explode(r.At, r.Explosive, r.FiredBy);
        }
        _blasts.RemoveAll(b => Time - b.Time > 3f);
        _aircraft.RemoveAll(a => Time > a.Start + a.Duration);
    }

    /// <summary>Send a round on its way; it lands after the given flight time.</summary>
    internal void FireRoundAt(Vector2 at, Explosive explosive, float flightSeconds, Side firedBy) =>
        _incoming.Add(new IncomingRound(at, explosive, Time + flightSeconds, firedBy));

    /// <summary>
    /// Blow up a charge at a point. Hit chance falls from certain at the kill
    /// radius to nothing at the wound radius; lying down and solid cover help a
    /// lot, forest less so because shells burst in the treetops.
    /// </summary>
    public void Explode(Vector2 at, Explosive e, Side firedBy)
    {
        _blasts.Add(new Blast(at, e, Time));
        TryIgnite(at, e);
        foreach (var s in Soldiers)
        {
            if (!s.IsAlive) continue;
            float d = Vector2.Distance(s.Position, at);
            if (d > e.SuppressionRadius) continue;

            if (s.CanAct)
            {
                float k = 1 - d / e.SuppressionRadius;
                // Friendly rounds frighten too, if less
                if (s.Side == firedBy) k *= 0.5f;
                AddStress(s, e.Suppression * k * (s.Posture == Posture.Prone ? 0.7f : 1f));
                s.LastUnderFire = Time;
            }
            if (d > e.WoundRadius || s.Health >= Health.Incapacitated) continue;

            float p = d <= e.KillRadius ? 0.9f : 0.9f * (1 - (d - e.KillRadius) / (e.WoundRadius - e.KillRadius));
            p *= s.Posture switch
            {
                Posture.Prone => 0.4f,
                Posture.Crouched => 0.75f,
                _ => 1f,
            };
            p *= Map.TerrainAt(s.Position) switch
            {
                TerrainType.Rock => 0.5f,
                TerrainType.Forest => 0.85f,
                TerrainType.Building => 0.6f,
                TerrainType.Trench => 0.35f,
                TerrainType.Ruin => 0.7f,
                _ => 1f,
            };
            if (Rng.Chance(p))
                ApplyHit(s);
        }
    }

    /// <summary>
    /// Call a fire mission from an off-map asset onto a point. Returns false if
    /// the asset has nothing left. The call itself can fail, as radio and field
    /// telephone links often did; that still uses up the mission.
    /// </summary>
    public bool CallSupport(SupportAsset asset, Vector2 target)
    {
        if (asset.MissionsLeft <= 0 || Time < asset.ReadyAt || InPrep) return false;
        asset.MissionsLeft--;
        asset.ReadyAt = Time + asset.Delay + asset.Rounds * asset.Interval + 30f;
        if (!Rng.Chance(asset.Reliability))
        {
            Log(asset.Side, $"{asset.Name}: no answer. The call did not get through.");
            return true;
        }
        Log(asset.Side, $"{asset.Name}: on the way, {asset.Delay:0} seconds.");
        if (asset.IsAircraft)
        {
            // The aircraft dives in from its own side's map edge
            float fromY = asset.Side == Side.German ? Map.WorldHeight + 200 : -200;
            var from = new Vector2(target.X - 300, fromY);
            var to = target + (target - from) * 0.8f;
            float run = 6f;
            _aircraft.Add(new Aircraft(from, to, Time + asset.Delay - run / 1.8f, run, asset.Side));
            Log(Other(asset.Side), "Aircraft overhead!");
        }
        for (int i = 0; i < asset.Rounds; i++)
        {
            float angle = Rng.Range(0, MathF.Tau);
            float dist = asset.Spread * MathF.Sqrt(Rng.NextFloat());
            var at = target + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
            FireRoundAt(at, asset.Explosive, asset.Delay + i * asset.Interval, asset.Side);
        }
        return true;
    }

    public static Side Other(Side side) => side == Side.Allied ? Side.German : Side.Allied;
}

/// <summary>Off-map artillery, mortars or aircraft that a side can call on a few times per battle.</summary>
public sealed class SupportAsset
{
    public string Name { get; }
    public Side Side { get; }
    public Explosive Explosive { get; }
    /// <summary>Rounds or bombs in one mission.</summary>
    public int Rounds { get; }
    /// <summary>Seconds from the call to the first impact.</summary>
    public float Delay { get; }
    /// <summary>Seconds between impacts within a mission.</summary>
    public float Interval { get; }
    /// <summary>How far from the aiming point rounds land, in metres.</summary>
    public float Spread { get; }
    /// <summary>Chance that a call gets through at all.</summary>
    public float Reliability { get; }
    public bool IsAircraft { get; }

    public int MissionsLeft { get; internal set; }
    public float ReadyAt { get; internal set; }

    public SupportAsset(string name, Side side, Explosive explosive, int missions, int rounds,
        float delay, float interval, float spread, float reliability = 1f, bool isAircraft = false)
    {
        Name = name;
        Side = side;
        Explosive = explosive;
        MissionsLeft = missions;
        Rounds = rounds;
        Delay = delay;
        Interval = interval;
        Spread = spread;
        Reliability = reliability;
        IsAircraft = isAircraft;
    }

    public bool IsAvailable(float now) => MissionsLeft > 0 && now >= ReadyAt;
}
