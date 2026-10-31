using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// The soldiers' voices, in English, Norwegian or German by their army. Your
/// men answer each order you give, call out when they see the enemy, warn of
/// shells and aircraft, shout when pinned or running, cry out when hit, and
/// report a man killed. Enemy soldiers you can see are heard too when they are
/// hit, break or give up. Each man keeps the same voice all battle.
/// The lines are listed in assets/voices/voices.json, made by tools/make_voices.py.
/// </summary>
public partial class VoiceView : Node2D
{
    public Simulation? Sim { get; set; }
    /// <summary>The player's orders: each new one gets an answer.</summary>
    public SavedBattle? Battle { get; set; }
    public Side PlayerSide { get; set; }

    const string Manifest = "res://assets/voices/voices.json";
    /// <summary>At most this many men talking at once, besides the answer to an order.</summary>
    const int MaxTalking = 3;
    /// <summary>A team keeps quiet this long after one of its men has spoken.</summary>
    const float TeamQuiet = 4f;
    /// <summary>A team says the same kind of thing at most this often.</summary>
    const float RepeatQuiet = 15f;
    /// <summary>A team calls a round coming in when it lands this close, in metres.</summary>
    const float IncomingNear = 60f;
    /// <summary>Men are spotting the enemy afresh after this long without a target.</summary>
    const float ContactAgain = 45f;

    // nation -> men -> event -> files
    readonly Dictionary<string, List<Dictionary<string, List<string>>>> _lines = new();
    readonly Dictionary<string, AudioStream> _streams = new();
    readonly List<AudioStreamPlayer2D> _pool = new();
    int _next;
    AudioStreamPlayer _answer = null!;

    Simulation? _seenSim;
    SavedBattle? _seenBattle;
    int _seenCommands;
    float _seenUntil;
    readonly Dictionary<int, (Health Health, MoraleState Morale, int Ammo)> _was = new();
    readonly Dictionary<int, float> _teamSpoke = new();
    readonly Dictionary<(int Team, string Event), float> _teamSaid = new();
    readonly Dictionary<int, float> _teamLastTarget = new();

    public override void _Ready()
    {
        LoadManifest();
        for (int i = 0; i < 8; i++)
        {
            var p = new AudioStreamPlayer2D { MaxDistance = 2600, Attenuation = 1.2f };
            AddChild(p);
            _pool.Add(p);
        }
        // Your men answering orders are heard wherever the camera is, as in Close Combat
        _answer = new AudioStreamPlayer { VolumeDb = -3 };
        AddChild(_answer);
    }

    void LoadManifest()
    {
        if (!FileAccess.FileExists(Manifest)) return;
        var root = Json.ParseString(FileAccess.GetFileAsString(Manifest)).AsGodotDictionary();
        foreach (var (key, value) in root)
        {
            if (value.VariantType != Variant.Type.Array) continue;
            var men = new List<Dictionary<string, List<string>>>();
            foreach (var man in value.AsGodotArray())
            {
                var said = new Dictionary<string, List<string>>();
                foreach (var (ev, files) in man.AsGodotDictionary())
                    said[ev.AsString()] = files.AsGodotArray().Select(f => f.AsString()).ToList();
                men.Add(said);
            }
            _lines[key.AsString()] = men;
        }
    }

    /// <summary>A line for this man to say, or null if his army has none.</summary>
    AudioStream? Line(Soldier s, string ev)
    {
        string nation = s.Team.Nation is { Length: > 0 } n ? n : s.Side == Side.German ? "german" : "british";
        if (!_lines.TryGetValue(nation, out var men) || men.Count == 0) return null;
        var man = men[s.Id % men.Count];
        if (!man.TryGetValue(ev, out var files) || files.Count == 0) return null;
        string path = files[GD.RandRange(0, files.Count - 1)];
        if (!_streams.TryGetValue(path, out var stream))
        {
            stream = ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null!;
            _streams[path] = stream;
        }
        return stream;
    }

    /// <summary>Someone in the team to speak for it: the leader if he can, else any man still on his feet.</summary>
    static Soldier? Speaker(Team team) =>
        team.Soldiers.Where(s => s.CanAct && s.Morale <= MoraleState.Pinned).OrderByDescending(s => s.IsLeader).FirstOrDefault();

    /// <summary>Let a man say something where he stands, if he is not drowned out by others.</summary>
    void Say(Soldier s, string ev, bool urgent = false)
    {
        if (Sim == null) return;
        float now = Sim.Time;
        int team = s.Team.Id;
        if (!urgent && _teamSpoke.TryGetValue(team, out var spoke) && now - spoke < TeamQuiet) return;
        if (_teamSaid.TryGetValue((team, ev), out var said) && now - said < (urgent ? TeamQuiet : RepeatQuiet)) return;
        if (_pool.Count(p => p.Playing) >= MaxTalking) return;
        var stream = Line(s, ev);
        if (stream == null) return;
        var p = _pool[_next];
        _next = (_next + 1) % _pool.Count;
        p.Stream = stream;
        p.Position = View.ToScreen(s.Position);
        // The enemy is heard more faintly than your own men
        p.VolumeDb = s.Side == PlayerSide ? -2 : -8;
        p.PitchScale = 1f + (float)GD.RandRange(-0.03, 0.03);
        p.Play();
        _teamSpoke[team] = now;
        _teamSaid[(team, ev)] = now;
    }

