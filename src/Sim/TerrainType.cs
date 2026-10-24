namespace Saltdal.Sim;

/// <summary>Terrain in one map cell. The byte values match tools/build_map.py.</summary>
public enum TerrainType : byte
{
    Grass = 0,
    Forest = 1,
    Rock = 2,
    Snow = 3,
    Water = 4,
    Road = 5,
    Bridge = 6,
}

public static class TerrainInfo
{
    /// <summary>Multiplier on walking time; higher is slower. Infinity means impassable.</summary>
    public static float MoveCost(TerrainType t) => t switch
    {
        TerrainType.Road => 0.8f,
        TerrainType.Bridge => 0.8f,
        TerrainType.Grass => 1.0f,
        TerrainType.Forest => 1.5f,
        TerrainType.Rock => 2.5f,
        TerrainType.Snow => 2.0f,
        TerrainType.Water => float.PositiveInfinity,
        _ => 1.0f,
    };

    public static bool IsPassable(TerrainType t) => !float.IsInfinity(MoveCost(t));

    public static string Name(TerrainType t) => t.ToString();
}
