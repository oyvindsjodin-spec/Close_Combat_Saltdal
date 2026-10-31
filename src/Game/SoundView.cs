using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// Plays the sounds of the battle: shots, mortar bombs leaving the tube,
/// explosions, aircraft, burning houses and digging. Sounds come from where
/// they happen, so distant fighting is quieter and off to one side. Each
/// weapon of 1940 has its own sound: the Lee-Enfield, Krag and Mauser rifles,
/// the Bren, Colt and MG 34 machine guns, the MP 40. The sounds themselves are
/// made by tools/make_sounds.py.
/// </summary>
public partial class SoundView : Node2D
{
    public Simulation? Sim { get; set; }
    /// <summary>Loops (aircraft, fire) stop while the game is paused.</summary>
    public bool Paused { get; set; }

    const int Voices = 24;
    /// <summary>How long before landing each kind of round is heard coming, matching the length of its sound.</summary>
    const float ShellWhistle = 1.2f, MortarWhoosh = 0.9f, BombWhistle = 2.0f;
    /// <summary>The far-off boom of the guns firing, this long before their shells land.</summary>
    const float GunReport = 6f;
    /// <summary>At most this many new sounds of one kind start in a frame, so speeded-up fighting does not roar.</summary>
    const int PerKindPerFrame = 3;

    readonly Dictionary<string, AudioStream> _sounds = new();
    readonly List<AudioStreamPlayer2D> _pool = new();
    int _next;
    AudioStreamPlayer2D _aircraft = null!;
    AudioStreamPlayer2D _siren = null!;
    AudioStreamPlayer _distant = null!;
    readonly Dictionary<Weapon, string> _weaponSound = new();
    readonly Dictionary<int, AudioStreamPlayer2D> _fires = new();
    readonly Dictionary<int, Soldier> _byId = new();
    readonly Dictionary<Team, float> _nextDig = new();
    Simulation? _seenSim;
    float _seenUntil;

    public override void _Ready()
    {
        foreach (var name in new[]
                 {
                     "rifle_lee_enfield", "rifle_krag", "rifle_kar98k", "mg_bren", "mg_colt", "mg_mg34", "smg_mp40",
                     "ricochet", "mortar", "mortar_light", "blast_small", "blast_shell", "blast_bomb",
                     "incoming_shell", "incoming_mortar", "incoming_bomb", "distant_gun", "stuka_siren", "dig",
                 })
            _sounds[name] = GD.Load<AudioStream>($"res://assets/sounds/{name}.wav");
        _sounds["aircraft"] = Looping("aircraft");
        _sounds["fire"] = Looping("fire");
        for (int i = 0; i < Voices; i++)
        {
            var p = MakePlayer();
            _pool.Add(p);
        }
        _aircraft = MakePlayer();
        _aircraft.Stream = _sounds["aircraft"];
        _aircraft.MaxDistance = 6000;
        _siren = MakePlayer();
        _siren.Stream = _sounds["stuka_siren"];
        _siren.MaxDistance = 6000;
        _distant = new AudioStreamPlayer { Stream = _sounds["distant_gun"], VolumeDb = -16 };
        AddChild(_distant);
    }

    /// <summary>Which sound a weapon makes, from its name in data/weapons.json, or from what kind of gun it is.</summary>
    string WeaponSound(Soldier? s)
    {
        if (s == null) return "rifle_lee_enfield";
        var w = s.Weapon;
        if (_weaponSound.TryGetValue(w, out var known)) return known;
        string n = w.Name.ToLowerInvariant();
        bool german = s.Side == Side.German, norwegian = s.Team.Nation == "norwegian";
        string kind =
            n.Contains("enfield") || n.Contains("no. 1") ? "rifle_lee_enfield"
            : n.Contains("krag") ? "rifle_krag"
            : n.Contains("98") || n.Contains("mauser") ? "rifle_kar98k"
            : n.Contains("bren") ? "mg_bren"
            : n.Contains("colt") ? "mg_colt"
            : n.Contains("mg 34") || n.Contains("mg34") ? "mg_mg34"
            : n.Contains("mp 40") || n.Contains("mp40") ? "smg_mp40"
            // Something new in the data: go by what kind of gun it is
            : w.Burst >= 5 && w.Range >= 500 ? (german ? "mg_mg34" : norwegian ? "mg_colt" : "mg_bren")
            : w.Burst > 1 ? "smg_mp40"
            : german ? "rifle_kar98k" : norwegian ? "rifle_krag" : "rifle_lee_enfield";
        _weaponSound[w] = kind;
        return kind;
    }

    static bool IsMortarBomb(Explosive e) => e.Name.Contains("mortar", System.StringComparison.OrdinalIgnoreCase);
    static bool IsAircraftBomb(Explosive e) => e.Name.Contains("kg") || (!IsMortarBomb(e) && e.KillRadius > 6f);

    static AudioStream Looping(string name)
    {
        var wav = (AudioStreamWav)GD.Load<AudioStreamWav>($"res://assets/sounds/{name}.wav").Duplicate();
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopBegin = 0;
        wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
        return wav;
    }

    AudioStreamPlayer2D MakePlayer()
    {
        var p = new AudioStreamPlayer2D { MaxDistance = 4000, Attenuation = 1.4f };
        AddChild(p);
        return p;
    }

