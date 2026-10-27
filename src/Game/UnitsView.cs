using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// Draws flags, soldiers, tracers and the selected team's route. Enemy soldiers
/// are shown only while the player's men can see them (fog of war).
/// </summary>
public partial class UnitsView : Node2D
{
    public Simulation? Sim { get; set; }
    public Side PlayerSide { get; set; } = Side.Allied;
    public Team? Selected { get; set; }
    /// <summary>Show every soldier, for testing.</summary>
    public bool RevealAll { get; set; }
    /// <summary>How far between the last tick and the next we are drawing (0 to 1).</summary>
    public float Alpha { get; set; }
    /// <summary>Camera zoom, so dots and labels keep a readable size on screen.</summary>
    public float Zoom { get; set; } = 1f;

    // Sizes in screen pixels
    const float RadiusPx = 4f;
    const int FontPx = 12;
    const float LastKnownSeconds = 20f;

    public override void _Draw()
    {
        if (Sim == null) return;
        float k = 1f / Zoom;
        foreach (var flag in Sim.Flags)
            DrawFlag(flag, k);
        foreach (var team in Sim.Teams)
        {
            if (team == Selected)
                DrawRoute(team);
            foreach (var s in team.Soldiers)
                DrawSoldier(s, team == Selected, k);
        }
        DrawShots(k);
        foreach (var team in Sim.Teams)
            if (team.Side == PlayerSide || RevealAll)
                DrawLabel(View.ToScreen(team.Center) + new Vector2(-30, -16) * k, team.Name,
                    team == Selected ? Colors.White : new Color(1, 1, 1, 0.75f), k);
    }

    void DrawSoldier(Soldier s, bool selected, float k)
    {
        float radius = RadiusPx * k;
        var p = View.ToScreen(Vec.Lerp(s.PreviousPosition, s.Position, Alpha));
        bool friendly = s.Side == PlayerSide || RevealAll;
        if (!friendly && !s.IsVisibleToEnemy(Sim!.Time))
        {
            // Out of sight: a faint marker where he was last seen, for a while
            if (s.IsAlive && Sim.Time - s.LastSeenByEnemy < LastKnownSeconds)
                DrawArc(View.ToScreen(s.LastKnownPosition), radius, 0, Mathf.Tau, 12,
                    new Color(View.SideColor(s.Side), 0.5f), 1.2f * k);
            return;
        }

        var color = View.SideColor(s.Side);
        if (s.Health == Health.Dead)
        {
            var d = new Vector2(radius, radius) * 0.8f;
            var dark = new Color(0.15f, 0.12f, 0.1f, 0.8f);
            DrawLine(p - d, p + d, dark, 1.5f * k);
            DrawLine(p + new Vector2(-d.X, d.Y), p + new Vector2(d.X, -d.Y), dark, 1.5f * k);
            return;
        }
        if (s.Health == Health.Incapacitated)
        {
            DrawCircle(p, radius * 0.8f, color.Darkened(0.5f));
            return;
        }

        var ring = s.Morale == MoraleState.Surrendered ? Colors.White : View.MoraleColor(s.Morale);
        if (selected)
            DrawCircle(p, radius + 3f * k, Colors.White);
        DrawCircle(p, radius + 1.5f * k, ring);
        DrawCircle(p, s.Posture == Posture.Prone ? radius * 0.8f : radius, color);
        var facing = new Vector2(Mathf.Cos(s.Facing), Mathf.Sin(s.Facing));
        DrawLine(p, p + facing * (radius + 3 * k), Colors.Black, 1.2f * k);
        if (s.Health == Health.Wounded)
            DrawCircle(p, radius * 0.35f, new Color(0.7f, 0.05f, 0.05f));
    }

    void DrawShots(float k)
    {
        foreach (var shot in Sim!.RecentShots)
        {
            // Hide the shooter's end of an unseen enemy's tracer; the impact still shows
            var from = View.ToScreen(shot.From);
            var to = View.ToScreen(shot.To);
            if (shot.Side != PlayerSide && !RevealAll)
            {
                var shooter = FindSoldier(shot.ShooterId);
                if (shooter != null && !shooter.IsVisibleToEnemy(Sim.Time))
                    from = to + (from - to).Normalized() * 40f * k;
            }
            float age = Sim.Time - shot.Time;
            float fade = Mathf.Clamp(1f - age / 0.6f, 0f, 1f);
            var color = shot.Side == Side.Allied ? new Color(1f, 0.9f, 0.5f, fade) : new Color(1f, 0.6f, 0.4f, fade);
            DrawLine(from, to, color, 1f * k);
        }
    }

    Soldier? FindSoldier(int id)
    {
        foreach (var team in Sim!.Teams)
            foreach (var s in team.Soldiers)
                if (s.Id == id) return s;
        return null;
    }

    void DrawFlag(VictoryFlag flag, float k)
    {
        var p = View.ToScreen(flag.Position);
        var color = flag.Owner == null ? new Color(0.8f, 0.8f, 0.8f) : View.SideColor(flag.Owner.Value);
        DrawCircle(p, flag.Radius * View.Scale, new Color(color, 0.12f));
        DrawArc(p, flag.Radius * View.Scale, 0, Mathf.Tau, 48, new Color(color, 0.7f), 1.5f * k);
        // A little pennant on a pole
        var top = p + new Vector2(0, -22) * k;
        DrawLine(p, top, Colors.Black, 2f * k);
        DrawColoredPolygon(new[] { top, top + new Vector2(14, 4) * k, top + new Vector2(0, 8) * k }, color);
        string owner = flag.Owner == null ? "" : $" ({View.SideName(flag.Owner.Value)})";
        DrawLabel(p + new Vector2(8, -24) * k, flag.Name + owner, Colors.White, k);
    }

    void DrawLabel(Vector2 at, string text, Color color, float k)
    {
        DrawSetTransform(at, 0, Vector2.One * k);
        DrawString(ThemeDB.FallbackFont, new Vector2(1, 1), text, HorizontalAlignment.Left, -1, FontPx, new Color(0, 0, 0, color.A * 0.8f));
        DrawString(ThemeDB.FallbackFont, Vector2.Zero, text, HorizontalAlignment.Left, -1, FontPx, color);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawRoute(Team team)
    {
        Soldier? guide = null;
        foreach (var s in team.Soldiers)
            if (s.CanAct && s.IsMoving) { guide = s; break; }
        if (guide == null)
        {
            if (team.Order == TeamOrder.Fire)
                DrawLine(View.ToScreen(team.Center), View.ToScreen(team.OrderPoint), new Color(1, 0.3f, 0.2f, 0.6f), 1.5f / Zoom);
            return;
        }
        var from = View.ToScreen(guide.Position);
        foreach (var w in guide.Waypoints)
        {
            var to = View.ToScreen(w);
            DrawLine(from, to, new Color(1, 1, 1, 0.6f), 1.5f / Zoom);
            from = to;
        }
    }
}
