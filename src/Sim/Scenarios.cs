using System.Numerics;

namespace Saltdal.Sim;

/// <summary>Battles that can be played. Forces are placeholders until the order of battle is done.</summary>
public static class Scenarios
{
    /// <summary>
    /// Pothus, 25 May 1940: a platoon of the Irish Guards holds the crossing;
    /// a German mountain company's leading platoons attack from the south (the AI). Names are invented.
    /// </summary>
    public static Simulation PothusDayOne(MapData map, ulong seed = 1)
    {
        var sim = new Simulation(map, seed)
        {
            TimeLimit = 45 * 60,
            AlliedRetreatY = 0,
            GermanRetreatY = map.WorldHeight,
        };

        sim.Flags.Add(new VictoryFlag("Pothus bridge", map.NearestOpenGround(0.445f, 0.40f), Side.Allied, value: 2));
        sim.Flags.Add(new VictoryFlag("Farm", map.NearestOpenGround(0.62f, 0.33f), Side.Allied));
        sim.Flags.Add(new VictoryFlag("West bank", map.NearestOpenGround(0.37f, 0.36f), Side.Allied));

        const float guards = 0.55f;
        Section(sim, "1 Section", map.NearestOpenGround(0.37f, 0.38f), guards,
            "Cpl Byrne", "LCpl Walsh", "Gdsm Doyle", "Gdsm Ryan", "Gdsm Kelly", "Gdsm Nolan", "Gdsm Quinn");
        Section(sim, "2 Section", map.NearestOpenGround(0.48f, 0.41f), guards,
            "Cpl Moran", "LCpl Flynn", "Gdsm Hayes", "Gdsm Lynch", "Gdsm Daly", "Gdsm Power", "Gdsm Casey");
        Section(sim, "3 Section", map.NearestOpenGround(0.62f, 0.35f), guards,
            "Cpl Duffy", "LCpl Keane", "Gdsm Brady", "Gdsm Murray", "Gdsm Foley", "Gdsm Healy", "Gdsm Byrne");
        var hq = sim.AddTeam("Platoon HQ", Side.Allied);
        hq.IsHeadquarters = true;
        var hqAt = map.NearestOpenGround(0.50f, 0.33f);
        sim.AddSoldier(hq, "Lt O'Neill", hqAt, Weapon.LeeEnfield, leader: true, experience: 0.5f);
        sim.AddSoldier(hq, "Sgt Gallagher", hqAt + new Vector2(4, 0), Weapon.LeeEnfield, leader: true, experience: 0.7f);
        sim.AddSoldier(hq, "Gdsm Tierney", hqAt + new Vector2(-4, 2), Weapon.LeeEnfield, experience: guards);
        sim.AddSoldier(hq, "Gdsm Rooney", hqAt + new Vector2(0, 4), Weapon.Mortar2Inch, experience: guards);

        // Norwegian mortars on the height west of the road gave the crossfire
        // that held the ridge on the first day
        var norw = sim.AddTeam("Norw. mortars", Side.Allied);
        var norwAt = map.NearestOpenGround(0.31f, 0.34f);
        sim.AddSoldier(norw, "Sersjant Hansen", norwAt, Weapon.Krag, leader: true, experience: 0.5f);
        sim.AddSoldier(norw, "Korporal Olsen", norwAt + new Vector2(3, 0), Weapon.Mortar81Norwegian, experience: 0.5f);
        sim.AddSoldier(norw, "Soldat Johansen", norwAt + new Vector2(-3, 2), Weapon.Krag, experience: 0.4f);
        sim.AddSoldier(norw, "Soldat Pedersen", norwAt + new Vector2(0, 4), Weapon.Krag, experience: 0.4f);

        // A troop of 25-pounders from 203 Battery, with poor communications
        sim.Support.Add(new SupportAsset("25-pounder troop", Side.Allied, Explosive.Gun25Pounder,
            missions: 2, rounds: 8, delay: 50f, interval: 2f, spread: 40f, reliability: 0.6f));

        foreach (var t in sim.Teams)
            sim.OrderDefend(t, t.Center + new Vector2(0, 200));

        const float jaeger = 0.6f;
        Gruppe(sim, "1. Gruppe", map.NearestOpenGround(0.36f, 0.93f), jaeger,
            "Uffz. Huber", "Gefr. Mayr", "Jg. Gruber", "Jg. Hofer", "Jg. Wimmer", "Jg. Brandl", "Jg. Lechner", "Jg. Stadler", "Jg. Moser");
        Gruppe(sim, "2. Gruppe", map.NearestOpenGround(0.52f, 0.95f), jaeger,
            "Uffz. Pichler", "Gefr. Aigner", "Jg. Reiter", "Jg. Fuchs", "Jg. Bauer", "Jg. Egger", "Jg. Haas", "Jg. Kofler", "Jg. Leitner");
        Gruppe(sim, "3. Gruppe", map.NearestOpenGround(0.64f, 0.93f), jaeger,
            "Uffz. Weber", "Gefr. Steiner", "Jg. Berger", "Jg. Wagner", "Jg. Schwarz", "Jg. Winkler", "Jg. Maier", "Jg. Lang", "Jg. Koller");

        Gruppe(sim, "4. Gruppe", map.NearestOpenGround(0.46f, 0.98f), jaeger,
            "Uffz. Hofmann", "Gefr. Brunner", "Jg. Pfeifer", "Jg. Rainer", "Jg. Strobl", "Jg. Unger", "Jg. Wolf", "Jg. Zach", "Jg. Ortner");
        Gruppe(sim, "5. Gruppe", map.NearestOpenGround(0.58f, 0.98f), jaeger,
            "Uffz. Graf", "Gefr. Lindner", "Jg. Hackl", "Jg. Kern", "Jg. Pirker", "Jg. Rauch", "Jg. Sommer", "Jg. Thaler", "Jg. Vogl");

        var grw = sim.AddTeam("Granatwerfer", Side.German);
        var grwAt = map.NearestOpenGround(0.55f, 0.99f);
        sim.AddSoldier(grw, "Uffz. Eder", grwAt, Weapon.Kar98k, leader: true, experience: jaeger);
        sim.AddSoldier(grw, "Gefr. Fink", grwAt + new Vector2(3, 0), Weapon.Grw34, experience: jaeger);
        sim.AddSoldier(grw, "Jg. Huemer", grwAt + new Vector2(-3, 2), Weapon.Kar98k, experience: jaeger);

        // The mountain artillery battery and the Luftwaffe, both off the map
        sim.Support.Add(new SupportAsset("Mountain guns", Side.German, Explosive.MountainGun75,
            missions: 2, rounds: 6, delay: 45f, interval: 2.5f, spread: 35f, reliability: 0.9f));
        sim.Support.Add(new SupportAsset("Ju 87 Stuka", Side.German, Explosive.Bomb250Kg,
            missions: 1, rounds: 1, delay: 60f, interval: 0f, spread: 15f, reliability: 0.8f, isAircraft: true));

        sim.Commanders.Add(new AttackAI(Side.German));
        return sim;
    }

    /// <summary>A British rifle section: the corporal, a Bren gunner, and riflemen.</summary>
    static void Section(Simulation sim, string name, Vector2 at, float experience, params string[] men)
    {
        var team = sim.AddTeam(name, Side.Allied);
        for (int i = 0; i < men.Length; i++)
        {
            var weapon = i == 1 ? Weapon.Bren : Weapon.LeeEnfield;
            sim.AddSoldier(team, men[i], at + Simulation.FormationOffset(i, men.Length), weapon, leader: i == 0, experience);
        }
    }

    /// <summary>A German rifle squad: the squad leader with an MP 40, the MG 34 gunner, and riflemen.</summary>
    static void Gruppe(Simulation sim, string name, Vector2 at, float experience, params string[] men)
    {
        var team = sim.AddTeam(name, Side.German);
        for (int i = 0; i < men.Length; i++)
        {
            var weapon = i == 0 ? Weapon.Mp40 : i == 1 ? Weapon.Mg34 : Weapon.Kar98k;
            sim.AddSoldier(team, men[i], at + Simulation.FormationOffset(i, men.Length), weapon, leader: i == 0, experience);
        }
    }
}
