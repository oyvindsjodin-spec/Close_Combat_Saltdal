using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Saltdal.Sim;

/// <summary>
/// Everything the game reads from the data folder: weapons and explosives
/// (weapons.json), the kinds of team and off-map support (units.json), and the
/// battles (battles/*.json). The files are plain text so numbers can be changed
/// without touching the code.
/// </summary>
public sealed class GameData
{
    public IReadOnlyDictionary<string, Explosive> Explosives { get; }
    public IReadOnlyDictionary<string, Weapon> Weapons { get; }
    public IReadOnlyDictionary<string, TeamType> TeamTypes { get; }
    public IReadOnlyDictionary<string, SupportType> SupportTypes { get; }
    /// <summary>The battles in the order they were fought.</summary>
    public IReadOnlyList<Scenario> Battles { get; }

    GameData(Dictionary<string, Explosive> explosives, Dictionary<string, Weapon> weapons,
        Dictionary<string, TeamType> teams, Dictionary<string, SupportType> support, List<Scenario> battles)
    {
        Explosives = explosives;
        Weapons = weapons;
        TeamTypes = teams;
        SupportTypes = support;
        Battles = battles;
    }

    public Scenario Find(string id) => Battles.FirstOrDefault(b => b.Id == id)
        ?? throw new ArgumentException($"No battle called {id}");

