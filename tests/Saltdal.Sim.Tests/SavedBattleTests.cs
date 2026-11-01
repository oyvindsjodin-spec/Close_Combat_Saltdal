using System.Linq;
using System.Numerics;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class SavedBattleTests
{
    /// <summary>A short battle with a few orders, as a player might give them.</summary>
    static SavedBattle Play(long ticks)
    {
        var sc = ScenarioTests.Data.Find("pothus");
        var saved = new SavedBattle("pothus", 7, Side.Allied, null, null);
        var sim = saved.Begin(sc, ScenarioTests.LoadMap(sc.Map));
        var ours = sim.Teams.Where(t => t.Side == Side.Allied).ToList();
        while (sim.TickCount < ticks && !sim.IsOver)
        {
            switch (sim.TickCount)
            {
                case 10: saved.DigIn(ours[0], ours[0].Center + new Vector2(0, 200)); break;
                case 40: saved.Move(ours[1], ours[1].Center + new Vector2(30, 60), MoveMode.Sneak); break;
                case 80: saved.EndPrep(); break;
                case 120: saved.Fire(ours[2], ours[2].Center + new Vector2(0, 300)); break;
                case 300: saved.Hide(ours[1]); break;
                case 320: saved.Move(ours[3], ours[3].Center + new Vector2(-20, 40), MoveMode.Move, new Deployment(0.5f, Formation.Wedge, 6f)); break;
                case 360: saved.Deploy(ours[4], new Deployment(2f, Formation.Line, 9f), dig: false); break;
            }
            sim.Tick();
        }
        return saved;
    }

    static string Snapshot(Simulation sim) => string.Join("\n", sim.Soldiers.Select(s =>
        $"{s.Id} {s.Position.X:R} {s.Position.Y:R} {s.Health} {s.Morale} {s.Ammo}"));

    [Fact]
    public void ALoadedBattleIsTheSameBattle()
    {
        var played = Play(1200);
        var json = played.Save();
        var loaded = SavedBattle.Load(json, out long tick);
        Assert.Equal(1200, tick);
        Assert.Equal(7, loaded.Commands.Count);
        Assert.Equal(new Deployment(0.5f, Formation.Wedge, 6f), loaded.Commands[5].Deployment);
        var sc = ScenarioTests.Data.Find(loaded.BattleId);
        var sim = loaded.Replay(sc, ScenarioTests.LoadMap(sc.Map), tick);
        Assert.Equal(played.Sim.TickCount, sim.TickCount);
        Assert.Equal(Snapshot(played.Sim), Snapshot(sim));
        // And it carries on the same way
        for (int i = 0; i < 300; i++)
        {
            played.Sim.Tick();
            sim.Tick();
        }
        Assert.Equal(Snapshot(played.Sim), Snapshot(sim));
    }

    [Fact]
    public void ThePracticeBattleIsKeptApart()
    {
        var data = ScenarioTests.Data;
        Assert.NotNull(data.Tutorial);
        Assert.DoesNotContain(data.Battles, b => b.IsTutorial);
        Assert.DoesNotContain(data.OperationBattles, b => b.IsTutorial);
        Assert.Same(data.Tutorial, data.Find("tutorial"));
    }
}
