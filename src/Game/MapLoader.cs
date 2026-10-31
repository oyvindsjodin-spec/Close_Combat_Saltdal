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
    /// <summary>
    /// Where the numbers are read from. The stand-alone game ships a copy of the
    /// data folder beside the program, so it can be changed with a text editor;
    /// without one it uses the copy built into the game.
    /// </summary>
    public static string Folder()
    {
        if (!OS.HasFeature("editor"))
        {
            var dir = OS.GetExecutablePath().GetBaseDir();
            // Beside the .exe on Windows; beside the .app on a Mac (the program is inside it, three folders down)
            foreach (var d in new[] { dir, $"{dir}/../../.." })
                if (DirAccess.DirExistsAbsolute($"{d}/data/battles"))
                    return ProjectSettings.GlobalizePath($"{d}/data").SimplifyPath();
        }
        return "res://data";
    }

    public static GameData Load() => Load(Folder());

    public static GameData Load(string folder)
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