    /// <summary>Reads a data folder from disk (for tests and tools; the game reads through Godot).</summary>
    public static GameData LoadFolder(string folder) => Load(
        File.ReadAllText(Path.Combine(folder, "weapons.json")),
        File.ReadAllText(Path.Combine(folder, "units.json")),
        Directory.GetFiles(Path.Combine(folder, "battles"), "*.json").OrderBy(f => f)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f))));

    /// <summary>Builds the game data from the text of the files. Mistakes in a file are reported with its name.</summary>
    public static GameData Load(string weaponsJson, string unitsJson, IEnumerable<(string File, string Json)> battleFiles)
    {
        var w = Parse("weapons.json", weaponsJson);
        var explosives = new Dictionary<string, Explosive>();
        foreach (var (id, e) in w.Section("explosives"))
            explosives[id] = new Explosive(e.Str("name"), e.Num("killRadius"), e.Num("woundRadius"),
                e.Num("suppressionRadius"), e.Num("suppression"));

        var weapons = new Dictionary<string, Weapon>();
        foreach (var (id, e) in w.Section("weapons"))
        {
            Explosive? shell = null;
            if (e.Has("shell")) shell = e.Lookup("shell", explosives, "explosive");
            weapons[id] = new Weapon(e.Str("name"), e.Num("range"), (int)e.Num("burst", 1), e.Num("roundInterval", 0),
                e.Num("aimTime"), e.Num("accuracy", 0), e.Num("suppression", 0), e.Num("suppressionRadius", 0),
                (int)e.Num("ammo"), shell, e.Num("minRange", 0));
        }

        var u = Parse("units.json", unitsJson);
        var teams = new Dictionary<string, TeamType>();
        foreach (var (id, e) in u.Section("teams"))
        {
            var men = new List<MemberSpec>();
            foreach (var m in e.List("men"))
            {
                var spec = new MemberSpec(m.Lookup("weapon", weapons, "weapon"), m.Bool("leader"),
                    m.Has("experience") ? m.Num("experience") : null);
                for (int i = 0; i < (int)m.Num("count", 1); i++) men.Add(spec);
            }
            teams[id] = new TeamType(id, e.Str("name"), e.Str("nation"), e.SideOf("side"), (int)e.Num("cost"),
                e.Num("experience", 0.5f), e.Bool("headquarters"), e.Str("about", ""), men);
        }
        var support = new Dictionary<string, SupportType>();
        foreach (var (id, e) in u.Section("support"))
            support[id] = new SupportType(id, e.Str("name"), e.SideOf("side"), (int)e.Num("costPerMission"),
                e.Lookup("explosive", explosives, "explosive"), (int)e.Num("rounds"), e.Num("delay"), e.Num("interval"),
                e.Num("spread"), e.Num("reliability", 1), e.Bool("aircraft"), e.Str("about", ""));

        var battles = new List<Scenario>();
        foreach (var (file, json) in battleFiles)
            battles.Add(ReadBattle(Parse(file, json), teams, support));
        battles.Sort((a, b) => a.Order.CompareTo(b.Order));
        return new GameData(explosives, weapons, teams, support, battles);
    }

    static Scenario ReadBattle(Node b, Dictionary<string, TeamType> teams, Dictionary<string, SupportType> support)
    {
        var flags = b.List("flags").Select(f => new FlagSpec(f.Str("name"), f.Point("at"), (int)f.Num("value", 1),
            f.Has("owner") ? f.SideOf("owner") : Side.Allied)).ToList();
        SideForces ReadSide(Side side, string key)
        {
            var s = b.Child(key);
            var options = new List<ForceOption>();
            foreach (var t in s.List("units"))
            {
                var type = t.Lookup("type", teams, "team type");
                if (type.Side != side) throw t.Error($"{type.Id} is a {type.Side} team");
                var names = t.Has("men") ? t.Strings("men") : Array.Empty<string>();
                int men = names.Length > 0 ? Math.Min(names.Length, type.Men.Count) : type.Men.Count;
                options.Add(new ForceOption(t.Str("name"), type, null, 0, t.Point("at"),
                    t.Has("experience") ? t.Num("experience") : null, names, men,
                    t.Bool("default", true), t.Bool("required")));
            }
            if (s.Has("support"))
                foreach (var t in s.List("support"))
                {
                    var type = t.Lookup("type", support, "support type");
                    if (type.Side != side) throw t.Error($"{type.Id} is {type.Side} support");
                    options.Add(new ForceOption(t.Str("name", type.Name), null, type, (int)t.Num("missions", 1),
                        Vector2.Zero, null, Array.Empty<string>(), 0, t.Bool("default", true), t.Bool("required")));
                }
            var dup = options.GroupBy(o => o.Name).FirstOrDefault(g => g.Count() > 1);
            if (dup != null) throw s.Error($"two choices are called {dup.Key}; names must differ");
            int budget = s.Has("budget") ? (int)s.Num("budget") : options.Where(o => o.Default).Sum(o => o.Cost);
            return new SideForces(side, budget, s.Has("face") ? s.Point("face") : null, options);
        }
        return new Scenario(b.Str("id"), (int)b.Num("order", 0), b.Str("title"), b.Str("map"), (int)b.Num("minutes"),
            b.Str("briefing"), b.Str("alliedRole"), b.Str("germanRole"), flags,
            ReadSide(Side.Allied, "allied"), ReadSide(Side.German, "german"), (int)b.Num("prepMinutes", 0));
    }

    static Node Parse(string file, string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return new Node(file, "", doc.RootElement);
        }
        catch (JsonException e)
        {
            throw new GameDataException($"{file}: not valid JSON near line {e.LineNumber + 1}: {e.Message}");
        }
    }

    /// <summary>A place in a data file, so a mistake can say where it is.</summary>
    readonly record struct Node(string File, string Path, JsonElement E)
    {
        public GameDataException Error(string msg) => new($"{File}{(Path == "" ? "" : " at " + Path)}: {msg}");
        public bool Has(string key) => E.ValueKind == JsonValueKind.Object && E.TryGetProperty(key, out _);
        public Node Child(string key) => E.TryGetProperty(key, out var v) ? new(File, Join(key), v) : throw Error($"\"{key}\" is missing");
        string Join(string key) => Path == "" ? key : $"{Path}.{key}";

        public IEnumerable<(string Id, Node Node)> Section(string key)
        {
            var c = Child(key);
            foreach (var p in c.E.EnumerateObject())
                if (!p.Name.StartsWith('_'))
                    yield return (p.Name, new Node(File, c.Join(p.Name), p.Value));
        }

        public IEnumerable<Node> List(string key)
        {
            var c = Child(key);
            if (c.E.ValueKind != JsonValueKind.Array) throw c.Error("should be a list in [ ]");
            int i = 0;
            foreach (var v in c.E.EnumerateArray())
                yield return new Node(File, $"{c.Path}[{i++}]", v);
        }

        public string Str(string key, string? fallback = null)
        {
            if (!Has(key)) return fallback ?? throw Error($"\"{key}\" is missing");
            var v = Child(key);
            return v.E.ValueKind == JsonValueKind.String ? v.E.GetString()! : throw v.Error("should be text in quotes");
        }

        public float Num(string key, float? fallback = null)
        {
            if (!Has(key)) return fallback ?? throw Error($"\"{key}\" is missing");
            var v = Child(key);
            return v.E.ValueKind == JsonValueKind.Number ? v.E.GetSingle() : throw v.Error("should be a number");
        }

        public bool Bool(string key, bool fallback = false)
        {
            if (!Has(key)) return fallback;
            var v = Child(key);
            return v.E.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw v.Error("should be true or false"),
            };
        }

        public string[] Strings(string key) => List(key).Select(n =>
            n.E.ValueKind == JsonValueKind.String ? n.E.GetString()! : throw n.Error("should be text in quotes")).ToArray();

        public Vector2 Point(string key)
        {
            var nums = List(key).Select(n => n.E.ValueKind == JsonValueKind.Number ? n.E.GetSingle() : throw n.Error("should be a number")).ToArray();
            return nums.Length == 2 ? new Vector2(nums[0], nums[1]) : throw Child(key).Error("should be two numbers, [x, y]");
        }

        public Side SideOf(string key) => Str(key) switch
        {
            "allied" => Side.Allied,
            "german" => Side.German,
            var s => throw Child(key).Error($"should be \"allied\" or \"german\", not \"{s}\""),
        };

        public T Lookup<T>(string key, Dictionary<string, T> table, string what)
        {
            var id = Str(key);
            return table.TryGetValue(id, out var v) ? v
                : throw Child(key).Error($"no {what} called \"{id}\" (known: {string.Join(", ", table.Keys)})");
        }
    }
}

