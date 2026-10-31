using Godot;
using Saltdal.Sim;

namespace Saltdal.Game;

/// <summary>Reads a map folder written by tools/build_map.py.</summary>
public static class MapLoader
{
    public static MapData Load(string folder)
    {
        var json = Json.ParseString(FileAccess.GetFileAsString($"{folder}/map.json")).AsGodotDictionary();
        int width = json["width"].AsInt32();
        int height = json["height"].AsInt32();
        float cell = json["cellSize"].AsSingle();
        var heights = FileAccess.GetFileAsBytes($"{folder}/height.bin");
        var terrain = FileAccess.GetFileAsBytes($"{folder}/terrain.bin");
        return MapData.FromBytes(width, height, cell, heights, terrain);
    }
}

/// <summary>Reads the game data files (weapons, units, battles) through Godot, so it works in an exported game too.</summary>
public static class DataLoader
{
    public static GameData Load(string folder = "res://data")
    {
        var battles = new System.Collections.Generic.List<(string, string)>();
        foreach (var file in DirAccess.GetFilesAt($"{folder}/battles"))
            if (file.EndsWith(".json"))
                battles.Add((file, FileAccess.GetFileAsString($"{folder}/battles/{file}")));
        return GameData.Load(
            FileAccess.GetFileAsString($"{folder}/weapons.json"),
            FileAccess.GetFileAsString($"{folder}/units.json"),
            battles);
    }
}
