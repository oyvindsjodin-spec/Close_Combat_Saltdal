using System;
using Godot;
using Saltdal.Sim;

namespace Saltdal.Game;

/// <summary>Draws the terrain as one shaded image. Placeholder art until Phase 4.</summary>
public partial class MapView : Sprite2D
{
    static Color TerrainColor(TerrainType t) => t switch
    {
        TerrainType.Grass => new Color(0.60f, 0.64f, 0.42f),
        TerrainType.Forest => new Color(0.30f, 0.42f, 0.26f),
        TerrainType.Rock => new Color(0.52f, 0.50f, 0.46f),
        TerrainType.Snow => new Color(0.92f, 0.93f, 0.95f),
        TerrainType.Water => new Color(0.28f, 0.44f, 0.60f),
        TerrainType.Road => new Color(0.72f, 0.64f, 0.48f),
        TerrainType.Bridge => new Color(0.38f, 0.28f, 0.20f),
        TerrainType.Field => new Color(0.70f, 0.68f, 0.44f),
        TerrainType.Building => new Color(0.45f, 0.24f, 0.20f),
        TerrainType.Ford => new Color(0.42f, 0.56f, 0.66f),
        TerrainType.Wreck => new Color(0.20f, 0.18f, 0.17f),
        _ => Colors.Magenta,
    };

    public void Build(MapData map)
    {
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgb8);
        // Light from the north-west, like a printed relief map
        var light = new System.Numerics.Vector3(-1, -1, 1.5f);
        light = System.Numerics.Vector3.Normalize(light);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                float hx = map.HeightAt(Math.Min(x + 1, map.Width - 1), y) - map.HeightAt(Math.Max(x - 1, 0), y);
                float hy = map.HeightAt(x, Math.Min(y + 1, map.Height - 1)) - map.HeightAt(x, Math.Max(y - 1, 0));
                var normal = System.Numerics.Vector3.Normalize(new(-hx, -hy, 2 * map.CellSize));
                float shade = Math.Clamp(System.Numerics.Vector3.Dot(normal, light), 0, 1);
                var c = TerrainColor(map.TerrainAt(x, y));
                float k = 0.55f + 0.6f * shade;
                // Faint contour every 10 m
                float h = map.HeightAt(x, y);
                bool contour = (int)(h / 10) != (int)(map.HeightAt(Math.Min(x + 1, map.Width - 1), y) / 10)
                            || (int)(h / 10) != (int)(map.HeightAt(x, Math.Min(y + 1, map.Height - 1)) / 10);
                if (contour) k *= 0.85f;
                image.SetPixel(x, y, new Color(c.R * k, c.G * k, c.B * k));
            }
        }
        Texture = ImageTexture.CreateFromImage(image);
        Centered = false;
        Scale = Vector2.One * map.CellSize * View.Scale;
        TextureFilter = TextureFilterEnum.Linear;
    }
}