/// <summary>A mistake in one of the data files.</summary>
public sealed class GameDataException(string message) : Exception(message);

/// <summary>One kind of team, as listed in units.json.</summary>
public sealed record TeamType(string Id, string Name, string Nation, Side Side, int Cost, float Experience,
    bool Headquarters, string About, IReadOnlyList<MemberSpec> Men);

/// <summary>One place in a team: his weapon, whether he leads, and his experience if it differs from the team's.</summary>
public sealed record MemberSpec(Weapon Weapon, bool Leader, float? Experience);

/// <summary>A kind of off-map support, bought per mission.</summary>
public sealed record SupportType(string Id, string Name, Side Side, int CostPerMission, Explosive Explosive,
    int Rounds, float Delay, float Interval, float Spread, float Reliability, bool Aircraft, string About);

public sealed record FlagSpec(string Name, Vector2 At, int Value, Side Owner);

/// <summary>
/// One thing a side can bring to a battle: a team at its starting place, or
/// some missions of off-map support.
/// </summary>
/// <param name="MenExperience">Each man's own experience, for men carried over from an earlier battle.</param>
public sealed record ForceOption(string Name, TeamType? Team, SupportType? Support, int Missions, Vector2 At,
    float? Experience, string[] MenNames, int MenCount, bool Default, bool Required, float[]? MenExperience = null)
{
    public bool IsSupport => Support != null;

    /// <summary>A team short of men costs less in proportion.</summary>
    public int Cost => Team != null
        ? (int)MathF.Round(Team.Cost * MenCount / (float)Team.Men.Count)
        : Support!.CostPerMission * Missions;

    public string Summary => Team != null
        ? $"{Team.Name}, {MenCount} men. {Team.About}"
        : $"{Missions} mission{(Missions == 1 ? "" : "s")}. {Support!.About}";
}

/// <summary>What one side may bring to a battle and how much it may spend.</summary>
public sealed record SideForces(Side Side, int Budget, Vector2? Face, IReadOnlyList<ForceOption> Options)
{
    public IReadOnlyList<string> DefaultPicks => Options.Where(o => o.Default).Select(o => o.Name).ToList();

