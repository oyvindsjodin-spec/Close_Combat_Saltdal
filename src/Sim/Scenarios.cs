using System;
using System.Collections.Generic;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>A battle that can be picked from the menu.</summary>
/// <param name="Map">Folder name under data/maps.</param>
/// <param name="Build">Sets the battle up on its map. The player commands playerSide and the
/// computer the other; null lets the computer fight both sides.</param>
public sealed record Scenario(
    string Id, string Title, string Map, string Briefing, string AlliedRole, string GermanRole,
    Func<MapData, ulong, Side?, Simulation> Build);

/// <summary>
/// The battles, in the order they were fought. Forces are a platoon or company
/// a side, scaled down from the real battalions. Soldiers' names are invented.
/// Positions are fractions of the map: (0, 0) is the north-west corner.
/// </summary>
public static class Scenarios
{
    public static IReadOnlyList<Scenario> All { get; } = new[]
    {
        new Scenario("viskiskoia", "Viskiskoia, 23 May 1940", "viskiskoia",
            "The Germans have crossed Saltfjellet. In the open birch country of the upper valley a " +
            "company of the Scots Guards, with men of No. 2 Independent Company on the far bank, " +
            "holds the road bridge at Viskiskoia. Hold the bridge and the road behind it for 40 minutes; " +
            "the bridge counts double.",
            "Defend. Three Scots Guards sections, HQ, a 3-inch mortar and two Independent Company " +
            "sections on the east bank. No artillery.",
            "Attack. Five squads, the Zugtrupp and a mortar. The Zugtrupp can call the mountain guns " +
            "and a Stuka. The river can be waded at a gravel bar downstream.",
            Viskiskoia),
        new Scenario("pothus", "Pothus, 25 May 1940", "pothus",
            "Two days later and 25 km further north. At Pothus, where a side valley joins Saltdalselva, " +
            "the Irish Guards and a Norwegian mortar team hold the line. " +
            "The road bridge is already blown; the British hold the west bank, the ridge between the " +
            "valleys and Pothus farm. Hold the flags for an hour; the bridge and the ridge count double.",
            "Defend. Three Irish Guards sections, platoon HQ and the Norwegian 81 mm mortar. The HQ can call a troop of 25-pounders twice, if the signal gets through.",
            "Attack. Six squads with MG 34s, the Zugtrupp and a mortar. The Zugtrupp can call the " +
            "mountain guns twice and one Stuka. The river can be waded at two gravel bars.",
            PothusDayOne),
        new Scenario("rognan", "Rognan, 26 May 1940", "rognan",
            "The road ends at Rognan, at the head of Saltdalsfjorden. While ferries and puffers carry " +
            "the brigade across the fjord, a rearguard holds the village and the quay. Hold the quay " +
            "for 30 minutes and the last boats get away; the quay counts double.",
            "Defend. Two Irish Guards sections, HQ, and two Independent Company sections in the " +
            "houses. One last 25-pounder mission.",
            "Attack. Five squads, the Zugtrupp and a mortar, with the mountain guns and a Stuka. " +
            "Take the quay before the boats leave.",
            Rognan),
    };

    public static Scenario Find(string id) => Array.Find((Scenario[])All, s => s.Id == id)
        ?? throw new ArgumentException($"No battle called {id}");

    // ---------------------------------------------------------------- Pothus

