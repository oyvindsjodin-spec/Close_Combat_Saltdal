using System.Linq;
using Godot;
using Saltdal.Sim;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// What the player is dragging out with the right mouse button: an arrow the
/// way the team will face, a dot where each man will stand, and the shape and
/// width beside it.
/// </summary>
public partial class DeployView : Node2D
{
    /// <summary>The team being placed, or null when nothing is being dragged.</summary>
    public Team? Team { get; set; }
    /// <summary>The middle of the formation: where the team goes, or where it stands now if it only turns.</summary>
    public Vec Center { get; set; }
    public Deployment Deploy { get; set; }
    /// <summary>Length of the drag in metres, for the arrow.</summary>
    public float Length { get; set; }
    /// <summary>The team only turns where it stands (the drag began on it).</summary>
    public bool TurnOnly { get; set; }
    public float Zoom { get; set; } = 1f;

    const int FontPx = 14;
    static readonly Color Ink = new(0.95f, 0.95f, 0.8f, 0.95f);
    static readonly Color Shadow = new(0, 0, 0, 0.6f);

    public override void _Draw()
    {
        if (Team == null) return;
        var men = Team.Active.ToList();
        if (men.Count == 0) return;
        float k = 1f / Zoom;
        bool leaderFirst = men.Count > 2 && men[0].IsLeader;
        var offsets = Deploy.Offsets(men.Count, leaderFirst);

        // Where each man will stand
        foreach (var o in offsets)
        {
            var p = View.ToScreen(Center + o);
            DrawCircle(p, 5.5f * k, Shadow);
            DrawCircle(p, 3.8f * k, Ink);
        }

        // The arrow: which way they will look
        var fwd = Deploy.Forward;
        var from = View.ToScreen(Center);
        var to = View.ToScreen(Center + fwd * System.MathF.Max(Length, 8f));
        var dir = (to - from).Normalized();
        var side = new Vector2(-dir.Y, dir.X);
        float head = 10f * k;
        foreach (var (c, w) in new[] { (Shadow, 4f), (Ink, 2f) })
        {
            DrawLine(from, to, c, w * k);
            DrawLine(to, to - dir * head + side * head * 0.6f, c, w * k);
            DrawLine(to, to - dir * head - side * head * 0.6f, c, w * k);
        }

        string shape = Deploy.Shape switch { Formation.Wedge => "Wedge", Formation.Column => "Column", _ => "Line" };
        string text = $"{shape}, {Deploy.Width(men.Count, leaderFirst):0} m wide" + (TurnOnly ? ": turn here" : "");
        const string help = "Drag: direction and width   Wheel: shape   Release: give the order";
        DrawSetTransform(to + new Vector2(12, -8) * k, 0, Vector2.One * k);
        DrawString(ThemeDB.FallbackFont, new Vector2(1, 1), text, HorizontalAlignment.Left, -1, FontPx, new Color(0, 0, 0, 0.85f));
        DrawString(ThemeDB.FallbackFont, Vector2.Zero, text, HorizontalAlignment.Left, -1, FontPx, Ink);
        DrawString(ThemeDB.FallbackFont, new Vector2(1, 19), help, HorizontalAlignment.Left, -1, 12, new Color(0, 0, 0, 0.85f));
        DrawString(ThemeDB.FallbackFont, new Vector2(0, 18), help, HorizontalAlignment.Left, -1, 12, new Color(1, 1, 1, 0.75f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
