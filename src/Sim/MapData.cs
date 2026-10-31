using System;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>
/// A battle map: a grid of cells with height (metres) and terrain type.
/// World coordinates are metres, x east and y south, origin at the north-west corner.
/// </summary>
public sealed class MapData
{
    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }
    public float[] Heights { get; }
    public TerrainType[] Terrain { get; }

    public float WorldWidth => Width * CellSize;
    public float WorldHeight => Height * CellSize;

    public MapData(int width, int height, float cellSize, float[] heights, TerrainType[] terrain)
    {
        if (heights.Length != width * height || terrain.Length != width * height)
            throw new ArgumentException("Height and terrain arrays must have width * height entries.");
        Width = width;
        Height = height;
        CellSize = cellSize;
        Heights = heights;
        Terrain = terrain;
    }

    /// <summary>Build a map from the raw files written by tools/build_map.py.</summary>
    public static MapData FromBytes(int width, int height, float cellSize, byte[] heightBytes, byte[] terrainBytes)
    {
        var heights = new float[width * height];
        Buffer.BlockCopy(heightBytes, 0, heights, 0, heights.Length * sizeof(float));
        var terrain = new TerrainType[width * height];
        for (int i = 0; i < terrain.Length; i++)
            terrain[i] = (TerrainType)terrainBytes[i];
        return new MapData(width, height, cellSize, heights, terrain);
    }

    /// <summary>
    /// A copy with its own terrain, for one battle: trenches dug and houses burnt
    /// down change the ground, and the next battle on this map starts fresh.
    /// </summary>
    public MapData Copy() => new(Width, Height, CellSize, Heights, (TerrainType[])Terrain.Clone());

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public TerrainType TerrainAt(int x, int y) => Terrain[y * Width + x];

    public float HeightAt(int x, int y) => Heights[y * Width + x];

    public (int X, int Y) WorldToCell(Vector2 p) =>
        ((int)MathF.Floor(p.X / CellSize), (int)MathF.Floor(p.Y / CellSize));

    public Vector2 CellCenter(int x, int y) => new((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

    public TerrainType TerrainAt(Vector2 p)
    {
        var (x, y) = WorldToCell(p);
        return InBounds(x, y) ? TerrainAt(x, y) : TerrainType.Rock;
    }

    /// <summary>Height at any world point, bilinearly interpolated between cell centres.</summary>
    public float HeightAt(Vector2 p)
    {
        float fx = Math.Clamp(p.X / CellSize - 0.5f, 0, Width - 1);
        float fy = Math.Clamp(p.Y / CellSize - 0.5f, 0, Height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float dx = fx - x0, dy = fy - y0;
        float top = HeightAt(x0, y0) * (1 - dx) + HeightAt(x1, y0) * dx;
        float bottom = HeightAt(x0, y1) * (1 - dx) + HeightAt(x1, y1) * dx;
        return top * (1 - dy) + bottom * dy;
    }

    public bool IsPassable(int x, int y) => InBounds(x, y) && TerrainInfo.IsPassable(TerrainAt(x, y));

    /// <summary>
    /// The passable, non-rock cell nearest to a point given as fractions of the
    /// map's width and height (0,0 is the north-west corner).
    /// </summary>
    public Vector2 NearestOpenGround(float fx, float fy)
    {
        int cx = (int)(fx * Width), cy = (int)(fy * Height);
        for (int r = 0; r < 100; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = cx + dx, y = cy + dy;
                    if (IsPassable(x, y) && TerrainAt(x, y) is not (TerrainType.Rock or TerrainType.Building or TerrainType.Ford))
                        return CellCenter(x, y);
                }
        return CellCenter(Math.Clamp(cx, 0, Width - 1), Math.Clamp(cy, 0, Height - 1));
    }
}