    /// <summary>
    /// Pothus, 25 May 1940. The blown road bridge is in front of the British, who
    /// hold the west bank, the ridge between the two valleys and Pothus farm.
    /// The Germans come up the side valley and the main valley from the south.
    /// </summary>
    public static Simulation PothusDayOne(MapData map, ulong seed = 1, Side? playerSide = Side.Allied)
    {
        var sim = NewBattle(map, seed, "Pothus, 25 May 1940", minutes: 60);
        Vector2 At(float x, float y) => map.NearestOpenGround(x, y);

        sim.Flags.Add(new VictoryFlag("Pothus bridge", At(0.44f, 0.17f), Side.Allied, value: 2));
        sim.Flags.Add(new VictoryFlag("Røkland", At(0.24f, 0.13f), Side.Allied));
        sim.Flags.Add(new VictoryFlag("The ridge", At(0.45f, 0.31f), Side.Allied, value: 2));
        sim.Flags.Add(new VictoryFlag("Pothus farm", At(0.67f, 0.28f), Side.Allied));

        const float guards = 0.55f;
        Section(sim, "1 Section", At(0.45f, 0.33f), guards,
            "Cpl Byrne", "LCpl Walsh", "Gdsm Doyle", "Gdsm Ryan", "Gdsm Kelly", "Gdsm Nolan", "Gdsm Quinn");
        Section(sim, "2 Section", At(0.42f, 0.19f), guards,
            "Cpl Moran", "LCpl Flynn", "Gdsm Hayes", "Gdsm Lynch", "Gdsm Daly", "Gdsm Power", "Gdsm Casey");
        Section(sim, "3 Section", At(0.66f, 0.30f), guards,
            "Cpl Duffy", "LCpl Keane", "Gdsm Brady", "Gdsm Murray", "Gdsm Foley", "Gdsm Healy", "Gdsm Byrne");
        Headquarters(sim, "Platoon HQ", At(0.36f, 0.17f), "Lt O'Neill", "Sgt Gallagher", "Gdsm Tierney", "Gdsm Rooney", guards);
        NorwegianMortars(sim, At(0.27f, 0.24f));
        sim.Support.Add(TwentyFivePounders(missions: 2));
        foreach (var t in sim.Teams)
            sim.OrderDefend(t, t.Center + new Vector2(0, 200));

        GermanCompany(sim, At,
            (0.47f, 0.80f), (0.50f, 0.74f), (0.62f, 0.76f), (0.55f, 0.83f), (0.68f, 0.80f),
            zugtrupp: (0.55f, 0.86f), mortar: (0.60f, 0.88f), g6: (0.74f, 0.84f));
        GermanSupport(sim, gunMissions: 2);
        AddCommanders(sim, playerSide);
        return sim;
    }

    // ---------------------------------------------------------------- Viskiskoia

    /// <summary>
    /// Viskiskoia, 23 May 1940. The Scots Guards hold the bridge on the west bank;
    /// an Independent Company covers the east bank and is the first to be hit.
    /// </summary>
    public static Simulation Viskiskoia(MapData map, ulong seed = 1, Side? playerSide = Side.Allied)
    {
        var sim = NewBattle(map, seed, "Viskiskoia, 23 May 1940", minutes: 40);
        Vector2 At(float x, float y) => map.NearestOpenGround(x, y);

        sim.Flags.Add(new VictoryFlag("Viskiskoia bridge", At(0.30f, 0.50f), Side.Allied, value: 2));
        sim.Flags.Add(new VictoryFlag("The hut", At(0.50f, 0.41f), Side.Allied));
        sim.Flags.Add(new VictoryFlag("Road north", At(0.27f, 0.25f), Side.Allied));

        const float guards = 0.6f;
        Section(sim, "7 Section", At(0.29f, 0.47f), guards,
            "Cpl Grant", "LCpl Reid", "Gdsm Hunter", "Gdsm Watt", "Gdsm Baird", "Gdsm Kerr", "Gdsm Muir");
        Section(sim, "8 Section", At(0.24f, 0.40f), guards,
            "Cpl Duncan", "LCpl Boyd", "Gdsm Hay", "Gdsm Ogilvie", "Gdsm Craig", "Gdsm Fyfe", "Gdsm Lindsay");
        Section(sim, "9 Section", At(0.26f, 0.27f), guards,
            "Cpl Paterson", "LCpl Allan", "Gdsm Moir", "Gdsm Rennie", "Gdsm Imrie", "Gdsm Laing", "Gdsm Doig");
        IndependentSection(sim, "No. 2 Ind. Coy (1)", At(0.50f, 0.44f),
            "Sgt Cameron", "Pte Robertson", "Pte Stewart", "Pte Morrison", "Pte Macleod", "Pte Campbell");
        IndependentSection(sim, "No. 2 Ind. Coy (2)", At(0.68f, 0.40f),
            "Cpl MacDonald", "Pte Fraser", "Pte Murray", "Pte Maclean", "Pte Kennedy", "Pte Bell");
        Headquarters(sim, "Company HQ", At(0.22f, 0.33f), "Capt Lindsay-Hogg", "CSM Gibb", "Gdsm Rae", "Gdsm Aird", guards);
        var mortar = sim.AddTeam("3-inch mortar", Side.Allied);
        var mAt = At(0.18f, 0.22f);
        sim.AddSoldier(mortar, "Sgt Bruce", mAt, Weapon.LeeEnfield, leader: true, experience: guards);
        sim.AddSoldier(mortar, "Gdsm Christie", mAt + new Vector2(3, 0), Weapon.Mortar3Inch, experience: guards);
        sim.AddSoldier(mortar, "Gdsm Ure", mAt + new Vector2(-3, 2), Weapon.LeeEnfield, experience: guards);
        foreach (var t in sim.Teams)
            sim.OrderDefend(t, t.Center + new Vector2(0, 200));

        GermanCompany(sim, At,
            (0.45f, 0.95f), (0.58f, 0.94f), (0.70f, 0.95f), (0.52f, 0.98f), (0.64f, 0.98f),
            zugtrupp: (0.58f, 0.99f), mortar: (0.62f, 0.99f));
        GermanSupport(sim, gunMissions: 2);
        AddCommanders(sim, playerSide);
        return sim;
    }

