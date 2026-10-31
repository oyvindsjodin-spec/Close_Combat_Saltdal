using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class CommanderTests
{
    static MapData Field()
    {
        const int n = 100;
        return new MapData(n, n, 4f, new float[n * n], new TerrainType[n * n]);
    }

    [Fact]
    public void DefendersGoBackToTheirFlag()
    {
        var sim = new Simulation(Field(), seed: 1) { AlliedRetreatY = 0, GermanRetreatY = 400 };
        var flag = new VictoryFlag("Flag", new Vector2(200, 200), Side.Allied);
        sim.Flags.Add(flag);
        var team = sim.SpawnTeam("Section", Side.Allied, new Vector2(200, 230), new[] { "a", "b", "c", "d" });
        sim.Commanders.Add(new DefendAI(Side.Allied));
        sim.Tick(); // posts are given out on the first update
        sim.OrderMove(team, new Vector2(200, 60), MoveMode.MoveFast);
        for (int i = 0; i < 2500; i++) sim.Tick();
        Assert.True(Vector2.Distance(team.Center, new Vector2(200, 230)) < 30f);
        Assert.Contains(team.Order, new[] { TeamOrder.Defend, TeamOrder.DigIn }); // back at its post, maybe digging in
    }

    [Fact]
    public void DefendersCounterAttackALostFlag()
    {
        var sim = new Simulation(Field(), seed: 4) { AlliedRetreatY = 0, GermanRetreatY = 400 };
        var flag = new VictoryFlag("Flag", new Vector2(200, 250), Side.German); // already lost
        var home = new VictoryFlag("Home", new Vector2(200, 60), Side.Allied);
        sim.Flags.Add(flag);
        sim.Flags.Add(home);
        var a = sim.SpawnTeam("A", Side.Allied, new Vector2(190, 60), new[] { "a", "b", "c", "d", "e", "f" });
        var b = sim.SpawnTeam("B", Side.Allied, new Vector2(210, 60), new[] { "g", "h", "i", "j", "k", "l" });
        var enemy = sim.SpawnTeam("Enemy", Side.German, new Vector2(200, 250), new[] { "x" });
        enemy.Soldiers[0].Ammo = 0;
        // Keeps the German side in the battle when the lone man runs
        var far = sim.SpawnTeam("Far", Side.German, new Vector2(390, 390), new[] { "y", "z", "w" });
        sim.OrderHide(far);
        sim.Commanders.Add(new DefendAI(Side.Allied));
        for (int i = 0; i < 3000; i++) sim.Tick();
        Assert.Contains(sim.Messages, m => m.Text.Contains("Counter-attack"));
        Assert.Equal(Side.Allied, flag.Owner);
    }

    [Fact]
    public void PothusGivesTheComputerTheSideThePlayerDidNotPick()
    {
        var Pothus = ScenarioTests.Data.Find("pothus");
        var map = Field();
        Assert.IsType<AttackAI>(Assert.Single(Pothus.Build(map, 1, Side.Allied).Commanders));
        Assert.IsType<DefendAI>(Assert.Single(Pothus.Build(map, 1, Side.German).Commanders));
        Assert.Equal(2, Pothus.Build(map, 1, null).Commanders.Count);
        Assert.Contains(Pothus.Build(map, 1, Side.Allied).Teams, t => t.Side == Side.German && t.IsHeadquarters);
    }
}
