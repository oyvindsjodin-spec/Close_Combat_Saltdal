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
        DrawBlasts(k);
        DrawAircraft(k);
        foreach (var team in Sim.Teams)
            if (team.Side == PlayerSide || RevealAll)
                DrawLabel(View.ToScreen(team.Center) + new Vector2(-30, -26) * k, team.Name,
                    team == Selected ? Colors.White : new Color(1, 1, 1, 0.75f), k);
    }

    /// <summary>
    /// A soldier seen from above, as a small figure: helmet, shoulders and pack,
    /// and his weapon pointing where he faces. Drawn larger than life so a man
    /// can be picked out at a glance.
    /// </summary>
    void DrawSoldier(Soldier s, bool selected, float k)
    {
        var p = View.ToScreen(Vec.Lerp(s.PreviousPosition, s.Position, Alpha));
        bool friendly = s.Side == PlayerSide || RevealAll;
        if (!friendly && !s.IsVisibleToEnemy(Sim!.Time))
        {
            // Out of sight: a faint marker where he was last seen, for a while
            if (s.IsAlive && Sim.Time - s.LastSeenByEnemy < LastKnownSeconds)
                DrawArc(View.ToScreen(s.LastKnownPosition), RadiusPx * k, 0, Mathf.Tau, 12,
                    new Color(View.SideColor(s.Side), 0.5f), 1.2f * k);
            return;
        }

        // Figures grow a little when zoomed in, but stay readable when zoomed out
        float f = k * Mathf.Clamp(Mathf.Sqrt(Zoom), 1.0f, 2.2f);
        var look = Uniform.For(s);
        bool down = s.Health >= Health.Incapacitated;
        bool prone = down || s.Posture == Posture.Prone;

        // Shadow falls to the south-east, away from the light
        DrawSetTransform(p + new Vector2(1.6f, 1.6f) * f, s.Facing, Vector2.One * f);
        if (prone) DrawEllipse(new Vector2(-2, 0), 7.5f, 3.2f, new Color(0, 0, 0, 0.28f));
        else DrawEllipse(Vector2.Zero, 3.6f, 5.2f, new Color(0, 0, 0, 0.28f));

        DrawSetTransform(p, s.Facing, Vector2.One * f);
        if (selected && !down)
            DrawArc(prone ? new Vector2(-2, 0) : Vector2.Zero, prone ? 8.5f : 6.5f, 0, Mathf.Tau, 20, new Color(1, 1, 1, 0.85f), 1.0f);

        if (s.Health == Health.Dead)
        {
            var dead = look.Dimmed(0.45f);
            DrawProne(dead, s, false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        if (s.Health == Health.Incapacitated)
        {
            DrawProne(look.Dimmed(0.25f), s, false);
            DrawCircle(new Vector2(-1, 0.5f), 1.1f, new Color(0.6f, 0.05f, 0.05f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }

        if (prone) DrawProne(look, s, true);
        else DrawUpright(look, s, s.Posture == Posture.Crouched);

        // Muzzle flash for a moment after each shot
        if (Sim!.Time - s.LastShotTime < 0.12f && !s.Weapon.IsIndirect)
        {
            var tip = prone ? new Vector2(7.5f, 0.6f) : new Vector2(7.2f, 1.6f);
            DrawCircle(tip + new Vector2(1.2f, 0), 1.4f, new Color(1f, 0.85f, 0.4f, 0.95f));
        }
        if (s.Health == Health.Wounded)
            DrawCircle(prone ? new Vector2(-1, 0) : new Vector2(-1.2f, -1.5f), 0.9f, new Color(0.7f, 0.05f, 0.05f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // A small coloured pip over a man who is shaken, so trouble is easy to spot
        var mood = s.Morale == MoraleState.Surrendered ? Colors.White : View.MoraleColor(s.Morale);
        if (s.Morale != MoraleState.Ready)
            DrawCircle(p + new Vector2(0, -8f) * f, 1.8f * f, mood);
    }

    /// <summary>Standing or crouching: shoulders across, pack behind, helmet on top.</summary>
    void DrawUpright(Uniform u, Soldier s, bool crouched)
    {
        float w = crouched ? 4.2f : 4.8f;    // half the shoulder width
        DrawEllipse(new Vector2(-0.3f, 0), 2.6f, w, u.Cloth.Darkened(0.35f));
        DrawEllipse(new Vector2(-0.1f, 0), 2.2f, w - 0.6f, u.Cloth);
        DrawRect(new Rect2(-3.4f, -2.2f, 1.8f, 4.4f), u.Kit);
        DrawWeapon(s, upright: true);
        DrawHelmet(u, new Vector2(0.3f, 0));
    }

    /// <summary>Lying down: the body stretched out behind the head, legs apart.</summary>
    void DrawProne(Uniform u, Soldier s, bool withWeapon)
    {
        var legs = u.Cloth.Darkened(0.2f);
        DrawLine(new Vector2(-5, -1.2f), new Vector2(-10.5f, -2.6f), legs, 1.8f);
        DrawLine(new Vector2(-5, 1.2f), new Vector2(-10.5f, 2.4f), legs, 1.8f);
        DrawEllipse(new Vector2(-2, 0), 4.8f, 2.9f, u.Cloth.Darkened(0.3f));
        DrawEllipse(new Vector2(-1.8f, 0), 4.3f, 2.4f, u.Cloth);
        DrawRect(new Rect2(-4.4f, -1.5f, 2.6f, 3f), u.Kit);
        if (withWeapon) DrawWeapon(s, upright: false);
        DrawHelmet(u, new Vector2(3.2f, 0));
    }

    void DrawWeapon(Soldier s, bool upright)
    {
        var w = s.Weapon;
        var metal = new Color(0.12f, 0.12f, 0.11f);
        var wood = new Color(0.36f, 0.24f, 0.14f);
        var from = upright ? new Vector2(0.5f, 2.6f) : new Vector2(2f, 0.9f);
        if (w.IsIndirect)
        {
            // The mortar tube set up beside him, seen end-on
            var at = upright ? new Vector2(5.5f, 3.5f) : new Vector2(6f, 2.8f);
            DrawCircle(at, 2.1f, metal);
            DrawCircle(at, 1.2f, new Color(0.3f, 0.3f, 0.28f));
            DrawLine(at, at + new Vector2(-2.5f, -2.5f), metal, 0.7f);
            DrawLine(at, at + new Vector2(-2.5f, 2.5f), metal, 0.7f);
            return;
        }
        bool mg = w.Burst >= 5 && w.Range >= 800;
        bool smg = w.Range <= 200;
        float length = mg ? 9.5f : smg ? 5f : 8f;
        var to = from + new Vector2(length, upright ? -1.2f : -0.3f);
        DrawLine(from, from + (to - from) * 0.45f, smg ? metal : wood, mg ? 1.6f : 1.2f);
        DrawLine(from + (to - from) * 0.45f, to, metal, mg ? 1.4f : 0.9f);
        if (mg)
        {
            // Bipod legs and the magazine on top (Bren) or the belt (MG 34)
            DrawLine(to - new Vector2(1.5f, 0), to + new Vector2(-0.5f, 1.6f), metal, 0.6f);
            DrawLine(to - new Vector2(1.5f, 0), to + new Vector2(-0.5f, -1.6f), metal, 0.6f);
            DrawRect(new Rect2(from + (to - from) * 0.35f - new Vector2(0.6f, 1.6f), new Vector2(1.4f, 1.4f)), metal);
        }
    }

    void DrawHelmet(Uniform u, Vector2 at)
    {
        if (u.WideBrim)
        {
            // The British Brodie: a flat, wide brim around a shallow bowl
            DrawCircle(at, 2.9f, u.Helmet.Darkened(0.25f));
            DrawCircle(at, 2.5f, u.Helmet);
            DrawCircle(at + new Vector2(0.3f, -0.4f), 1.5f, u.Helmet.Lightened(0.15f));
        }
        else
        {
            // The German Stahlhelm (or Norwegian helmet): a deep bowl flared at the back
            DrawEllipse(at + new Vector2(-0.4f, 0), 2.7f, 2.5f, u.Helmet.Darkened(0.3f));
            DrawCircle(at, 2.2f, u.Helmet);
            DrawCircle(at + new Vector2(0.4f, -0.5f), 1.2f, u.Helmet.Lightened(0.18f));
        }
    }

    void DrawEllipse(Vector2 c, float rx, float ry, Color color)
    {
        const int n = 14;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, color);
    }

    /// <summary>Colours of a soldier's uniform, kit and helmet.</summary>
    readonly record struct Uniform(Color Cloth, Color Kit, Color Helmet, bool WideBrim)
    {
        static readonly Uniform British = new(new Color(0.56f, 0.46f, 0.29f), new Color(0.66f, 0.62f, 0.45f), new Color(0.40f, 0.41f, 0.27f), true);
        static readonly Uniform German = new(new Color(0.43f, 0.47f, 0.41f), new Color(0.32f, 0.28f, 0.22f), new Color(0.31f, 0.35f, 0.31f), false);
        static readonly Uniform Norwegian = new(new Color(0.40f, 0.44f, 0.36f), new Color(0.50f, 0.44f, 0.32f), new Color(0.36f, 0.39f, 0.32f), false);

        public static Uniform For(Soldier s) =>
            s.Side == Side.German ? German
            : s.Weapon == Weapon.Krag || s.Weapon == Weapon.Mortar81Norwegian ? Norwegian
            : British;

        public Uniform Dimmed(float amount) => new(Cloth.Darkened(amount), Kit.Darkened(amount), Helmet.Darkened(amount), WideBrim);
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

    void DrawBlasts(float k)
    {
        foreach (var b in Sim!.RecentBlasts)
        {
            float age = Sim.Time - b.Time;
            var p = View.ToScreen(b.At);
            float size = b.Explosive.KillRadius * View.Scale;
            if (age < 0.4f)
            {
                // The flash, growing fast
                float t = age / 0.4f;
                DrawCircle(p, size * (0.6f + 1.2f * t), new Color(1f, 0.75f, 0.3f, 0.9f * (1 - t)));
                DrawCircle(p, size * 0.5f, new Color(1f, 1f, 0.8f, 1 - t));
            }
            // Smoke that drifts and fades over a few seconds
            float fade = Mathf.Clamp(1f - age / 3f, 0f, 1f);
            DrawCircle(p + new Vector2(age * 4f, -age * 2f), size * (1f + age * 0.6f), new Color(0.35f, 0.33f, 0.3f, 0.45f * fade));
        }
    }

    void DrawAircraft(float k)
    {
        foreach (var a in Sim!.AircraftOverhead)
        {
            if (!a.IsFlying(Sim.Time)) continue;
            var p = View.ToScreen(a.PositionAt(Sim.Time));
            var dir = (View.ToScreen(a.To) - View.ToScreen(a.From)).Normalized();
            var side = new Vector2(-dir.Y, dir.X);
            float s = 14f * k;
            var dark = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            // A gull-winged silhouette: fuselage, wings, tail
            DrawLine(p - dir * s, p + dir * s, dark, 3f * k);
            DrawPolyline(new[] { p + side * s * 1.2f + dir * s * 0.1f, p + side * s * 0.5f + dir * s * 0.3f, p, p - side * s * 0.5f + dir * s * 0.3f, p - side * s * 1.2f + dir * s * 0.1f }, dark, 3f * k);
            DrawLine(p - dir * s + side * s * 0.4f, p - dir * s - side * s * 0.4f, dark, 2f * k);
            DrawCircle(p + new Vector2(10, 12) * k, s * 0.6f, new Color(0, 0, 0, 0.15f));
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
