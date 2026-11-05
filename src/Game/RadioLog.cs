using System.Collections.Generic;
using System.Linq;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>What a report is about, which sets its colour and lets the player hide a kind.</summary>
public enum ReportKind
{
    /// <summary>Men killed or hit.</summary>
    Casualty,
    /// <summary>Pinned down, breaking, giving up.</summary>
    Morale,
    /// <summary>Running short of ammunition.</summary>
    Ammo,
    /// <summary>Orders taken, positions reached, the enemy seen.</summary>
    Orders,
    /// <summary>Everything else: the battle's own news.</summary>
    General,
}

public readonly record struct Report(float Time, string From, string Text, ReportKind Kind);

/// <summary>
/// The radio and runner reports from the player's teams, as written on the
/// signal pad in the command panel: a man killed or hit, a team pinned down
/// or breaking, ammunition running short, an order taken, a position reached,
/// the enemy sighted, and the battle's own messages. Worked out by watching
/// the simulation, so nothing in it changes how the battle goes.
/// </summary>
public sealed class RadioLog
{
    const int Keep = 200;
    /// <summary>A team reports the same thing at most this often, in battle seconds.</summary>
    const float Repeat = 20f;
    /// <summary>A team that has not shot at anyone for this long reports the enemy afresh.</summary>
    const float ContactAgain = 45f;

    readonly List<Report> _reports = new();
    public IReadOnlyList<Report> Reports => _reports;
    /// <summary>Goes up whenever a report is added, so the pad knows to redraw.</summary>
    public int Version { get; private set; }

    Simulation? _sim;
    SavedBattle? _battle;
    Side _side;
    int _seenMessages, _seenCommands;
    readonly Dictionary<int, (Health Health, MoraleState Morale, int Ammo)> _men = new();
    readonly Dictionary<int, (MoraleState Morale, bool Moving, int AmmoPercent, bool Destroyed)> _teams = new();
    readonly Dictionary<(int, string), float> _said = new();
    readonly Dictionary<int, float> _lastTarget = new();

    public void Update(Simulation sim, SavedBattle? battle, Side side)
    {
        if (sim != _sim || battle != _battle || side != _side)
            Reset(sim, battle, side);
        BattleMessages();
        Orders();
        Men();
        Teams();
        Contact();
    }

    void Reset(Simulation sim, SavedBattle? battle, Side side)
    {
        _sim = sim;
        _battle = battle;
        _side = side;
        _reports.Clear();
        _men.Clear();
        _teams.Clear();
        _said.Clear();
        _lastTarget.Clear();
        _seenMessages = 0;
        _seenCommands = battle?.Commands.Count ?? 0;
        foreach (var s in sim.Soldiers) _men[s.Id] = (s.Health, s.Morale, s.Ammo);
        foreach (var t in sim.Teams) _teams[t.Id] = (t.Morale, t.IsMoving, t.AmmoPercent, t.IsDestroyed);
        Version++;
    }

    void Add(string from, string text, ReportKind kind)
    {
        _reports.Add(new Report(_sim!.Time, from, text, kind));
        if (_reports.Count > Keep) _reports.RemoveAt(0);
        Version++;
    }

    /// <summary>Once per team and kind of news within a short while, so the pad is not flooded.</summary>
    bool Fresh(Team team, string what)
    {
        float now = _sim!.Time;
        if (_said.TryGetValue((team.Id, what), out var t) && now - t < Repeat) return false;
        _said[(team.Id, what)] = now;
        return true;
    }

    void BattleMessages()
    {
        var all = _sim!.Messages;
        for (; _seenMessages < all.Count; _seenMessages++)
        {
            var m = all[_seenMessages];
            if (m.ForSide != null && m.ForSide != _side) continue;
            // "No. 4 Coy (1): the house is burning" comes from that team; the rest from headquarters
            string from = m.ForSide == null ? "Battle report" : "HQ", text = m.Text;
            int colon = text.IndexOf(": ");
            if (colon > 0 && colon < 34)
            {
                from = text[..colon];
                text = char.ToUpper(text[colon + 2]) + text[(colon + 3)..];
            }
            var kind = text.Contains("fire") || text.Contains("collapsed") ? ReportKind.Casualty
                : text.Contains("pinned") ? ReportKind.Morale
                : text.Contains("bombs") || text.Contains("ammunition") ? ReportKind.Ammo
                : ReportKind.General;
            Add(from, text, kind);
        }
    }