    public override void _Process(double delta)
    {
        if (Sim == null) return;
        if (_seenSim != Sim)
        {
            // A new battle: start listening from now, and forget the old one's loops
            _seenSim = Sim;
            _seenUntil = Sim.Time;
            _byId.Clear();
            foreach (var s in Sim.Soldiers) _byId[s.Id] = s;
            foreach (var f in _fires.Values) f.QueueFree();
            _fires.Clear();
            _nextDig.Clear();
            _aircraft.Stop();
            _siren.Stop();
            _distant.Stop();
        }
        var started = new Dictionary<string, int>();
        void Play(string name, Vec at, float db, float pitchSpread = 0.08f)
        {
            started.TryGetValue(name, out int n);
            if (n >= PerKindPerFrame) return;
            started[name] = n + 1;
            var p = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            p.Stream = _sounds[name];
            p.Position = View.ToScreen(at);
            p.VolumeDb = db;
            p.PitchScale = 1f + (float)GD.RandRange(-pitchSpread, pitchSpread);
            p.Play();
        }

        // Everything stamped from the start of the first tick run since last frame,
        // up to (not including) the time now. A tick stamps what happens in it with
        // the time it began, so the last frame's end time belongs to this frame.
        float since = _seenUntil, now = Sim.Time;
        bool New(float t) => t >= since && t < now;
        foreach (var shot in Sim.RecentShots)
        {
            if (!New(shot.Time)) continue;
            string kind = WeaponSound(_byId.GetValueOrDefault(shot.ShooterId));
            Play(kind, shot.From, kind.StartsWith("rifle") ? -6 : -8, 0.05f);
            // Now and then a miss glances off a stone and whines away
            if (!shot.Hit && GD.Randf() < 0.08f)
                Play("ricochet", shot.To, -12, 0.15f);
        }
        foreach (var s in _byId.Values)
            if (s.Weapon.IsIndirect && New(s.LastShotTime))
                Play(s.Weapon.Shell is { KillRadius: < 3f } ? "mortar_light" : "mortar", s.Position, -4, 0.04f);
        foreach (var b in Sim.RecentBlasts)
        {
            if (!New(b.Time)) continue;
            var e = b.Explosive;
            string kind = IsMortarBomb(e) ? "blast_small" : IsAircraftBomb(e) ? "blast_bomb" : "blast_shell";
            Play(kind, b.At, kind == "blast_small" ? -3 : 0, 0.1f);
        }
        // Rounds heard coming in just before they land: the whoosh of a mortar
        // bomb, the whistle of a shell, the scream of a bomb. Guns are also
        // heard firing far away, a few seconds before their shells arrive.
        foreach (var (at, e, landsAt, _) in Sim.IncomingRounds)
        {
            if (IsMortarBomb(e))
            {
                if (New(landsAt - MortarWhoosh)) Play("incoming_mortar", at, -8, 0.06f);
            }
            else if (IsAircraftBomb(e))
            {
                if (New(landsAt - BombWhistle)) Play("incoming_bomb", at, -8, 0.04f);
            }
            else
            {
                if (New(landsAt - ShellWhistle)) Play("incoming_shell", at, -9, 0.05f);
                if (New(landsAt - GunReport) && !_distant.Playing)
                {
                    _distant.PitchScale = 1f + (float)GD.RandRange(-0.08, 0.08);
                    _distant.Play();
                }
            }
        }
        DigSounds();
        Loops();
        _seenUntil = now;
    }

    /// <summary>A spade now and then from each team digging in.</summary>
    void DigSounds()
    {
        foreach (var team in Sim!.Teams)
        {
            if (team.Order != TeamOrder.DigIn || team.IsMoving) continue;
            if (_nextDig.TryGetValue(team, out var at) && Sim.Time < at) continue;
            _nextDig[team] = Sim.Time + (float)GD.RandRange(1.2, 2.5);
            var p = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            p.Stream = _sounds["dig"];
            p.Position = View.ToScreen(team.Center);
            p.VolumeDb = -14;
            p.PitchScale = 1f + (float)GD.RandRange(-0.1, 0.1);
            p.Play();
        }
    }

    /// <summary>The drone of aircraft overhead and the crackle of burning houses.</summary>
    void Loops()
    {
        var plane = Sim!.AircraftOverhead.FirstOrDefault(a => a.IsFlying(Sim.Time));
        bool flying = Sim.AircraftOverhead.Any(a => a.IsFlying(Sim.Time));
        if (flying)
        {
            _aircraft.Position = View.ToScreen(plane.PositionAt(Sim.Time));
            if (!_aircraft.Playing) _aircraft.Play();
        }
        else if (_aircraft.Playing) _aircraft.Stop();
        _aircraft.StreamPaused = Paused;

        // A Stuka's siren wails as it tips into its dive
        foreach (var a in Sim.AircraftOverhead)
            if (a.Dives && a.Start >= _seenUntil && a.Start < Sim.Time)
            {
                _siren.PitchScale = 1f + (float)GD.RandRange(-0.04, 0.04);
                _siren.Play();
            }
        var diving = Sim.AircraftOverhead.FirstOrDefault(a => a.Dives && a.IsFlying(Sim.Time));
        if (_siren.Playing && diving.Dives) _siren.Position = View.ToScreen(diving.PositionAt(Sim.Time));
        _siren.StreamPaused = Paused;
        _distant.StreamPaused = Paused;

        foreach (var house in Sim.Houses)
        {
            bool burning = house.State == HouseState.Burning;
            _fires.TryGetValue(house.Id, out var p);
            if (burning && p == null)
            {
                p = MakePlayer();
                p.Stream = _sounds["fire"];
                p.Position = View.ToScreen(house.Center);
                p.VolumeDb = -6;
                p.MaxDistance = 1400;
                p.Play((float)GD.RandRange(0, 3));
                _fires[house.Id] = p;
            }
            else if (!burning && p != null)
            {
                p.QueueFree();
                _fires.Remove(house.Id);
            }
            if (p != null) p.StreamPaused = Paused;
        }
    }
}
