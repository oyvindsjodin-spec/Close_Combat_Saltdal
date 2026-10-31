using System;
using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

/// <summary>Turning a team and choosing its shape by dragging with the right mouse button.</summary>
public class DeploymentTests
{
    static MapData Flat()
    {
        const int n = 60;
        return new MapData(n, n, 4f, new float[n * n], new TerrainType[n * n]);
    }

    static void RunUntilStill(Simulation sim, Team team)
    {
        for (int i = 0; i < 3000 && team.Soldiers.Any(s => s.IsMoving); i++) sim.Tick();
    }

    [Fact]
    public void DragSetsFacingAndSpacing()
    {
        var d = Deployment.FromDrag(Vector2.Zero, new Vector2(0, 30), Formation.Line, 7);
        Assert.Equal(MathF.PI / 2, d.Facing, 3);
        Assert.Equal(5f, d.Spacing, 3);
        // Very short and very long drags are kept sensible
        Assert.Equal(Deployment.MinSpacing, Deployment.FromDrag(Vector2.Zero, new Vector2(1, 0), Formation.Line, 7).Spacing);
        Assert.Equal(Deployment.MaxSpacing, Deployment.FromDrag(Vector2.Zero, new Vector2(500, 0), Formation.Line, 7).Spacing);
    }

    [Theory]
    [InlineData(Formation.Line)]
    [InlineData(Formation.Wedge)]
    [InlineData(Formation.Column)]
    public void ShapesAreCentredAndNobodySharesAPlace(Formation shape)
    {
        var d = new Deployment(1.1f, shape, 5f);
        var o = d.Offsets(8, leaderFirst: true);
        var mid = o.Aggregate(Vector2.Zero, (a, p) => a + p) / o.Length;
        Assert.True(mid.Length() < 0.01f);
        for (int i = 0; i < o.Length; i++)
            for (int j = i + 1; j < o.Length; j++)
                Assert.True(Vector2.Distance(o[i], o[j]) > 1.5f, $"{shape}: men {i} and {j} too close");
    }

    [Fact]
    public void ALineIsWiderThanAColumn()
    {
        var line = new Deployment(0, Formation.Line, 6f).Width(8, true);
        var column = new Deployment(0, Formation.Column, 6f).Width(8, true);
        Assert.True(line > 3 * column);
    }

    [Fact]
    public void ATeamTurnsWhereItStands()
    {
        var map = Flat();
        var sim = new Simulation(map);
        var team = sim.SpawnTeam("t", Side.Allied, map.CellCenter(30, 30), new[] { "a", "b", "c", "d", "e", "f" });
        RunUntilStill(sim, team);
        var before = team.Center;
        // Face west (to the left of the map), in a wide line
        sim.OrderDefend(team, new Deployment(MathF.PI, Formation.Line, 8f));
        Assert.Equal(TeamOrder.Defend, team.Order);
        RunUntilStill(sim, team);
        Assert.True(Vector2.Distance(before, team.Center) < 8f);
        foreach (var s in team.Soldiers)
            Assert.True(MathF.Abs(MathF.IEEERemainder(s.Facing - MathF.PI, 2 * MathF.PI)) < 0.01f);
        // A line facing west runs north to south
        float spreadY = team.Soldiers.Max(s => s.Position.Y) - team.Soldiers.Min(s => s.Position.Y);
        float spreadX = team.Soldiers.Max(s => s.Position.X) - team.Soldiers.Min(s => s.Position.X);
        Assert.True(spreadY > 25f && spreadX < 12f, $"spread x {spreadX}, y {spreadY}");
    }

    [Fact]
    public void AMovingTeamArrivesInTheShapeAsked()
    {
        var map = Flat();
        var sim = new Simulation(map);
        var team = sim.SpawnTeam("t", Side.Allied, map.CellCenter(10, 10), new[] { "a", "b", "c", "d", "e" });
        var goal = map.CellCenter(40, 40);
        var deploy = new Deployment(0f, Formation.Line, 6f); // facing east
        Assert.True(sim.OrderMove(team, goal, MoveMode.MoveFast, deploy: deploy));
        RunUntilStill(sim, team);
        Assert.True(Vector2.Distance(goal, team.Center) < 3f);
        foreach (var s in team.Soldiers) Assert.Equal(0f, s.Facing, 3);
        float spreadY = team.Soldiers.Max(s => s.Position.Y) - team.Soldiers.Min(s => s.Position.Y);
        Assert.True(spreadY > 15f);
    }

    [Fact]
    public void DiggingInKeepsTheShapeAfterwards()
    {
        var map = Flat();
        var sim = new Simulation(map);
        var team = sim.SpawnTeam("t", Side.Allied, map.CellCenter(30, 30), new[] { "a", "b", "c" });
        var deploy = new Deployment(MathF.PI / 2, Formation.Line, 7f);
        sim.OrderDigIn(team, deploy);
        Assert.Equal(deploy, team.Deployment);
        for (int i = 0; i < 20000 && team.Order == TeamOrder.DigIn; i++) sim.Tick();
        Assert.Equal(TeamOrder.Defend, team.Order);
        Assert.Equal(deploy, team.Deployment);
    }
}
