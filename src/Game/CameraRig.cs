using Godot;

namespace Saltdal.Game;

/// <summary>Pan with WASD, arrow keys or middle mouse drag; zoom with the mouse wheel.</summary>
public partial class CameraRig : Camera2D
{
    const float PanSpeed = 900f;
    const float MinZoom = 0.25f, MaxZoom = 4f;

    public Rect2 Bounds { get; set; }

    bool _dragging;

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (dir != Vector2.Zero)
            Position += dir.Normalized() * PanSpeed * (float)delta / Zoom.X;
        Position = ClampToBounds(Position);
    }

    /// <summary>Keep the view inside the map, so no empty space shows at the edges.</summary>
    Vector2 ClampToBounds(Vector2 p)
    {
        var half = GetViewportRect().Size / 2 / Zoom;
        var lo = Bounds.Position + half;
        var hi = Bounds.End - half;
        return new Vector2(
            lo.X > hi.X ? Bounds.GetCenter().X : Mathf.Clamp(p.X, lo.X, hi.X),
            lo.Y > hi.Y ? Bounds.GetCenter().Y : Mathf.Clamp(p.Y, lo.Y, hi.Y));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
                _dragging = mb.Pressed;
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
                SetZoomAround(Zoom.X * 1.15f);
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
                SetZoomAround(Zoom.X / 1.15f);
        }
        else if (e is InputEventMouseMotion mm && _dragging)
        {
            Position -= mm.Relative / Zoom.X;
        }
    }

    void SetZoomAround(float z)
    {
        // Keep the point under the mouse still while zooming
        var before = GetGlobalMousePosition();
        Zoom = Vector2.One * Mathf.Clamp(z, MinZoom, MaxZoom);
        ForceUpdateScroll();
        Position += before - GetGlobalMousePosition();
    }
}