    void Orders()
    {
        if (_battle == null) return;
        var commands = _battle.Commands;
        for (; _seenCommands < commands.Count; _seenCommands++)
        {
            var c = commands[_seenCommands];
            var team = _sim!.Teams.FirstOrDefault(t => t.Id == c.Team);
            if (team == null || team.Side != _side) continue;
            string? text = c.Kind switch
            {
                "move" => (MoveMode)c.Value switch
                {
                    MoveMode.MoveFast => "Moving out, on the double.",
                    MoveMode.Sneak => "Creeping forward.",
                    _ => "Moving out.",
                },
                "fire" => "Opening fire.",
                "defend" => "Taking up a position.",
                "hide" => "Getting our heads down.",
                "stop" => "Holding here.",
                "dig" => "Digging in.",
                "support" => "Calling for the guns.",
                _ => null,
            };
            if (text != null) Add(team.Name, text, ReportKind.Orders);
        }
    }

    void Men()
    {
        foreach (var s in _sim!.Soldiers)
        {
            if (s.Side != _side) continue;
            var now = (s.Health, s.Morale, s.Ammo);
            if (!_men.TryGetValue(s.Id, out var was)) was = now;
            _men[s.Id] = now;
            if (now == was) continue;
            if (s.Health > was.Health)
            {
                string text = s.Health switch
                {
                    Health.Dead => $"{s.Name} has been killed!",
                    Health.Incapacitated => $"{s.Name} is badly hit!",
                    _ => $"We've taken a hit. {s.Name} is wounded.",
                };
                Add(s.Team.Name, text, ReportKind.Casualty);
                continue;
            }
            if (s.Morale == MoraleState.Surrendered && was.Morale != MoraleState.Surrendered)
                Add(s.Team.Name, $"{s.Name} has given himself up.", ReportKind.Morale);
            else if (s.Ammo == 0 && was.Ammo > 0 && s.CanAct)
                Add(s.Team.Name, s.Weapon.IsIndirect ? "Out of bombs." : $"{s.Name}: {s.Weapon.Name} out of ammunition.", ReportKind.Ammo);
        }
    }

    void Teams()
    {
        foreach (var t in _sim!.Teams)
        {
            if (t.Side != _side) continue;
            var now = (Morale: t.Morale, Moving: t.IsMoving, AmmoPercent: t.AmmoPercent, Destroyed: t.IsDestroyed);
            if (!_teams.TryGetValue(t.Id, out var was)) was = now;
            _teams[t.Id] = now;
            if (now.Destroyed)
            {
                if (!was.Destroyed) Add(t.Name, "No longer answering.", ReportKind.Casualty);
                continue;
            }
            if (now.Morale != was.Morale && now.Morale > was.Morale)
            {
                string? text = now.Morale switch
                {
                    MoraleState.Pinned => "We're pinned down!",
                    MoraleState.Cowering => "We can't lift our heads!",
                    MoraleState.Panicked => "The men are panicking!",
                    MoraleState.Routed => "We're falling back!",
                    _ => null,
                };
                if (text != null && Fresh(t, "morale" + now.Morale)) Add(t.Name, text, ReportKind.Morale);
            }
            else if (now.Morale == MoraleState.Ready && was.Morale >= MoraleState.Pinned && Fresh(t, "rally"))
                Add(t.Name, "We've got our nerve back.", ReportKind.Morale);
            if (was.Moving && !now.Moving && t.Morale == MoraleState.Ready && Fresh(t, "arrived"))
                Add(t.Name, t.Order == TeamOrder.DigIn ? "In position, starting to dig." : "In position.", ReportKind.Orders);
            if (now.AmmoPercent <= 25 && was.AmmoPercent > 25 && Fresh(t, "ammo"))
                Add(t.Name, "We're running low on ammo.", ReportKind.Ammo);
        }
    }

    void Contact()
    {
        float now = _sim!.Time;
        foreach (var t in _sim.Teams)
        {
            if (t.Side != _side || t.IsDestroyed) continue;
            var target = t.Soldiers.FirstOrDefault(s => s.CanAct && s.Target != null)?.Target;
            if (target == null) continue;
            bool fresh = !_lastTarget.TryGetValue(t.Id, out var last) || now - last > ContactAgain;
            _lastTarget[t.Id] = now;
            if (fresh && now > 1f)
            {
                var d = System.Numerics.Vector2.Distance(t.Center, target.Position);
                Add(t.Name, $"Enemy in sight, {d:0} m. Engaging.", ReportKind.Orders);
            }
        }
    }
}
