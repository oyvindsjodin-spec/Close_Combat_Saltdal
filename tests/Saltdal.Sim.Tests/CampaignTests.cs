using System.Linq;
using Saltdal.Sim;
using Xunit;

namespace Saltdal.Sim.Tests;

public class CampaignTests
{
    static GameData Data => ScenarioTests.Data;

    /// <summary>Plays a battle "instantly": kills the named men, then calls the result.</summary>
    static Simulation Fight(Campaign c, params string[] killed)
    {
        var sc = c.Current;
        var sim = sc.Build(ScenarioTests.LoadMap(sc.Map), 1, c.PlayerSide);
        foreach (var s in sim.Soldiers.Where(s => killed.Contains(s.Name)))
            s.Health = Health.Dead;
        sim.CallCeasefire();
        Assert.True(sim.IsOver);
        return sim;
    }

    [Fact]
    public void TheOperationRunsThroughTheBattlesInOrder()
    {
        var c = new Campaign(Data, Side.Allied);
        Assert.Equal("viskiskoia", c.Current.Id);
        c.Record(Fight(c));
        Assert.Equal("pothus", c.Current.Id);
        c.Record(Fight(c));
        Assert.Equal("rognan", c.Current.Id);
        c.Record(Fight(c));
        Assert.True(c.IsOver);
        Assert.Equal(3, c.Results.Count);
    }

    [Fact]
    public void LossesAndExperienceCarryOverToTheNextBattle()
    {
        var c = new Campaign(Data, Side.Allied);
        c.Record(Fight(c, "Jg. Gruber", "Jg. Hofer", "Gefr. Mayr"));
        var gruppe = c.Current.German.Options.Single(o => o.Name == "1. Gruppe");
        // Six survivors and two of the three losses replaced
        Assert.Equal(8, gruppe.MenCount);
        Assert.DoesNotContain("Jg. Gruber", gruppe.MenNames);
        Assert.Equal("Uffz. Huber", gruppe.MenNames[0]);
        Assert.Equal(0.65f, gruppe.MenExperience![0], 3);
        Assert.Equal(Campaign.ReplacementExperience, gruppe.MenExperience[7]);
        Assert.StartsWith("Jg. ", gruppe.MenNames[7]);
        Assert.True(gruppe.Cost < Data.TeamTypes["gruppe"].Cost, "a smaller team costs less");

        var sim = c.Current.Build(ScenarioTests.LoadMap("pothus"), 1, Side.Allied);
        var team = sim.Teams.Single(t => t.Name == "1. Gruppe");
        Assert.Equal(8, team.Soldiers.Count);
        Assert.Equal(Weapon.Mg34, team.Soldiers[1].Weapon); // someone picks up the MG
    }

    [Fact]
    public void ABritishTeamWithNobodyLeftIsGoneButTheGermansRebuild()
    {
        var c = new Campaign(Data, Side.Allied);
        c.Record(Fight(c, Data.Find("viskiskoia").German.Options.Single(o => o.Name == "2. Gruppe").MenNames));
        var rebuilt = c.Current.German.Options.Single(o => o.Name == "2. Gruppe");
        Assert.Equal(7, rebuilt.MenCount);
        Assert.All(rebuilt.MenExperience!, e => Assert.Equal(Campaign.ReplacementExperience, e));

        var pothus = Data.Find("pothus").Allied;
        c.Record(Fight(c, pothus.Options.Single(o => o.Name == "1 Section").MenNames
            .Concat(pothus.Options.Single(o => o.Name == "Platoon HQ").MenNames).ToArray()));
        var rognan = c.Current.Allied;
        Assert.DoesNotContain(rognan.Options, o => o.Name == "1 Section");
        // The rearguard HQ is a different team and comes at full strength
        Assert.Equal(4, rognan.Options.Single(o => o.Name == "Rearguard HQ").MenCount);
    }

    [Fact]
    public void TheWinnerGetsMorePointsForTheNextBattle()
    {
        var c = new Campaign(Data, Side.Allied);
        var sim = Fight(c); // a ceasefire at once leaves every flag British
        Assert.Equal(Side.Allied, sim.Result!.Winner);
        c.Record(sim);
        Assert.Equal((int)System.MathF.Round(Data.Find("pothus").Allied.Budget * 1.15f), c.Current.Allied.Budget);
        Assert.Equal(Data.Find("pothus").German.Budget, c.Current.German.Budget);
    }

    [Fact]
    public void TheComputerSpendsPointsLeftOverOnReserves()
    {
        var german = Data.Find("pothus").German;
        Assert.DoesNotContain("7. Gruppe", german.AiPicks());
        var richer = german with { Budget = german.Budget + 85 };
        Assert.Contains("7. Gruppe", richer.AiPicks());
        Assert.Null(richer.Problem(richer.AiPicks()));
    }

    [Fact]
    public void AnOperationCanBeSavedAndContinued()
    {
        var c = new Campaign(Data, Side.German);
        c.Record(Fight(c, "Gdsm Hunter", "Gdsm Watt"));
        var again = Campaign.Load(Data, c.Save());
        Assert.Equal(Side.German, again.PlayerSide);
        Assert.Equal(1, again.Index);
        Assert.Equal(c.Results[0], again.Results[0]);
        Assert.Equal(c.Current.German.Options.Select(o => o.MenCount), again.Current.German.Options.Select(o => o.MenCount));
    }

    [Fact]
    public void TheEnemyRefusesAnEarlyCeasefireFromTheSideAhead()
    {
        var sc = Data.Find("rognan");
        var sim = sc.Build(ScenarioTests.LoadMap(sc.Map), 1, Side.Allied);
        Assert.NotNull(sim.RequestCeasefire(Side.Allied)); // British hold every flag at the start
        Assert.False(sim.IsOver);
        Assert.Null(sim.RequestCeasefire(Side.German));    // the Germans are behind, so the British accept
        Assert.True(sim.IsOver);
    }
}
