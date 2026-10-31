using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class SimulationTests
{
    /// <summary>A flat 40 x 40 map with a river down column 20 and a bridge at row 30.</summary>
    static MapData RiverMap()
    {
        const int n = 40;
        var heights = new float[n * n];
        var terrain = new TerrainType[n * n];
        for (int y = 0; y < n; y++)
        {
            terrain[y * n + 20] = TerrainType.Water;
        }
        terrain[30 * n + 20] = TerrainType.Bridge;
        return new MapData(n, n, 4f, heights, terrain);
    }

    [Fact]
    public void PathCrossesRiverOnlyAtTheBridge()
    {
        var map = RiverMap();
        var path = new Pathfinder(map).FindPath(map.CellCenter(5, 5), map.CellCenter(35, 5));
        Assert.NotNull(path);
        var sim = new Simulation(map);
        var team = sim.SpawnTeam("t", Side.Allied, map.CellCenter(5, 5), new[] { "a" });
        Assert.True(sim.OrderMove(team, map.CellCenter(35, 5), MoveMode.MoveFast));
        bool usedBridge = false;
        for (int i = 0; i < 3000 && team.IsMoving; i++)
        {
            sim.Tick();
            var t = map.TerrainAt(team.Soldiers[0].Position);
            Assert.NotEqual(TerrainType.Water, t);
            usedBridge |= t == TerrainType.Bridge;
        }
        Assert.True(usedBridge);
        Assert.False(team.IsMoving);
        Assert.True(Vector2.Distance(team.Soldiers[0].Position, map.CellCenter(35, 5)) < 1f);
    }

    [Fact]
    public void CliffsBlockMovement()
    {
        const int n = 20;
        var heights = new float[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 10; x < n; x++)
                heights[y * n + x] = 50; // a 50 m wall at column 10
        var map = new MapData(n, n, 4f, heights, new TerrainType[n * n]);
        Assert.Null(new Pathfinder(map).FindPath(map.CellCenter(2, 2), map.CellCenter(15, 2)));
    }

    [Fact]
    public void SneakIsSlowerThanMoveFast()
    {
        var map = RiverMap();
        var sim = new Simulation(map);
        var at = map.CellCenter(5, 5);
        var dir = Vector2.UnitY;
        Assert.True(sim.Speed(at, dir, MoveMode.Sneak) < sim.Speed(at, dir, MoveMode.Move));
        Assert.True(sim.Speed(at, dir, MoveMode.Move) < sim.Speed(at, dir, MoveMode.MoveFast));
    }

    [Fact]
    public void SameOrdersGiveSameResult()
    {
        Vector2[] Run()
        {
            var map = RiverMap();
            var sim = new Simulation(map);
            var team = sim.SpawnTeam("t", Side.German, map.CellCenter(5, 35), new[] { "a", "b", "c", "d" });
            sim.OrderMove(team, map.CellCenter(35, 2), MoveMode.Move);
            for (int i = 0; i < 400; i++) sim.Tick();
            return team.Soldiers.Select(s => s.Position).ToArray();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void PothusMapLoadsAndCanBeCrossed()
    {
        var map = ScenarioTests.LoadMap("pothus");
        Assert.Contains(TerrainType.Water, map.Terrain);
        Assert.Contains(TerrainType.Wreck, map.Terrain);
        Assert.Contains(TerrainType.Building, map.Terrain);
        // Walk the valley floor from the south edge to the north edge
        var road = Enumerable.Range(0, map.Width).First(x => map.TerrainAt(x, map.Height - 1) == TerrainType.Road);
        var roadNorth = Enumerable.Range(0, map.Width).First(x => map.TerrainAt(x, 0) == TerrainType.Road);
        var path = new Pathfinder(map).FindPath(map.CellCenter(road, map.Height - 1), map.CellCenter(roadNorth, 0));
        Assert.NotNull(path);
    }
}
