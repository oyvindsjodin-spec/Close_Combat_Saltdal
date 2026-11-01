using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Saltdal.Game;

/// <summary>
/// The pre-rendered soldier figures made by tools/render_soldiers.py: one
/// picture per uniform, weapon, pose and each of 16 directions, packed into
/// data/sprites/soldiers.png with an index beside it.
/// </summary>
public sealed class SoldierSprites
{
    public Texture2D Texture { get; }
    /// <summary>Pixels each side of one sprite.</summary>
    public int Size { get; }
    public int Directions { get; }
    /// <summary>Sprite pixels per metre.</summary>
    public float PixelsPerMetre { get; }
    /// <summary>Where the man's feet are in the sprite, in pixels from its top left.</summary>
    public Vector2 Anchor { get; }
    readonly Dictionary<string, (int Row, int Col)> _index = new();
    /// <summary>Stands in when the sprite files are missing, so the loader is not asked again every frame.</summary>
    public static readonly SoldierSprites Missing = new();

    SoldierSprites() { Texture = new PlaceholderTexture2D(); }

    SoldierSprites(Texture2D texture, JsonElement meta)
    {
        Texture = texture;
        Size = meta.GetProperty("size").GetInt32();
        Directions = meta.GetProperty("directions").GetInt32();
        float metres = meta.GetProperty("metres").GetSingle();
        PixelsPerMetre = Size / metres;
        float centreW = meta.GetProperty("centreW").GetSingle();
        Anchor = new Vector2(Size / 2f, Size / 2f - centreW * PixelsPerMetre);
        foreach (var p in meta.GetProperty("sprites").EnumerateObject())
            _index[p.Name] = (p.Value[0].GetInt32(), p.Value[1].GetInt32());
    }

    /// <summary>Loads the sprites, or null if they are missing (the game then draws simple figures).</summary>
    public static SoldierSprites? Load(string folder = "res://data/sprites")
    {
        string png = $"{folder}/soldiers.png", json = $"{folder}/soldiers.json";
        if (!ResourceLoader.Exists(png) || !FileAccess.FileExists(json)) return null;
        var tex = GD.Load<Texture2D>(png);
        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(json));
        return new SoldierSprites(tex, doc.RootElement.Clone());
    }

    /// <summary>The part of the texture showing this figure facing this way (radians, 0 = east, clockwise on screen).</summary>
    public Rect2? Region(string uniform, string weapon, string pose, float facing)
    {
        if (!_index.TryGetValue($"{uniform}/{weapon}/{pose}", out var at)) return null;
        int dir = Mathf.PosMod(Mathf.RoundToInt(facing / Mathf.Tau * Directions), Directions);
        return new Rect2((at.Col + dir) * Size, at.Row * Size, Size, Size);
    }
}
