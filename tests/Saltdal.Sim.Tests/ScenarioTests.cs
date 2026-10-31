using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class ScenarioTests
{
    public static MapData LoadMap(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "../../../../../data/maps", name);
        var meta = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "map.json"))).RootElement;
        return MapData.FromBytes(meta.GetProperty("width").GetInt32(), meta.GetProperty("height").GetInt32(),
            meta.GetProperty("cellSize").GetSingle(),
            File.ReadAllBytes(Path.Combine(dir, "height.bin")),
            File.ReadAllBytes(Path.Combine(dir, "terrain.bin")));
    }

    public static TheoryData<string> Battles()
    {
        var data = new TheoryData<string>();
        foreach (var s in Scenarios.All) data.Add(s.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(Battles))]
    public void EveryBattleStartsOnSolidGroundAndGetsGoing(string id)
    {
        var sc = Scenarios.Find(id);
        var map = LoadMap(sc.Map);
        var sim = sc.Build(map, 1, null);
        Assert.False(string.IsNullOrEmpty(sim.Title));
        Assert.All(sim.Soldiers, s => Assert.True(map.IsPassable(map.WorldToCell(s.Position).Item1, map.WorldToCell(s.Position).Item2), $"{s.Name} starts in water"));
        Assert.All(sim.Flags, f => Assert.Equal(Side.Allied, f.Owner));
        Assert.Contains(sim.Teams, t => t.Side == Side.Allied && t.IsHeadquarters);
        Assert.Contains(sim.Teams, t => t.Side == Side.German && t.IsHeadquarters);
        // Every flag can be walked to from where the Germans start
        var german = sim.Teams.First(t => t.Side == Side.German).Center;
        Assert.All(sim.Flags, f => Assert.NotNull(sim.Pathfinder.FindPath(german, f.Position)));

        for (int i = 0; i < 3000; i++) sim.Tick(); // five minutes
        Assert.Contains(sim.Teams, t => t.Side == Side.German && (t.IsMoving || t.Order != TeamOrder.None));
    }
}
