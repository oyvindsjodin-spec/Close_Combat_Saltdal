using System.IO;
using System;
using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class CombatTests
{
    /// <summary>A flat open map, with an optional ridge or strip of forest across column 20.</summary>
    static MapData Field(float ridgeHeight = 0, bool forest = false)
    {
        const int n = 60;
        var heights = new float[n * n];
        var terrain = new TerrainType[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 18; x < 23; x++)
            {
                heights[y * n + x] = ridgeHeight;
                if (forest) terrain[y * n + x] = TerrainType.Forest;
            }
        return new MapData(n, n, 4f, heights, terrain);
    }

    [Fact]
    public void HillsAndForestBlockLineOfSight()
    {
        var a = new Vector2(20, 100);
        var b = new Vector2(200, 100);
        Assert.True(new Simulation(Field()).LineOfSight(a, 1.7f, b, 1.7f));
        Assert.False(new Simulation(Field(ridgeHeight: 10)).LineOfSight(a, 1.7f, b, 1.7f));
        // A strip 80 m deep is more forest than anyone can see through
        var thick = Field(forest: true);
        for (int y = 0; y < thick.Height; y++)
            for (int x = 10; x < 30; x++)
                thick.Terrain[y * thick.Width + x] = TerrainType.Forest;
        Assert.False(new Simulation(thick).LineOfSight(a, 1.7f, b, 1.7f));
    }

    [Fact]
    public void ProneMenAreSpottedLaterThanStandingMen()
    {
        var sim = new Simulation(Field());
        var watcher = sim.SpawnTeam("w", Side.Allied, new Vector2(20, 20), new[] { "a" });
        var target = sim.SpawnTeam("t", Side.German, new Vector2(20, 220), new[] { "b" });
        var o = watcher.Soldiers[0];
        var t = target.Soldiers[0];
        t.Posture = Posture.Standing;
        float standing = sim.SpotRange(o, t);
        t.Posture = Posture.Prone;
        Assert.True(sim.SpotRange(o, t) < standing / 2);
    }

    [Fact]
    public void FireCausesStressAndCasualties()
    {
        var sim = new Simulation(Field(), seed: 7);
        var shooters = sim.SpawnTeam("Brens", Side.Allied, new Vector2(120, 40), new[] { "a", "b", "c", "d" }, Weapon.Bren);
        var targets = sim.SpawnTeam("Targets", Side.German, new Vector2(120, 140), new[] { "e", "f", "g", "h", "i", "j" });
        sim.OrderDefend(shooters, targets.Center);
        sim.OrderDefend(targets, shooters.Center);
        foreach (var s in targets.Soldiers) s.Ammo = 0; // they cannot shoot back
        for (int i = 0; i < 600; i++) sim.Tick();
        Assert.Contains(sim.RecentShots, s => s.Side == Side.Allied);
        Assert.True(targets.Soldiers.Count(s => !s.CanAct) >= 2, "an unanswered MG at 100 m should hurt");
        Assert.All(shooters.Soldiers, s => Assert.Equal(Health.Healthy, s.Health));
    }

    [Theory]
    [InlineData(0f, MoraleState.Ready)]
    [InlineData(30f, MoraleState.Pinned)]
    [InlineData(60f, MoraleState.Cowering)]
    [InlineData(80f, MoraleState.Panicked)]
    [InlineData(110f, MoraleState.Routed)]
    public void StressSetsMorale(float stress, MoraleState expected)
    {
        Assert.Equal(expected, Simulation.MoraleFromStress(stress, MoraleState.Ready));
    }

    [Fact]
    public void RoutedMenStayRoutedUntilTheyCalmDown()
    {
        Assert.Equal(MoraleState.Routed, Simulation.MoraleFromStress(60f, MoraleState.Routed));
        Assert.Equal(MoraleState.Pinned, Simulation.MoraleFromStress(40f, MoraleState.Routed));
    }

    [Fact]
    public void FlagGoesToTheOnlySideNearIt()
    {
        var sim = new Simulation(Field());
        var flag = new VictoryFlag("f", new Vector2(120, 120), Side.Allied);
        sim.Flags.Add(flag);
        sim.SpawnTeam("g", Side.German, new Vector2(120, 120), new[] { "a", "b" });
        for (int i = 0; i < 10; i++) sim.Tick();
        Assert.Equal(Side.German, flag.Owner);
    }

    [Fact]
    public void AttackingAIMovesTowardTheFlag()
    {
        var sim = new Simulation(Field());
        sim.Flags.Add(new VictoryFlag("f", new Vector2(120, 30), Side.Allied));
        var team = sim.SpawnTeam("g", Side.German, new Vector2(120, 200), new[] { "a", "b", "c" });
        sim.Commanders.Add(new AttackAI(Side.German));
        float start = Vector2.Distance(team.Center, sim.Flags[0].Position);
        for (int i = 0; i < 600; i++) sim.Tick();
        Assert.True(Vector2.Distance(team.Center, sim.Flags[0].Position) < start - 50);
    }

    [Fact]
    public void PothusBattleIsRepeatableAndEnds()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "../../../../../data/maps/pothus");
        MapData Load() => MapData.FromBytes(384, 384, 4f,
            File.ReadAllBytes(Path.Combine(dir, "height.bin")),
            File.ReadAllBytes(Path.Combine(dir, "terrain.bin")));
        BattleResult Run()
        {
            var sim = Scenarios.PothusDayOne(Load(), seed: 3);
            while (!sim.IsOver) sim.Tick();
            return sim.Result!;
        }
        var first = Run();
        Assert.Equal(first, Run());
        Assert.True(first.AlliedLosses.Killed + first.GermanLosses.Killed > 0);
    }
}
