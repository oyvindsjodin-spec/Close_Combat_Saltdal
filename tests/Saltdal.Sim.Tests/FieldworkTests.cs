using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class FieldworkTests
{
    /// <summary>A flat 240 m square of grass with one 16 m square house in the middle (cells 28 to 31).</summary>
    static MapData Village()
    {
        const int n = 60;
        var terrain = new TerrainType[n * n];
        for (int y = 28; y < 32; y++)
            for (int x = 28; x < 32; x++)
                terrain[y * n + x] = TerrainType.Building;
        return new MapData(n, n, 4f, new float[n * n], terrain);
    }

    static readonly Vector2 HouseMiddle = new(120, 120);

    [Fact]
    public void ManInAHouseSeesOutButNotThroughAnotherHouse()
    {
        var sim = new Simulation(Village());
        Assert.Single(sim.Houses);
        // From the middle of the house, out of its windows
        Assert.True(sim.LineOfSight(HouseMiddle, 1.1f, new Vector2(120, 40), 1.1f));
        // From outside, straight through the house to the far side
        Assert.False(sim.LineOfSight(new Vector2(120, 80), 1.1f, new Vector2(120, 160), 1.1f));
    }

    [Fact]
    public void ATeamMovingIntoAHouseSpreadsThroughItsRooms()
    {
        var sim = new Simulation(Village());
        var team = sim.SpawnTeam("Section", Side.Allied, new Vector2(120, 40), new[] { "a", "b", "c", "d", "e", "f" });
        Assert.True(sim.OrderMove(team, HouseMiddle, MoveMode.Move));
        for (int i = 0; i < 1200; i++) sim.Tick();
        Assert.All(team.Soldiers, s => Assert.Equal(TerrainType.Building, sim.Map.TerrainAt(s.Position)));
        Assert.True(team.Soldiers.Select(s => s.Position).Distinct().Count() == 6, "every man at his own window");
    }

    [Fact]
    public void ABurningHouseDrivesItsMenOutSmokesAndFallsIn()
    {
        var sim = new Simulation(Village());
        var team = sim.SpawnTeam("Section", Side.Allied, HouseMiddle, new[] { "a", "b", "c", "d" });
        sim.OrderDefend(team, new Vector2(120, 0));
        sim.SetOnFire(sim.Houses[0]);
        Assert.Equal(TerrainType.BurningHouse, sim.Map.TerrainAt(HouseMiddle));
        for (int i = 0; i < 300; i++) sim.Tick();
        Assert.All(team.Active, s => Assert.NotEqual(TerrainType.BurningHouse, sim.Map.TerrainAt(s.Position)));
        Assert.NotEmpty(sim.Smoke);
        // Smoke drifting downwind hides what is behind it
        var puff = sim.Smoke.Last();
        var c = puff.PositionAt(sim.Time, sim.Wind);
        Assert.False(sim.LineOfSight(c + new Vector2(-60, 0), 1.7f, c + new Vector2(60, 0), 1.7f));

        while (sim.Houses[0].State == HouseState.Burning) sim.Tick();
        Assert.Equal(HouseState.Ruined, sim.Houses[0].State);
        Assert.Equal(TerrainType.Ruin, sim.Map.TerrainAt(HouseMiddle));
    }

    [Fact]
    public void BigShellsCanSetAHouseOnFire()
    {
        int fires = 0;
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var sim = new Simulation(Village(), seed);
            sim.Explode(HouseMiddle, Explosive.Bomb250Kg, Side.German);
            if (sim.Houses[0].State == HouseState.Burning) fires++;
        }
        Assert.InRange(fires, 8, 20);
    }

    [Fact]
    public void MenDigTrenchesThatProtectThem()
    {
        var sim = new Simulation(Village());
        var team = sim.SpawnTeam("Section", Side.Allied, new Vector2(60, 60), new[] { "a", "b", "c" });
        var enemy = sim.SpawnTeam("Enemy", Side.German, new Vector2(60, 200), new[] { "x" });
        var target = team.Soldiers[1];
        float open = sim.HitChance(enemy.Soldiers[0], target, 140);
        sim.PrepUntil = 200; // nobody shoots while they dig

        sim.OrderDigIn(team, new Vector2(60, 200));
        for (int i = 0; i < 600; i++) sim.Tick();
        Assert.Equal(TeamOrder.DigIn, team.Order);
        Assert.InRange(sim.DigProgress(team), 0.5f, 0.8f);

        for (int i = 0; i < 400; i++) sim.Tick();
        Assert.Equal(TeamOrder.Defend, team.Order);
        Assert.Equal(3, sim.Trenches.Count);
        Assert.All(team.Soldiers, s => Assert.True(sim.HasTrench(s.Position)));
        Assert.True(sim.HitChance(enemy.Soldiers[0], target, 140) < open * 0.5f);
    }

    [Fact]
    public void NobodyFiresOrAttacksBeforeTheAttackBegins()
    {
        var sc = ScenarioTests.Data.Find("pothus");
        var sim = sc.Build(ScenarioTests.LoadMap("pothus"), 1, null);
        Assert.True(sim.InPrep);
        Assert.Equal(Side.German, sim.Attacker);
        var germans = sim.Teams.Where(t => t.Side == Side.German).Select(t => t.Center).ToList();
        for (int i = 0; i < 1800; i++) sim.Tick(); // three minutes
        Assert.Equal(germans, sim.Teams.Where(t => t.Side == Side.German).Select(t => t.Center).ToList());
        Assert.Empty(sim.RecentShots);
        // The defending computer uses the time to dig in
        Assert.True(sim.Trenches.Count(t => t.Side == Side.Allied) >= 20, "the British dug in");

        float limit = sim.TimeLimit;
        sim.EndPrep();
        Assert.False(sim.InPrep);
        Assert.Equal(limit - (10 * 60 - sim.Time), sim.TimeLimit, 1);
    }
}