    // ---------------------------------------------------------------- Rognan

    /// <summary>
    /// Rognan, 26 May 1940. A rearguard in the village covers the quay while the
    /// last boats load; the Germans come down the valley from the south-west.
    /// </summary>
    public static Simulation Rognan(MapData map, ulong seed = 1, Side? playerSide = Side.Allied)
    {
        var sim = NewBattle(map, seed, "Rognan, 26 May 1940", minutes: 30);
        Vector2 At(float x, float y) => map.NearestOpenGround(x, y);

        sim.Flags.Add(new VictoryFlag("The quay", At(0.56f, 0.15f), Side.Allied, value: 2));
        sim.Flags.Add(new VictoryFlag("Rognan village", At(0.50f, 0.36f), Side.Allied));
        sim.Flags.Add(new VictoryFlag("Nygård", At(0.31f, 0.50f), Side.Allied));

        const float guards = 0.55f;
        Section(sim, "1 Section", At(0.30f, 0.47f), guards,
            "Cpl Byrne", "LCpl Walsh", "Gdsm Doyle", "Gdsm Ryan", "Gdsm Kelly", "Gdsm Nolan");
        Section(sim, "2 Section", At(0.47f, 0.42f), guards,
            "Cpl Moran", "LCpl Flynn", "Gdsm Hayes", "Gdsm Lynch", "Gdsm Daly", "Gdsm Power");
        IndependentSection(sim, "No. 3 Ind. Coy (1)", At(0.53f, 0.38f),
            "Sgt Fraser", "Pte Munro", "Pte Ross", "Pte Gunn", "Pte Mackay", "Pte Sutherland");
        IndependentSection(sim, "No. 3 Ind. Coy (2)", At(0.44f, 0.33f),
            "Cpl Grant", "Pte Bain", "Pte Mackenzie", "Pte Urquhart", "Pte Matheson", "Pte Dunbar");
        Headquarters(sim, "Rearguard HQ", At(0.53f, 0.22f), "Capt Reynolds", "Sgt Gallagher", "Gdsm Tierney", "Gdsm Rooney", guards);
        sim.Support.Add(TwentyFivePounders(missions: 1));
        foreach (var t in sim.Teams)
            sim.OrderDefend(t, t.Center + new Vector2(-100, 200));

        GermanCompany(sim, At,
            (0.12f, 0.95f), (0.22f, 0.96f), (0.32f, 0.97f), (0.17f, 0.99f), (0.40f, 0.98f),
            zugtrupp: (0.25f, 0.99f), mortar: (0.30f, 0.99f));
        GermanSupport(sim, gunMissions: 2);
        AddCommanders(sim, playerSide);
        return sim;
    }

