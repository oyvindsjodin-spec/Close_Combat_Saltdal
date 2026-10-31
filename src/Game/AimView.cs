using System.Linq;
using Godot;
using Saltdal.Sim;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// The aiming line shown while the player picks a target for a Fire order:
/// from the team to the cursor, pale where the men can see the ground and red
/// beyond the point where something blocks the view, with the range and how
/// many of the men can see the spot.
/// </summary>
public partial class AimView : Node2D
{
    public Simulation? Sim { get; set; }
    /// <summary>The team taking aim, or null when not aiming.</summary>
    public Team? Team { get; set; }
    public Vec Target { get; set; }
    public float Zoom { get; set; } = 1f;

    /// <summary>Height above the ground of what is aimed at: a man lying or crouching.</summary>
    const float TargetHeight = 1.0f;
    const int FontPx = 14;

    static readonly Color Clear = new(0.95f, 0.95f, 0.85f, 0.9f);
    static readonly Color Blocked = new(0.95f, 0.25f, 0.2f, 0.9f);
    static readonly Color Lobbed = new(1f, 0.8f, 0.3f, 0.9f);

    public override void _Draw()
    {
        if (Sim == null || Team == null) return;
        var men = Team.Active.ToList();
        if (men.Count == 0) return;
        float k = 1f / Zoom;
        var rifles = men.Where(s => !s.Weapon.IsIndirect).ToList();
        var mortars = men.Where(s => s.Weapon.IsIndirect).ToList();
        var end = View.ToScreen(Target);
        string text;
        Color textColor = Clear;

        if (rifles.Count == 0 && mortars.Count > 0)
        {
            // Mortars lob their bombs: no need to see, but the range must be right
            var m = mortars.OrderBy(s => Vec.DistanceSquared(s.Position, Target)).First();
            float d = Vec.Distance(m.Position, Target);
            DrawDashed(View.ToScreen(m.Position), end, Lobbed, k);
            bool ok = d >= m.Weapon.MinRange && d <= m.Weapon.Range;
            text = $"{d:0} m   " + (d < m.Weapon.MinRange ? $"too close (at least {m.Weapon.MinRange:0} m)"
                : d > m.Weapon.Range ? $"out of range (at most {m.Weapon.Range:0} m)" : "barrage");
            textColor = ok ? Lobbed : Blocked;
        }
        else
        {
            // The line runs from the man nearest the target
            var shooter = rifles.OrderBy(s => Vec.DistanceSquared(s.Position, Target)).First();
            var from = shooter.Position;
            float eye = Simulation.EyeHeight(shooter.Posture);
            float dist = Vec.Distance(from, Target);
            // Walk along the line to where the view is first blocked
            float seenTo = dist;
            const float step = 3f;
            for (float t = step; t < dist; t += step)
            {
                var p = Vec.Lerp(from, Target, t / dist);
                if (!Sim.LineOfSight(from, eye, p, TargetHeight))
                {
                    seenTo = t - step;
                    break;
                }
            }
            if (seenTo >= dist && !Sim.LineOfSight(from, eye, Target, TargetHeight)) seenTo = dist - step;
            var start = View.ToScreen(from);
            var mid = View.ToScreen(Vec.Lerp(from, Target, System.Math.Clamp(seenTo / dist, 0, 1)));
            DrawLine(start, mid, new Color(0, 0, 0, 0.5f), 3.5f * k);
            DrawLine(start, mid, Clear, 2f * k);
            if (seenTo < dist)
            {
                DrawLine(mid, end, new Color(0, 0, 0, 0.5f), 3.5f * k);
                DrawLine(mid, end, Blocked, 2f * k);
                DrawCircle(mid, 3f * k, Blocked);
            }

            int see = rifles.Count(s => Sim.LineOfSight(s.Position, Simulation.EyeHeight(s.Posture), Target, TargetHeight));
            float range = rifles.Max(s => s.Weapon.Range);
            float rangeFrom = Vec.Distance(Team.Center, Target);
            text = $"{rangeFrom:0} m   ";
            if (rangeFrom > range)
            {
                text += "out of range";
                textColor = Blocked;
            }
            else if (see == 0)
            {
                text += "no one can see it: firing blind";
                textColor = Blocked;
            }
            else text += $"{see} of {rifles.Count} can see it";
            if (mortars.Count > 0) text += ", mortar barrage";
        }

        // A ring at the aim point, and the words beside it
        DrawArc(end, 7f * k, 0, Mathf.Tau, 20, new Color(0, 0, 0, 0.6f), 3.5f * k);
        DrawArc(end, 7f * k, 0, Mathf.Tau, 20, textColor, 1.8f * k);
        DrawLine(end - new Vector2(11, 0) * k, end - new Vector2(4, 0) * k, textColor, 1.5f * k);
        DrawLine(end + new Vector2(4, 0) * k, end + new Vector2(11, 0) * k, textColor, 1.5f * k);
        DrawLine(end - new Vector2(0, 11) * k, end - new Vector2(0, 4) * k, textColor, 1.5f * k);
        DrawLine(end + new Vector2(0, 4) * k, end + new Vector2(0, 11) * k, textColor, 1.5f * k);
        DrawSetTransform(end + new Vector2(14, -10) * k, 0, Vector2.One * k);
        DrawString(ThemeDB.FallbackFont, new Vector2(1, 1), text, HorizontalAlignment.Left, -1, FontPx, new Color(0, 0, 0, 0.85f));
        DrawString(ThemeDB.FallbackFont, Vector2.Zero, text, HorizontalAlignment.Left, -1, FontPx, textColor);
        DrawString(ThemeDB.FallbackFont, new Vector2(1, 19), "Left-click: fire   Right-click or Esc: cancel", HorizontalAlignment.Left, -1, 12, new Color(0, 0, 0, 0.85f));
        DrawString(ThemeDB.FallbackFont, new Vector2(0, 18), "Left-click: fire   Right-click or Esc: cancel", HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.75f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    void DrawDashed(Vector2 a, Vector2 b, Color color, float k)
    {
        float len = a.DistanceTo(b);
        var dir = (b - a) / Mathf.Max(len, 0.01f);
        float dash = 8f * k;
        for (float t = 0; t < len; t += dash * 2)
            DrawLine(a + dir * t, a + dir * Mathf.Min(t + dash, len), color, 2f * k);
    }
}