    /// <summary>
    /// What the computer brings: the historical force, then reserves in the
    /// order listed while points remain (only after earlier losses leave some over).
    /// </summary>
    public IReadOnlyList<string> AiPicks()
    {
        var picks = DefaultPicks.ToList();
        int left = Budget - CostOf(picks);
        foreach (var o in Options.Where(o => !o.Default && !o.IsSupport))
            if (o.Cost <= left)
            {
                picks.Add(o.Name);
                left -= o.Cost;
            }
        return picks;
    }

    public int CostOf(IEnumerable<string> picks) => Options.Where(o => picks.Contains(o.Name)).Sum(o => o.Cost);

    /// <summary>Why a choice of forces is not allowed, or null when it is.</summary>
    public string? Problem(IReadOnlyCollection<string> picks)
    {
        var missing = Options.FirstOrDefault(o => o.Required && !picks.Contains(o.Name));
        if (missing != null) return $"{missing.Name} must come.";
        if (!Options.Any(o => !o.IsSupport && picks.Contains(o.Name))) return "Choose at least one team.";
        int cost = CostOf(picks);
        return cost > Budget ? $"Over budget by {cost - Budget} points." : null;
    }
}

/// <summary>A battle that can be picked from the menu, read from data/battles.</summary>
public sealed record Scenario(string Id, int Order, string Title, string Map, int Minutes,
    string Briefing, string AlliedRole, string GermanRole, IReadOnlyList<FlagSpec> Flags,
    SideForces Allied, SideForces German, int PrepMinutes = 0)
{
    public SideForces Forces(Side side) => side == Side.Allied ? Allied : German;

    /// <summary>
    /// Sets the battle up on its map. The player commands playerSide with the
    /// forces in playerPicks (the historical force if null); the computer takes
    /// the historical force of the other side. A null playerSide lets the
    /// computer fight both sides.
    /// </summary>
    public Simulation Build(MapData map, ulong seed, Side? playerSide, IReadOnlyCollection<string>? playerPicks = null)
    {
        map = map.Copy();
        var sim = new Simulation(map, seed)
        {
            Title = Title,
            TimeLimit = (Minutes + PrepMinutes) * 60,
            PrepUntil = PrepMinutes * 60,
            AlliedRetreatY = 0,
            GermanRetreatY = map.WorldHeight,
        };
        Vector2 At(Vector2 f) => map.NearestOpenGround(f.X, f.Y);
        foreach (var f in Flags)
            sim.Flags.Add(new VictoryFlag(f.Name, At(f.At), f.Owner, value: f.Value));

        foreach (var forces in new[] { Allied, German })
        {
            var picks = forces.Side == playerSide && playerPicks != null ? playerPicks
                : forces.Side == playerSide ? forces.DefaultPicks : forces.AiPicks();
            foreach (var o in forces.Options.Where(o => picks.Contains(o.Name)))
            {
                if (o.Support != null)
                {
                    var s = o.Support;
                    sim.Support.Add(new SupportAsset(s.Name, s.Side, s.Explosive, missions: o.Missions, rounds: s.Rounds,
                        delay: s.Delay, interval: s.Interval, spread: s.Spread, reliability: s.Reliability, isAircraft: s.Aircraft));
                    continue;
                }
                var type = o.Team!;
                var team = sim.AddTeam(o.Name, forces.Side);
                team.IsHeadquarters = type.Headquarters;
                team.Nation = type.Nation;
                var at = At(o.At);
                for (int i = 0; i < o.MenCount; i++)
                {
                    var spec = type.Men[i];
                    string name = i < o.MenNames.Length ? o.MenNames[i] : $"{type.Name} {i + 1}";
                    float exp = o.MenExperience?[i] ?? spec.Experience ?? o.Experience ?? type.Experience;
                    sim.AddSoldier(team, name, at + Simulation.FormationOffset(i, o.MenCount), spec.Weapon, spec.Leader, exp);
                }
                if (forces.Face is { } face)
                    sim.OrderDefend(team, team.Center + face);
            }
        }

        if (playerSide != Side.Allied) sim.Commanders.Add(new DefendAI(Side.Allied));
        if (playerSide != Side.German) sim.Commanders.Add(new AttackAI(Side.German));
        return sim;
    }
}
