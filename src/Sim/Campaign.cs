using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Saltdal.Sim;

/// <summary>
/// The operation of 23 to 26 May 1940: the battles fought one after another.
/// Teams that appear again in a later battle (the same name on the same side)
/// come back with the men who walked away, a little more experienced, and
/// some replacements: the Germans fed in fresh troops all the way, the British
/// had few. A British team with nobody left is gone. The winner of a battle
/// gets extra points to spend before the next one.
/// </summary>
public sealed class Campaign
{
    /// <summary>Experience a man gains for each battle he comes through.</summary>
    public const float ExperiencePerBattle = 0.05f;
    public const float MaxExperience = 0.9f;
    /// <summary>The winner of a battle gets this share more points for the next one.</summary>
    public const float WinnerBonus = 0.15f;
    /// <summary>
    /// Share of a team's losses made good by replacements before the next
    /// battle. The Germans fed fresh troops forward all the way; the British had none to spare.
    /// </summary>
    public static float ReplacementShare(Side side) => side == Side.German ? 0.75f : 0.25f;
    /// <summary>Replacements are fresh from the depot.</summary>
    public const float ReplacementExperience = 0.45f;

    public Side PlayerSide { get; }
    /// <summary>Which battle is next, counting from 0; equal to the number of battles when the operation is over.</summary>
    public int Index { get; private set; }
    public IReadOnlyList<BattleRecord> Results => _results;
    public bool IsOver => Index >= _data.OperationBattles.Count;

    readonly GameData _data;
    readonly List<BattleRecord> _results = new();
    // Key "Allied:1 Section": the men still with the team, in order
    readonly Dictionary<string, List<ManRecord>> _roster = new();

    public Campaign(GameData data, Side playerSide)
    {
        _data = data;
        PlayerSide = playerSide;
    }

    /// <summary>The next battle, with each side's forces as the earlier battles left them.</summary>
    public Scenario Current
    {
        get
        {
            if (IsOver) throw new InvalidOperationException("The operation is over.");
            var sc = _data.OperationBattles[Index];
            return sc with { Allied = Adjust(sc.Allied), German = Adjust(sc.German) };
        }
    }

    SideForces Adjust(SideForces forces)
    {
        var options = new List<ForceOption>();
        foreach (var o in forces.Options)
        {
            if (o.Team == null || !_roster.TryGetValue(Key(forces.Side, o.Name), out var men))
            {
                options.Add(o);
                continue;
            }
            // A headquarters that was wiped out is replaced by a new one
            if (men.Count == 0 && o.Required)
            {
                options.Add(o);
                continue;
            }
            // A British team with nobody left is gone; the Germans rebuild theirs from fresh troops
            if (men.Count == 0 && forces.Side == Side.Allied) continue;
            men = men.Take(o.MenCount).ToList();
            int replacements = (int)MathF.Round((o.MenCount - men.Count) * ReplacementShare(forces.Side));
            for (int i = 0; i < replacements; i++)
                men.Add(new ManRecord(ReplacementName(o, men.Count), ReplacementExperience));
            options.Add(o with
            {
                MenNames = men.Select(m => m.Name).ToArray(),
                MenCount = men.Count,
                MenExperience = men.Select(m => m.Experience).ToArray(),
            });
        }
        var last = _results.LastOrDefault();
        int budget = forces.Budget;
        if (last?.Winner == forces.Side) budget = (int)MathF.Round(budget * (1 + WinnerBonus));
        return forces with { Budget = budget, Options = options };
    }

    /// <summary>Writes down how a finished battle went and moves on to the next.</summary>
    public void Record(Simulation sim)
    {
        if (IsOver) throw new InvalidOperationException("The operation is over.");
        if (sim.Result is not { } result) throw new InvalidOperationException("The battle is not over yet.");
        foreach (var team in sim.Teams)
        {
            // Men who can still walk come back; the dead, the badly wounded and the captured do not
            var men = team.Soldiers
                .Where(s => s.Health <= Health.Wounded && s.Morale != MoraleState.Surrendered)
                .OrderByDescending(s => s.IsLeader)
                .Select(s => new ManRecord(s.Name, MathF.Min(MaxExperience, s.Experience + ExperiencePerBattle)))
                .ToList();
            _roster[Key(team.Side, team.Name)] = men;
        }
        _results.Add(new BattleRecord(_data.OperationBattles[Index].Id, _data.OperationBattles[Index].Title, result.Winner,
            result.Reason, result.AlliedLosses, result.GermanLosses));
        Index++;
    }

    /// <summary>The side that won more battles, or null for a drawn operation.</summary>
    public Side? Winner
    {
        get
        {
            int allied = _results.Count(r => r.Winner == Side.Allied);
            int german = _results.Count(r => r.Winner == Side.German);
            return allied > german ? Side.Allied : german > allied ? Side.German : null;
        }
    }

    static string Key(Side side, string team) => $"{side}:{team}";

    static readonly Dictionary<string, string[]> Surnames = new()
    {
        ["german"] = new[] { "Aschauer", "Bichler", "Dengg", "Eisl", "Fellner", "Gruber", "Hinterberger", "Kogler", "Mairhofer", "Neuhauser", "Oberauer", "Prenner", "Rieder", "Schober", "Trattner", "Wallner", "Zangerl" },
        ["british"] = new[] { "Barry", "Collins", "Dillon", "Egan", "Fallon", "Greene", "Hogan", "Kenny", "Lyons", "Mahon", "Nugent", "O'Brien", "Quigley", "Ryan", "Sheehan", "Ward" },
        ["norwegian"] = new[] { "Berg", "Dahl", "Eide", "Haugen", "Lie", "Moen", "Nilsen", "Strand", "Vik" },
    };

    /// <summary>A name for a new man: the rank of the man he replaces and a surname from home.</summary>
    static string ReplacementName(ForceOption o, int slot)
    {
        var pool = Surnames.TryGetValue(o.Team!.Nation, out var p) ? p : Surnames["british"];
        string template = o.MenNames.Length > 0 ? o.MenNames[Math.Min(slot, o.MenNames.Length - 1)] : "";
        int space = template.IndexOf(' ');
        string rank = space > 0 && slot > 0 ? template[..space] : o.Team.Nation == "german" ? "Jg." : "Pte";
        // A stable choice, so the same team gets the same new names every time
        int pick = (o.Name.Sum(ch => ch) * 31 + slot * 7) % pool.Length;
        return $"{rank} {pool[pick]}";
    }

    // ---------------------------------------------------------------- saving

    sealed record SaveFile(string Side, int Index, List<BattleRecord> Results, Dictionary<string, List<ManRecord>> Roster);

    public string Save() => JsonSerializer.Serialize(
        new SaveFile(PlayerSide.ToString(), Index, _results, _roster),
        new JsonSerializerOptions { WriteIndented = true });

    public static Campaign Load(GameData data, string json)
    {
        var f = JsonSerializer.Deserialize<SaveFile>(json) ?? throw new GameDataException("The saved operation is empty.");
        var c = new Campaign(data, Enum.Parse<Side>(f.Side)) { Index = Math.Min(f.Index, data.OperationBattles.Count) };
        c._results.AddRange(f.Results);
        foreach (var (k, v) in f.Roster) c._roster[k] = v;
        return c;
    }
}

public sealed record ManRecord(string Name, float Experience);

public sealed record BattleRecord(string Id, string Title, Side? Winner, string Reason, Losses AlliedLosses, Losses GermanLosses);
