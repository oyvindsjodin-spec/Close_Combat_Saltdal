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
    /// <summary>Ploughed field or meadow: open ground, like grass.</summary>
    Field = 7,
    /// <summary>A house, barn or shed: good cover, blocks sight, slow to move through.</summary>
    Building = 8,
    /// <summary>Shallow water over gravel bars: slow and exposed, but it can be waded.</summary>
    Ford = 9,
    /// <summary>The wreck of a blown bridge: impassable.</summary>
    Wreck = 10,
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
        TerrainType.Field => 1.0f,
        TerrainType.Building => 1.4f,
        TerrainType.Ford => 3.5f,
        TerrainType.Wreck => float.PositiveInfinity,
        _ => 1.0f,
    };

    public static bool IsPassable(TerrainType t) => !float.IsInfinity(MoveCost(t));

    /// <summary>Terrain that hides men and stops some of the bullets.</summary>
    public static bool IsCover(TerrainType t) => t is TerrainType.Forest or TerrainType.Rock or TerrainType.Building;

    public static string Name(TerrainType t) => t switch
    {
        TerrainType.Building => "House",
        TerrainType.Wreck => "Blown bridge",
        _ => t.ToString(),
    };
}
