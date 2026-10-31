using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// Plays the sounds of the battle: shots, mortar bombs leaving the tube,
/// explosions, aircraft, burning houses and digging. Sounds come from where
/// they happen, so distant fighting is quieter and off to one side. The sounds
/// themselves are made by tools/make_sounds.py.
/// </summary>
public partial class SoundView : Node2D
{
    public Simulation? Sim { get; set; }
    /// <summary>Loops (aircraft, fire) stop while the game is paused.</summary>
    public bool Paused { get; set; }

    const int Voices = 24;
    const float WhistleSeconds = 1.2f;
    /// <summary>At most this many new sounds of one kind start in a frame, so speeded-up fighting does not roar.</summary>
    const int PerKindPerFrame = 3;

    readonly Dictionary<string, AudioStream> _sounds = new();
    readonly List<AudioStreamPlayer2D> _pool = new();
    int _next;
    AudioStreamPlayer2D _aircraft = null!;
    readonly Dictionary<int, AudioStreamPlayer2D> _fires = new();
    readonly Dictionary<int, Soldier> _byId = new();
    readonly Dictionary<Team, float> _nextDig = new();
    Simulation? _seenSim;
    float _seenUntil;

    public override void _Ready()
    {
        foreach (var name in new[] { "rifle", "smg", "mg", "mortar", "blast_small", "blast_shell", "blast_bomb", "dig", "incoming" })
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
    }

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
        var p = new AudioStreamPlayer2D { MaxDistance = 2600, Attenuation = 1.6f };
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

        float since = _seenUntil;
        foreach (var shot in Sim.RecentShots)
        {
            if (shot.Time <= since) continue;
            var weapon = _byId.TryGetValue(shot.ShooterId, out var s) ? s.Weapon : null;
            string kind = weapon == null ? "rifle"
                : weapon.Burst >= 5 && weapon.Range >= 500 ? "mg"
                : weapon.Burst > 1 ? "smg" : "rifle";
            Play(kind, shot.From, kind == "rifle" ? -6 : -8);
        }
        foreach (var s in _byId.Values)
            if (s.Weapon.IsIndirect && s.LastShotTime > since && s.LastShotTime <= Sim.Time)
                Play("mortar", s.Position, -4, 0.04f);
        foreach (var b in Sim.RecentBlasts)
        {
            if (b.Time <= since) continue;
            float r = b.Explosive.KillRadius;
            Play(r <= 4.5f ? "blast_small" : r <= 6f ? "blast_shell" : "blast_bomb", b.At, r <= 4.5f ? -3 : 0, 0.1f);
        }
        // The whistle of a shell coming in, just over a second before it lands
        foreach (var (at, landsAt) in Sim.Incoming)
            if (landsAt - WhistleSeconds > since && landsAt - WhistleSeconds <= Sim.Time)
                Play("incoming", at, -10, 0.05f);
        DigSounds();
        Loops();
        _seenUntil = Sim.Time;
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
