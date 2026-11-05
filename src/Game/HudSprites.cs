using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Saltdal.Game;

/// <summary>
/// The painted pictures of the command panel made by tools/paint_hud.py:
/// olive steel, dark wells and signal-pad paper that tile, brass rivets, a
/// figure for each kind of team, the order and button pictures, and a side
/// view of each weapon. Packed into data/sprites/hud.png with an index beside it.
/// </summary>
public sealed class HudSprites
{
    public Texture2D Texture { get; }
    readonly Dictionary<string, Rect2> _regions = new();
    readonly Dictionary<string, Texture2D> _tiles = new();

    static HudSprites? _loaded;
    static bool _tried;

    HudSprites(Texture2D texture, JsonElement meta)
    {
        Texture = texture;
        foreach (var p in meta.GetProperty("sprites").EnumerateObject())
            _regions[p.Name] = new Rect2(p.Value[0].GetInt32(), p.Value[1].GetInt32(), p.Value[2].GetInt32(), p.Value[3].GetInt32());
        // The surfaces are cut out as textures of their own so they can repeat
        var image = texture.GetImage();
        if (image != null)
        {
            if (image.IsCompressed()) image.Decompress();
            foreach (var name in new[] { "steel", "well", "paper" })
                if (_regions.TryGetValue(name, out var r))
                    _tiles[name] = ImageTexture.CreateFromImage(image.GetRegion((Rect2I)r));
        }
    }

    /// <summary>The pictures, loaded once; null if the files are missing.</summary>
    public static HudSprites? Get()
    {
        if (_tried) return _loaded;
        _tried = true;
        const string png = "res://data/sprites/hud.png", json = "res://data/sprites/hud.json";
        if (!ResourceLoader.Exists(png) || !FileAccess.FileExists(json)) return null;
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(json));
        _loaded = new HudSprites(GD.Load<Texture2D>(png), doc.RootElement.Clone());
        return _loaded;
    }

    public Vector2 Size(string name) => _regions.TryGetValue(name, out var r) ? r.Size : Vector2.Zero;

    /// <summary>Draw one picture into a box, tinted.</summary>
    public void Draw(CanvasItem on, string name, Rect2 box, Color tint)
    {
        if (_regions.TryGetValue(name, out var region))
            on.DrawTextureRectRegion(Texture, box, region, tint);
    }

    /// <summary>Draw one picture at its own size, centred in a box, scaled down if it does not fit.</summary>
    public void DrawFit(CanvasItem on, string name, Rect2 box, Color tint, float scale = 1f)
    {
        if (!_regions.TryGetValue(name, out var region)) return;
        var size = region.Size * scale;
        float k = Mathf.Min(1f, Mathf.Min(box.Size.X / size.X, box.Size.Y / size.Y));
        size *= k;
        var at = (box.Position + (box.Size - size) / 2).Round();
        on.DrawTextureRectRegion(Texture, new Rect2(at, size), region, tint);
    }

    /// <summary>Cover a box with a repeating surface: steel, well or paper.</summary>
    public bool Tile(CanvasItem on, string name, Rect2 box, Color? tint = null)
    {
        if (!_tiles.TryGetValue(name, out var tex)) return false;
        on.DrawTextureRect(tex, box, true, tint ?? Colors.White);
        return true;
    }
}
