using Godot;
using Saltdal.Sim;

namespace Saltdal.Game;

/// <summary>Draws soldiers as dots, with the selected team's route.</summary>
public partial class UnitsView : Node2D
{
    public Simulation? Sim { get; set; }
    public Team? Selected { get; set; }
    /// <summary>How far between the last tick and the next we are drawing (0 to 1).</summary>
    public float Alpha { get; set; }

    /// <summary>Camera zoom, so dots and labels keep a readable size on screen.</summary>
    public float Zoom { get; set; } = 1f;

    // Sizes in screen pixels
    const float RadiusPx = 4f;
    const int FontPx = 12;

    public override void _Draw()
    {
        if (Sim == null) return;
        var font = ThemeDB.FallbackFont;
        float k = 1f / Zoom;
        float radius = RadiusPx * k;
        foreach (var team in Sim.Teams)
        {
            var color = View.SideColor(team.Side);
            bool selected = team == Selected;
            if (selected)
                DrawRoute(team);
            foreach (var s in team.Soldiers)
            {
                var p = View.ToScreen(System.Numerics.Vector2.Lerp(s.PreviousPosition, s.Position, Alpha));
                DrawCircle(p, radius + 1.5f * k, selected ? Colors.White : new Color(0.1f, 0.1f, 0.1f));
                DrawCircle(p, radius, color);
                var facing = new Vector2(Mathf.Cos(s.Facing), Mathf.Sin(s.Facing));
                DrawLine(p, p + facing * (radius + 3 * k), Colors.Black, 1.2f * k);
            }
            var label = View.ToScreen(team.Center) + new Vector2(-30, -16) * k;
            DrawSetTransform(label, 0, Vector2.One * k);
            DrawString(font, Vector2.Zero, team.Name, HorizontalAlignment.Left, -1, FontPx,
                selected ? Colors.White : new Color(1, 1, 1, 0.75f));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    void DrawRoute(Team team)
    {
        var leader = team.Soldiers[0];
        var from = View.ToScreen(leader.Position);
        foreach (var w in leader.Waypoints)
        {
            var to = View.ToScreen(w);
            DrawLine(from, to, new Color(1, 1, 1, 0.6f), 1.5f / Zoom);
            from = to;
        }
    }
}
