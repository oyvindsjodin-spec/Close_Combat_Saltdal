using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class ScenarioTests
{
    static readonly string DataFolder = Path.Combine(AppContext.BaseDirectory, "../../../../../data");
    static GameData? _data;
    public static GameData Data => _data ??= GameData.LoadFolder(DataFolder);

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
        foreach (var s in Data.Battles) data.Add(s.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(Battles))]
    public void EveryBattleStartsOnSolidGroundAndGetsGoing(string id)
    {
        var sc = Data.Find(id);
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

        while (sim.InPrep) sim.Tick();
        for (int i = 0; i < 3000; i++) sim.Tick(); // five minutes after the attack begins
        Assert.Contains(sim.Teams, t => t.Side == Side.German && (t.IsMoving || t.Order != TeamOrder.None));
    }

    [Fact]
    public void TheBattlesComeInTheOrderTheyWereFought()
    {
        Assert.Equal(new[] { "viskiskoia", "pothus", "rognan" }, Data.Battles.Select(b => b.Id));
    }

    [Fact]
    public void TheDataFilesKeepTheWeaponsTheGameWasBalancedWith()
    {
        Assert.Equal(Weapon.LeeEnfield, Data.Weapons["lee_enfield"]);
        Assert.Equal(Weapon.Bren, Data.Weapons["bren"]);
        Assert.Equal(Weapon.Mg34, Data.Weapons["mg34"]);
        Assert.Equal(Weapon.Mp40, Data.Weapons["mp40"]);
        Assert.Equal(Weapon.Grw34, Data.Weapons["grw34"]);
        Assert.Equal(Weapon.Mortar81Norwegian, Data.Weapons["mortar_81_no"]);
        Assert.Equal(Explosive.Gun25Pounder, Data.Explosives["gun_25pdr"]);
    }

    [Theory]
    [MemberData(nameof(Battles))]
    public void TheHistoricalForceFitsItsBudgetAndHasALeader(string id)
    {
        var sc = Data.Find(id);
        foreach (var side in new[] { Side.Allied, Side.German })
        {
            var forces = sc.Forces(side);
            Assert.Null(forces.Problem(forces.DefaultPicks));
            Assert.Contains(forces.Options, o => o.Required && o.Team?.Headquarters == true);
            Assert.Contains(forces.Options, o => !o.Default); // something to swap in
        }
    }

    [Fact]
    public void ThePlayerCanSwapATeamForAnotherWithinTheBudget()
    {
        var sc = Data.Find("pothus");
        var forces = sc.Forces(Side.Allied);
        var picks = forces.DefaultPicks.Where(n => n != "3 Section").Append("No. 1 Ind. Coy").ToList();
        Assert.Null(forces.Problem(picks));
        var sim = sc.Build(LoadMap("pothus"), 1, Side.Allied, picks);
        Assert.DoesNotContain(sim.Teams, t => t.Name == "3 Section");
        var ind = Assert.Single(sim.Teams, t => t.Name == "No. 1 Ind. Coy");
        Assert.Equal(6, ind.Soldiers.Count);
        Assert.Equal(0.45f, ind.Soldiers[2].Experience);
        // The Germans keep their historical force
        Assert.Equal(sc.German.DefaultPicks.Count(n => sc.German.Options.First(o => o.Name == n).Team != null),
            sim.Teams.Count(t => t.Side == Side.German));
    }

    [Fact]
    public void TooMuchOrNoHeadquartersIsRefused()
    {
        var forces = Data.Find("pothus").Allied;
        var all = forces.Options.Select(o => o.Name).ToList();
        Assert.StartsWith("Over budget", forces.Problem(all));
        Assert.Equal("Platoon HQ must come.", forces.Problem(forces.DefaultPicks.Where(n => n != "Platoon HQ").ToList()));
    }

    [Fact]
    public void AMistakeInADataFileSaysWhereItIs()
    {
        string weapons = File.ReadAllText(Path.Combine(DataFolder, "weapons.json"));
        string units = File.ReadAllText(Path.Combine(DataFolder, "units.json")).Replace("\"weapon\": \"bren\"", "\"weapon\": \"brenn\"");
        var e = Assert.Throws<GameDataException>(() => GameData.Load(weapons, units, Array.Empty<(string, string)>()));
        Assert.Contains("units.json", e.Message);
        Assert.Contains("brenn", e.Message);
    }
}
