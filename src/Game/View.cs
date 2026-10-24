using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>Conversions between simulation metres and Godot screen units.</summary>
public static class View
{
    /// <summary>Godot units per metre.</summary>
    public const float Scale = 2f;

    public static Godot.Vector2 ToScreen(Vec p) => new(p.X * Scale, p.Y * Scale);

    public static Vec ToWorld(Godot.Vector2 p) => new(p.X / Scale, p.Y / Scale);

    public static Godot.Color SideColor(Side side) => side switch
    {
        Side.Allied => new Godot.Color(0.80f, 0.62f, 0.30f),  // khaki
        _ => new Godot.Color(0.42f, 0.50f, 0.52f),            // field grey
    };
}
