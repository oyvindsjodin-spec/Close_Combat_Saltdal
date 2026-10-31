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

    /// <summary>Ring colour that shows a soldier's state of mind at a glance.</summary>
    public static Godot.Color MoraleColor(MoraleState m) => m switch
    {
        MoraleState.Ready => new Godot.Color(0.1f, 0.1f, 0.1f),
        MoraleState.Pinned => new Godot.Color(0.95f, 0.85f, 0.2f),
        MoraleState.Cowering => new Godot.Color(1f, 0.55f, 0.1f),
        MoraleState.Panicked => new Godot.Color(0.95f, 0.2f, 0.15f),
        MoraleState.Routed => new Godot.Color(0.85f, 0.2f, 0.85f),
        _ => new Godot.Color(1f, 1f, 1f),
    };

    public static string SideName(Side side) => side == Side.Allied ? "British" : "German";
}
