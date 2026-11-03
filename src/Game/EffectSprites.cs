using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Saltdal.Game;

/// <summary>
/// The painted effect pictures made by tools/render_effects.py: smoke puffs,
/// fireballs, flame tongues, sprays of earth, muzzle flashes and a soft glow,
/// packed into data/sprites/effects.png with an index beside it.
/// </summary>
public sealed class EffectSprites
{
    public Texture2D Texture { get; }
    public int Cell { get; }
    readonly Dictionary<string, Rect2> _regions = new();

    static EffectSprites? _loaded;
    static bool _tried;

    EffectSprites(Texture2D texture, JsonElement meta)
    {
        Texture = texture;
        Cell = meta.GetProperty("cell").GetInt32();
        foreach (var p in meta.GetProperty("sprites").EnumerateObject())
            _regions[p.Name] = new Rect2(p.Value[1].GetInt32() * Cell, p.Value[0].GetInt32() * Cell, Cell, Cell);
    }

    /// <summary>The pictures, loaded once; null if the files are missing.</summary>
    public static EffectSprites? Get()
    {
        if (_tried) return _loaded;
        _tried = true;
        const string png = "res://data/sprites/effects.png", json = "res://data/sprites/effects.json";
        if (!ResourceLoader.Exists(png) || !FileAccess.FileExists(json)) return null;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(json));
        _loaded = new EffectSprites(GD.Load<Texture2D>(png), doc.RootElement.Clone());
        return _loaded;
    }

    public bool Has(string name) => _regions.ContainsKey(name);

    /// <summary>
    /// Draw one picture centred on a point, the given size across, turned by
    /// an angle (radians) and tinted.
    /// </summary>
    public void Draw(CanvasItem on, string name, Vector2 at, float size, float angle, Color tint)
    {
        if (size <= 0.01f || tint.A <= 0.004f || !_regions.TryGetValue(name, out var region)) return;
        on.DrawSetTransform(at, angle, Vector2.One);
        on.DrawTextureRectRegion(Texture, new Rect2(-size / 2, -size / 2, size, size), region, tint);
        on.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