    // ---------------------------------------------------------------- building blocks

    static Simulation NewBattle(MapData map, ulong seed, string title, int minutes) => new(map, seed)
    {
        Title = title,
        TimeLimit = minutes * 60,
        AlliedRetreatY = 0,
        GermanRetreatY = map.WorldHeight,
    };

    static void AddCommanders(Simulation sim, Side? playerSide)
    {
        if (playerSide != Side.Allied) sim.Commanders.Add(new DefendAI(Side.Allied));
        if (playerSide != Side.German) sim.Commanders.Add(new AttackAI(Side.German));
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

    /// <summary>
    /// An Independent Company section: keen but newly raised and lightly armed,
    /// one Bren between them.
    /// </summary>
    static void IndependentSection(Simulation sim, string name, Vector2 at, params string[] men)
    {
        var team = sim.AddTeam(name, Side.Allied);
        for (int i = 0; i < men.Length; i++)
        {
            var weapon = i == 1 ? Weapon.Bren : Weapon.LeeEnfield;
            sim.AddSoldier(team, men[i], at + Simulation.FormationOffset(i, men.Length), weapon, leader: i == 0, experience: 0.45f);
        }
    }

    /// <summary>The officer, his sergeant, a runner and the 2-inch mortar man. It can call the guns.</summary>
    static void Headquarters(Simulation sim, string name, Vector2 at, string officer, string sergeant,
        string runner, string mortarMan, float experience)
    {
        var hq = sim.AddTeam(name, Side.Allied);
        hq.IsHeadquarters = true;
        sim.AddSoldier(hq, officer, at, Weapon.LeeEnfield, leader: true, experience: 0.5f);
        sim.AddSoldier(hq, sergeant, at + new Vector2(4, 0), Weapon.LeeEnfield, leader: true, experience: 0.7f);
        sim.AddSoldier(hq, runner, at + new Vector2(-4, 2), Weapon.LeeEnfield, experience: experience);
        sim.AddSoldier(hq, mortarMan, at + new Vector2(0, 4), Weapon.Mortar2Inch, experience: experience);
    }

    /// <summary>Norwegian mortars on the height west of the road gave the crossfire that held on the first day.</summary>
    static void NorwegianMortars(Simulation sim, Vector2 at)
    {
        var norw = sim.AddTeam("Norw. mortars", Side.Allied);
        sim.AddSoldier(norw, "Sersjant Hansen", at, Weapon.Krag, leader: true, experience: 0.5f);
        sim.AddSoldier(norw, "Korporal Olsen", at + new Vector2(3, 0), Weapon.Mortar81Norwegian, experience: 0.5f);
        sim.AddSoldier(norw, "Soldat Johansen", at + new Vector2(-3, 2), Weapon.Krag, experience: 0.4f);
        sim.AddSoldier(norw, "Soldat Pedersen", at + new Vector2(0, 4), Weapon.Krag, experience: 0.4f);
    }

    /// <summary>A troop of 25-pounders from 203 Battery, with poor communications.</summary>
    static SupportAsset TwentyFivePounders(int missions) => new("25-pounder troop", Side.Allied, Explosive.Gun25Pounder,
        missions: missions, rounds: 8, delay: 50f, interval: 2f, spread: 40f, reliability: 0.6f);

    /// <summary>The mountain artillery battery and the Luftwaffe, both off the map.</summary>
    static void GermanSupport(Simulation sim, int gunMissions)
    {
        sim.Support.Add(new SupportAsset("Mountain guns", Side.German, Explosive.MountainGun75,
            missions: gunMissions, rounds: 6, delay: 45f, interval: 2.5f, spread: 35f, reliability: 0.9f));
        sim.Support.Add(new SupportAsset("Ju 87 Stuka", Side.German, Explosive.Bomb250Kg,
            missions: 1, rounds: 1, delay: 60f, interval: 0f, spread: 15f, reliability: 0.8f, isAircraft: true));
    }

    static readonly string[][] GruppeNames =
    {
        new[] { "Uffz. Huber", "Gefr. Mayr", "Jg. Gruber", "Jg. Hofer", "Jg. Wimmer", "Jg. Brandl", "Jg. Lechner", "Jg. Stadler", "Jg. Moser" },
        new[] { "Uffz. Pichler", "Gefr. Aigner", "Jg. Reiter", "Jg. Fuchs", "Jg. Bauer", "Jg. Egger", "Jg. Haas", "Jg. Kofler", "Jg. Leitner" },
        new[] { "Uffz. Weber", "Gefr. Steiner", "Jg. Berger", "Jg. Wagner", "Jg. Schwarz", "Jg. Winkler", "Jg. Maier", "Jg. Lang", "Jg. Koller" },
        new[] { "Uffz. Hofmann", "Gefr. Brunner", "Jg. Pfeifer", "Jg. Rainer", "Jg. Strobl", "Jg. Unger", "Jg. Wolf", "Jg. Zach", "Jg. Ortner" },
        new[] { "Uffz. Graf", "Gefr. Lindner", "Jg. Hackl", "Jg. Kern", "Jg. Pirker", "Jg. Rauch", "Jg. Sommer", "Jg. Thaler", "Jg. Vogl" },
        new[] { "Uffz. Kaiser", "Gefr. Moritz", "Jg. Danner", "Jg. Eberl", "Jg. Frank", "Jg. Gasser", "Jg. Holzer", "Jg. Jäger", "Jg. Kirchner" },
    };

    /// <summary>
    /// The leading platoons of a Gebirgsjäger company: five squads, the platoon
    /// HQ (Zugtrupp), which can call the guns, and a mortar team.
    /// </summary>
    static void GermanCompany(Simulation sim, Func<float, float, Vector2> at,
        (float, float) g1, (float, float) g2, (float, float) g3, (float, float) g4, (float, float) g5,
        (float, float) zugtrupp, (float, float) mortar, (float, float)? g6 = null)
    {
        const float jaeger = 0.6f;
        var spots = g6 == null ? new[] { g1, g2, g3, g4, g5 } : new[] { g1, g2, g3, g4, g5, g6.Value };
        for (int i = 0; i < spots.Length; i++)
            Gruppe(sim, $"{i + 1}. Gruppe", at(spots[i].Item1, spots[i].Item2), jaeger, GruppeNames[i]);

        var zug = sim.AddTeam("Zugtrupp", Side.German);
        zug.IsHeadquarters = true;
        var zugAt = at(zugtrupp.Item1, zugtrupp.Item2);
        sim.AddSoldier(zug, "Lt. Kranz", zugAt, Weapon.Mp40, leader: true, experience: 0.65f);
        sim.AddSoldier(zug, "Ofw. Riedl", zugAt + new Vector2(4, 0), Weapon.Mp40, leader: true, experience: 0.7f);
        sim.AddSoldier(zug, "Gefr. Ebner", zugAt + new Vector2(-4, 2), Weapon.Kar98k, experience: jaeger);

        var grw = sim.AddTeam("Granatwerfer", Side.German);
        var grwAt = at(mortar.Item1, mortar.Item2);
        sim.AddSoldier(grw, "Uffz. Eder", grwAt, Weapon.Kar98k, leader: true, experience: jaeger);
        sim.AddSoldier(grw, "Gefr. Fink", grwAt + new Vector2(3, 0), Weapon.Grw34, experience: jaeger);
        sim.AddSoldier(grw, "Jg. Huemer", grwAt + new Vector2(-3, 2), Weapon.Kar98k, experience: jaeger);
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
