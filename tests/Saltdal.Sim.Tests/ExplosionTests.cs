using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class ExplosionTests
{
    static MapData Field(float ridgeAtColumn20 = 0)
    {
        const int n = 80;
        var heights = new float[n * n];
        var terrain = new TerrainType[n * n];
        if (ridgeAtColumn20 > 0)
            for (int y = 0; y < n; y++)
                for (int x = 18; x < 23; x++) heights[y * n + x] = ridgeAtColumn20;
        return new MapData(n, n, 4f, heights, terrain);
    }

    [Fact]
    public void ShellsHurtAndFrightenMenNearby()
    {
        var sim = new Simulation(Field(), seed: 3);
        var near = sim.SpawnTeam("near", Side.German, new Vector2(100, 100), new[] { "a", "b", "c", "d", "e" });
        var far = sim.SpawnTeam("far", Side.German, new Vector2(100, 250), new[] { "f" });
        foreach (var s in near.Soldiers.Concat(far.Soldiers)) s.Posture = Posture.Standing;
        for (int i = 0; i < 5; i++) sim.Explode(near.Center, Explosive.Gun25Pounder, Side.Allied);
        Assert.True(near.Soldiers.Count(s => s.Health != Health.Healthy) >= 2);
        Assert.All(near.Soldiers.Where(s => s.Health != Health.Dead), s => Assert.True(s.Stress > 20));
        Assert.Equal(Health.Healthy, far.Soldiers[0].Health);
        Assert.Equal(0f, far.Soldiers[0].Stress);
    }

    [Fact]
    public void SupportMissionLandsAfterDelayAndUsesUpMissions()
    {
        var sim = new Simulation(Field(), seed: 5);
        var guns = new SupportAsset("Guns", Side.Allied, Explosive.Gun25Pounder, missions: 1, rounds: 4,
            delay: 20, interval: 1, spread: 10);
        sim.Support.Add(guns);
        Assert.True(sim.CallSupport(guns, new Vector2(200, 200)));
        Assert.False(guns.IsAvailable(sim.Time));
        for (int i = 0; i < 150; i++) sim.Tick(); // 15 s
        Assert.Empty(sim.RecentBlasts);
        for (int i = 0; i < 100; i++) sim.Tick(); // 25 s
        Assert.NotEmpty(sim.RecentBlasts);
        Assert.Equal(0, guns.MissionsLeft);
    }

    [Fact]
    public void OnlyHeadquartersThatCanSeeTheSpotCanCallSupport()
    {
        var sim = new Simulation(Field(ridgeAtColumn20: 15), seed: 1);
        var guns = new SupportAsset("Guns", Side.Allied, Explosive.Gun25Pounder, 2, 4, 20, 1, 10);
        var section = sim.SpawnTeam("Section", Side.Allied, new Vector2(20, 100), new[] { "a" });
        var hq = sim.SpawnTeam("HQ", Side.Allied, new Vector2(20, 120), new[] { "b" });
        hq.IsHeadquarters = true;
        Assert.NotNull(sim.OrderSupport(section, guns, new Vector2(40, 100)));
        Assert.NotNull(sim.OrderSupport(hq, guns, new Vector2(200, 120))); // behind the ridge
        Assert.Null(sim.OrderSupport(hq, guns, new Vector2(40, 140)));
        Assert.Equal(1, guns.MissionsLeft);
    }

    [Fact]
    public void MortarsShellBunchedUpEnemies()
    {
        var sim = new Simulation(Field(), seed: 9);
        var mortar = sim.SpawnTeam("Mortar", Side.Allied, new Vector2(150, 20), new[] { "a" }, Weapon.Mortar81Norwegian);
        var targets = sim.SpawnTeam("Targets", Side.German, new Vector2(150, 260), new[] { "b", "c", "d", "e", "f" });
        foreach (var s in targets.Soldiers) { s.Ammo = 0; s.Posture = Posture.Standing; }
        sim.OrderDefend(mortar, targets.Center);
        for (int i = 0; i < 900; i++) sim.Tick();
        Assert.True(mortar.Soldiers[0].Ammo < Weapon.Mortar81Norwegian.Ammo, "the mortar should have fired");
        Assert.Contains(targets.Soldiers, s => s.Stress > 0 || s.Health != Health.Healthy);
    }

    [Fact]
    public void MortarFireOrderShellsThePointThenStops()
    {
        var sim = new Simulation(Field(), seed: 2);
        var mortar = sim.SpawnTeam("Mortar", Side.Allied, new Vector2(150, 20), new[] { "a" }, Weapon.Mortar81Norwegian);
        var target = new Vector2(150, 280);
        sim.OrderFire(mortar, target);
        for (int i = 0; i < 1200; i++) sim.Tick();
        Assert.Equal(Weapon.Mortar81Norwegian.Ammo - Simulation.MortarBarrage, mortar.Soldiers[0].Ammo);
        Assert.Contains(sim.Messages, m => m.Text.Contains("barrage fired"));

        sim.OrderFire(mortar, new Vector2(150, 80)); // 60 m away: too close
        Assert.Contains(sim.Messages, m => m.Text.Contains("too close"));
    }

    [Fact]
    public void MortarShellsAPairOfMenOnItsOwn()
    {
        var sim = new Simulation(Field(), seed: 4);
        var mortar = sim.SpawnTeam("Mortar", Side.Allied, new Vector2(150, 20), new[] { "a" }, Weapon.Mortar81Norwegian);
        var targets = sim.SpawnTeam("Targets", Side.German, new Vector2(150, 260), new[] { "b", "c" });
        foreach (var s in targets.Soldiers) { s.Ammo = 0; s.Posture = Posture.Standing; }
        sim.OrderDefend(mortar, targets.Center);
        for (int i = 0; i < 900; i++) sim.Tick();
        Assert.True(mortar.Soldiers[0].Ammo < Weapon.Mortar81Norwegian.Ammo, "the mortar should have fired at the pair");
    }

    [Fact]
    public void TheTutorialMortarTeamFiresWhenTold()
    {
        var sc = ScenarioTests.Data.Find("tutorial");
        var sim = sc.Build(ScenarioTests.LoadMap(sc.Map), 1, Side.Allied);
        while (sim.InPrep) sim.Tick();
        var team = sim.Teams.First(t => t.Side == Side.Allied && !t.IsHeadquarters && t.Soldiers.Any(s => s.Weapon.IsIndirect));
        Assert.True(team.Soldiers.Count > 1, "a mortar team has a crew, not just the man with the tube");
        var mortar = team.Soldiers.First(s => s.Weapon.IsIndirect);
        int before = mortar.Ammo;
        var enemy = sim.Teams.First(t => t.Side == Side.German).Center;
        sim.OrderFire(team, mortar.Position + Vector2.Normalize(enemy - mortar.Position) * 400);
        for (int i = 0; i < 1200; i++) sim.Tick();
        Assert.True(mortar.Ammo <= before - Simulation.MortarBarrage, $"fired {before - mortar.Ammo} bombs");
    }
}