    public override void _Process(double delta)
    {
        if (Sim == null) return;
        if (_seenSim != Sim || _seenBattle != Battle)
        {
            // A new or loaded battle: only what happens from now on is spoken
            _seenSim = Sim;
            _seenBattle = Battle;
            _seenCommands = Battle?.Commands.Count ?? 0;
            _seenUntil = Sim.Time;
            _was.Clear();
            _teamSpoke.Clear();
            _teamSaid.Clear();
            _teamLastTarget.Clear();
            foreach (var s in Sim.Soldiers) _was[s.Id] = (s.Health, s.Morale, s.Ammo);
            foreach (var p in _pool) p.Stop();
            _answer.Stop();
        }
        float since = _seenUntil, now = Sim.Time;
        bool New(float t) => t >= since && t < now;

        Answers();
        MenChanged();
        Contact(now);
        foreach (var (at, _, landsAt, firedBy) in Sim.IncomingRounds)
            if (firedBy != PlayerSide && New(landsAt - 1.6f))
                Warn(at, IncomingNear, "incoming");
        foreach (var a in Sim.AircraftOverhead)
            if (a.Side != PlayerSide && New(a.Start))
                Warn(a.To, 400, "aircraft");
        _seenUntil = now;
    }

    /// <summary>The selected team answers the order just given.</summary>
    void Answers()
    {
        if (Battle == null) return;
        var commands = Battle.Commands;
        if (commands.Count <= _seenCommands) return;
        // Several orders at once (all teams told to dig in): only the last is answered
        var c = commands[^1];
        _seenCommands = commands.Count;
        var team = Sim!.Teams.FirstOrDefault(t => t.Id == c.Team);
        if (team == null || team.Side != PlayerSide || Speaker(team) is not { } man) return;
        string? ev = c.Kind switch
        {
            "move" => (MoveMode)c.Value switch
            {
                MoveMode.MoveFast => "ack_fast",
                MoveMode.Sneak => "ack_sneak",
                _ => "ack_move",
            },
            "fire" => "ack_fire",
            "defend" => "ack_defend",
            "hide" => "ack_hide",
            "stop" => "ack_stop",
            "dig" => "ack_dig",
            "support" => "ack_move",
            _ => null,
        };
        if (ev == null || Line(man, ev) is not { } stream) return;
        _answer.Stream = stream;
        _answer.PitchScale = 1f + (float)GD.RandRange(-0.02, 0.02);
        _answer.Play();
        _teamSpoke[team.Id] = Sim.Time;
    }

    /// <summary>Men hit, killed, pinned, breaking, giving up or out of ammunition.</summary>
    void MenChanged()
    {
        foreach (var s in Sim!.Soldiers)
        {
            var now = (s.Health, s.Morale, s.Ammo);
            if (!_was.TryGetValue(s.Id, out var was)) was = now;
            _was[s.Id] = now;
            if (now == was) continue;
            bool ours = s.Side == PlayerSide;
            // Of the enemy, only what the player can see is heard
            bool heard = ours || s.IsVisibleToEnemy(Sim.Time);

            if (s.Health > was.Health)
            {
                if (s.Health == Health.Dead)
                {
                    if (ours && Mate(s) is { } mate) Say(mate, "man_down");
                }
                else if (heard) Say(s, "wounded", urgent: true);
                continue;
            }
            if (!s.CanAct && s.Morale != MoraleState.Surrendered) continue;
            if (s.Morale != was.Morale)
            {
                if (s.Morale == MoraleState.Surrendered && heard) Say(s, "surrender", urgent: true);
                else if (s.Morale >= MoraleState.Panicked && was.Morale < MoraleState.Panicked && heard) Say(s, "panic");
                else if (s.Morale is MoraleState.Pinned or MoraleState.Cowering && was.Morale == MoraleState.Ready && ours)
                    Say(s, "pinned");
            }
            else if (s.Ammo == 0 && was.Ammo > 0 && ours && !s.Weapon.IsIndirect)
                Say(s, "no_ammo");
        }
    }

    /// <summary>A man near the fallen one, still able to call out.</summary>
    static Soldier? Mate(Soldier fallen) =>
        fallen.Team.Soldiers
            .Where(m => m != fallen && m.CanAct && Vec.Distance(m.Position, fallen.Position) < 50)
            .OrderBy(m => Vec.Distance(m.Position, fallen.Position))
            .FirstOrDefault();

    /// <summary>A team of yours that starts shooting at men after a quiet spell calls out the enemy.</summary>
    void Contact(float now)
    {
        foreach (var team in Sim!.Teams)
        {
            if (team.Side != PlayerSide) continue;
            var shooter = team.Soldiers.FirstOrDefault(s => s.CanAct && s.Target != null);
            if (shooter == null) continue;
            bool fresh = !_teamLastTarget.TryGetValue(team.Id, out var last) || now - last > ContactAgain;
            _teamLastTarget[team.Id] = now;
            if (fresh && now > 1f) Say(shooter, "contact");
        }
    }

    /// <summary>The team of yours nearest a danger shouts a warning.</summary>
    void Warn(Vec at, float near, string ev)
    {
        Team? best = null;
        float bestD = near;
        foreach (var team in Sim!.Teams)
        {
            if (team.Side != PlayerSide || team.IsDestroyed) continue;
            float d = Vec.Distance(team.Center, at);
            if (d < bestD) (best, bestD) = (team, d);
        }
        if (best != null && Speaker(best) is { } man) Say(man, ev);
    }
}
