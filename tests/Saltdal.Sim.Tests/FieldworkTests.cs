using System;
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
    public void DiggingInMakesALineOfFoxholesFacingTheEnemy()
    {
        var sim = new Simulation(Village());
        var team = sim.SpawnTeam("Section", Side.Allied, new Vector2(100, 60), new[] { "a", "b", "c", "d", "e", "f" });
        sim.PrepUntil = 300;
        // The enemy is expected from the south
        sim.OrderDigIn(team, new Vector2(100, 200));
        for (int i = 0; i < 1200; i++) sim.Tick();
        Assert.Equal(6, sim.Trenches.Count);
        // The leader a little behind the middle, the rest in a line across, west to east
        var line = team.Soldiers.Skip(1).Select(s => s.Position).OrderBy(p => p.X).ToList();
        Assert.True(line.Max(p => p.Y) - line.Min(p => p.Y) < 3f, "the holes make a line across the way they face");
        for (int i = 1; i < line.Count; i++)
            Assert.InRange(line[i].X - line[i - 1].X, 4f, 6f);
        Assert.True(team.Soldiers[0].Position.Y < line.Average(p => p.Y) - 3f, "the leader is behind the line");
        // Everyone faces south
        Assert.All(team.Soldiers, s => Assert.InRange(s.Facing, MathF.PI / 2 - 0.2f, MathF.PI / 2 + 0.2f));
    }

    [Fact]
    public void DefendingMenLineUpFacingTheEnemy()
    {
        var sim = new Simulation(Village());
        var team = sim.SpawnTeam("Section", Side.Allied, new Vector2(100, 60), new[] { "a", "b", "c", "d" });
        // Enemy expected from the east: the line runs north to south
        sim.OrderDefend(team, new Vector2(220, 60));
        for (int i = 0; i < 200; i++) sim.Tick();
        var line = team.Soldiers.Skip(1).Select(s => s.Position).ToList();
        Assert.True(line.Max(p => p.X) - line.Min(p => p.X) < 3f);
        Assert.True(line.Max(p => p.Y) - line.Min(p => p.Y) > 8f);
        Assert.All(team.Soldiers, s => Assert.InRange(s.Facing, -0.2f, 0.2f));
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

    /// <summary>A team dug in facing south on open grass, with the preparation time still running.</summary>
    static (Simulation Sim, Team Team) DugIn(ulong seed = 1)
    {
        var sim = new Simulation(Village(), seed);
        var team = sim.SpawnTeam("Section", Side.Allied, new Vector2(100, 60), new[] { "a", "b", "c", "d", "e" });
        sim.PrepUntil = 2000;
        sim.OrderDigIn(team, new Vector2(100, 200));
        for (int i = 0; i < 1200; i++) sim.Tick();
        Assert.Equal(5, sim.Trenches.Count);
        return (sim, team);
    }

    [Fact]
    public void ATeamThatLeftItsFoxholesCanBeOrderedBackIntoThem()
    {
        var (sim, team) = DugIn();
        // Out of the holes and away
        Assert.True(sim.OrderMove(team, new Vector2(40, 40), MoveMode.Move));
        for (int i = 0; i < 600; i++) sim.Tick();
        Assert.All(team.Soldiers, s => Assert.False(sim.HasTrench(s.Position)));

        // A move order onto one end of the line puts every man back in a hole of his own
        Assert.True(sim.OrderMove(team, sim.Trenches[0].At, MoveMode.Move));
        for (int i = 0; i < 900; i++) sim.Tick();
        Assert.All(team.Soldiers, s => Assert.True(sim.HasTrench(s.Position)));
        Assert.Equal(5, team.Soldiers.Select(s => sim.Map.WorldToCell(s.Position)).Distinct().Count());
        // Facing the way the holes were dug: south
        Assert.All(team.Soldiers, s => Assert.InRange(s.Facing, MathF.PI / 2 - 0.3f, MathF.PI / 2 + 0.3f));
    }

    [Fact]
    public void DefendingNextToEmptyFoxholesGetsIntoThem()
    {
        var (sim, team) = DugIn();
        var other = sim.SpawnTeam("Other", Side.Allied, new Vector2(100, 68), new[] { "x", "y", "z" });
        // The first team leaves; the second, standing just behind the line, is told to defend
        sim.OrderMove(team, new Vector2(40, 40), MoveMode.Move);
        sim.OrderDefend(other, new Vector2(100, 200));
        for (int i = 0; i < 600; i++) sim.Tick();
        Assert.All(other.Soldiers, s => Assert.True(sim.HasTrench(s.Position)));
        // The holes are taken now: the first team coming back gets only the two left
        sim.OrderMove(team, sim.Trenches[0].At, MoveMode.Move);
        for (int i = 0; i < 900; i++) sim.Tick();
        Assert.Equal(2, team.Soldiers.Count(s => sim.HasTrench(s.Position)));
        Assert.Equal(8, sim.Teams.SelectMany(t => t.Soldiers).Select(s => sim.Map.WorldToCell(s.Position)).Distinct().Count());
    }

    [Fact]
    public void FoxholesGiveMuchBetterCoverThanLyingInTheOpen()
    {
        var (sim, team) = DugIn();
        var enemy = sim.SpawnTeam("Enemy", Side.German, new Vector2(100, 200), new[] { "x" });
        var shooter = enemy.Soldiers[0];
        var inHole = team.Soldiers[1];
        Assert.True(sim.InFoxhole(inHole));
        var inOpen = sim.SpawnTeam("Open", Side.Allied, new Vector2(160, 60), new[] { "o" }).Soldiers[0];
        sim.OrderDefend(inOpen.Team, new Vector2(160, 200));
        for (int i = 0; i < 50; i++) sim.Tick();
        Assert.Equal(Posture.Prone, inOpen.Posture);
        Assert.True(sim.HitChance(shooter, inHole, 140) < sim.HitChance(shooter, inOpen, 140) * 0.35f);
        Assert.True(sim.Exposure(inHole) < sim.Exposure(inOpen) * 0.5f);
        Assert.True(sim.SpotRange(shooter, inHole) < sim.SpotRange(shooter, inOpen));
    }

    [Fact]
    public void APanickedManStaysDownInHisFoxhole()
    {
        var (sim, team) = DugIn();
        sim.SpawnTeam("Enemy", Side.German, new Vector2(100, 160), new[] { "x" });
        var man = team.Soldiers[2];
        man.Stress = 95; // panicked, not routed
        for (int i = 0; i < 5; i++) sim.Tick();
        Assert.Equal(MoraleState.Panicked, man.Morale);
        Assert.False(man.Fleeing);
        Assert.True(sim.HasTrench(man.Position));
    }

    [Fact]
    public void ShellsBurstingNearFoxholesRarelyHurtTheMenInThem()
    {
        int hurtInHoles = 0, hurtInOpen = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (sim, team) = DugIn(seed);
            var open = sim.SpawnTeam("Open", Side.Allied, new Vector2(100, 120), new[] { "a", "b", "c", "d", "e" });
            sim.OrderDefend(open, new Vector2(100, 200));
            sim.Tick();
            foreach (var s in team.Soldiers) sim.Explode(s.Position + new Vector2(0, 6), Explosive.Mortar81, Side.German);
            foreach (var s in open.Soldiers) sim.Explode(s.Position + new Vector2(0, 6), Explosive.Mortar81, Side.German);
            hurtInHoles += team.Soldiers.Count(s => s.Health != Health.Healthy);
            hurtInOpen += open.Soldiers.Count(s => s.Health != Health.Healthy);
        }
        Assert.True(hurtInHoles * 3 < hurtInOpen, $"{hurtInHoles} hurt in holes, {hurtInOpen} in the open");
    }
}
