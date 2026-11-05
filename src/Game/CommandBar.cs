using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>
/// The command panel along the bottom of the battle screen, in the manner of
/// the classic wargames: a strip of buttons and the battle clock across the
/// top; below it the list of your teams with what each is doing, the men of
/// the selected team (role, health, morale, fatigue, weapon and ammunition),
/// the signal pad with the reports coming in, a map of the whole battlefield
/// and a close-up of the ground. Painted olive steel with brass rivets; the
/// pictures come from tools/paint_hud.py.
/// </summary>
public partial class CommandBar : Control
{
    public const float BarHeight = 252f;
    const float StripHeight = 38f;

    // Order ids, the same as the order menu's
    public const int OrderMove = 0, OrderMoveFast = 1, OrderSneak = 2, OrderFire = 3, OrderDefend = 4, OrderHide = 5, OrderStop = 6, OrderDigIn = 7;
    static readonly (int Id, string Icon, string Tip)[] OrderButtons =
    {
        (OrderMove, "move", "Move: click where to go"),
        (OrderMoveFast, "fast", "Move fast: run there, tiring the men"),
        (OrderSneak, "sneak", "Sneak: creep there, keeping low"),
        (OrderFire, "fire", "Fire: click a target (F)"),
        (OrderDefend, "defend", "Defend: click the way to face"),
        (OrderHide, "hide", "Hide: get down and keep quiet"),
        (OrderDigIn, "dig", "Dig in: click the way to face"),
        (OrderStop, "stop", "Stop where you are"),
    };

    public event Action<Team>? TeamClicked;
    /// <summary>The player clicked the mini-map, the close-up or a man; the argument is a point in world screen units.</summary>
    public event Action<Vector2>? MapClicked;
    public event Action<int>? OrderClicked;
    public event Action? ZoomIn, ZoomOut, PauseClicked, SpeedClicked, CeasefireClicked, SoundClicked, HelpClicked, BeginClicked;

    /// <summary>What the rest of the game tells the panel each frame.</summary>
    public struct State
    {
        public Team? Selected;
        public Rect2 CameraView;
        public bool RevealAll, Paused, SoundOn, CeasefireArmed, HelpOpen;
        public int Speed;
        /// <summary>The order button waiting for a click on the map, or -1.</summary>
        public int PendingOrder;
        /// <summary>What lies under the mouse, or a hint for the order being given.</summary>
        public string Info;
        /// <summary>Where the close-up looks, in world screen units.</summary>
        public Vector2 CloseUpAt;
    }

    Simulation? _sim;
    SavedBattle? _battle;
    Side _side;
    State _state;
    readonly RadioLog _radio = new();

    TeamList _teams = null!;
    TeamPanel _panel = null!;
    SignalPad _pad = null!;
    MiniMap _miniMap = null!;
    CloseUp _closeUp = null!;
    readonly List<IconButton> _strip = new();
    IconButton _pause = null!, _speed = null!, _flag = null!, _sound = null!, _help = null!;
    TextButton _begin = null!;
    int _troopsAtStart;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, BarHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TextureRepeat = TextureRepeatEnum.Enabled;

        _teams = new TeamList();
        _teams.Clicked += t => TeamClicked?.Invoke(t);
        AddChild(_teams);
        _panel = new TeamPanel();
        _panel.ManClicked += s => MapClicked?.Invoke(View.ToScreen(s.Position));
        AddChild(_panel);
        foreach (var (id, icon, tip) in OrderButtons)
        {
            var b = new IconButton(icon, tip);
            b.Pressed += () => OrderClicked?.Invoke(id);
            _panel.AddChild(b);
            _panel.Orders.Add((id, b));
        }
        _pad = new SignalPad(_radio);
        AddChild(_pad);
        _miniMap = new MiniMap();
        _miniMap.Clicked += p => MapClicked?.Invoke(p);
        AddChild(_miniMap);
        _closeUp = new CloseUp();
        _closeUp.Clicked += p => MapClicked?.Invoke(p);
        AddChild(_closeUp);

        IconButton Add(string icon, string tip, Action press)
        {
            var b = new IconButton(icon, tip);
            b.Pressed += press;
            AddChild(b);
            _strip.Add(b);
            return b;
        }
        Add("zoom_out", "Zoom out (wheel)", () => ZoomOut?.Invoke());
        Add("zoom_in", "Zoom in (wheel)", () => ZoomIn?.Invoke());
        _pause = Add("pause", "Pause (Space)", () => PauseClicked?.Invoke());
        _speed = Add("speed", "Game speed: x1, x2, x4 (1, 2, 3)", () => SpeedClicked?.Invoke());
        _flag = Add("flag", "Call a ceasefire: click twice (C C)", () => CeasefireClicked?.Invoke());
        _sound = Add("sound_on", "Sound on or off (F3)", () => SoundClicked?.Invoke());
        _help = Add("help", "Keys and mouse", () => HelpClicked?.Invoke());
        _begin = new TextButton("Begin attack", "The attack starts now instead of when the clock runs out (Enter)");
        _begin.Pressed += () => BeginClicked?.Invoke();
        AddChild(_begin);

        Resized += Layout;
        Layout();
    }

    void Layout()
    {
        float w = Size.X;
        float x = 238;
        foreach (var b in _strip)
        {
            b.Position = new Vector2(x, 5);
            b.Size = new Vector2(29, 29);
            x += b == _strip[1] || b == _speed ? 37 : 31;
        }
        _begin.Position = new Vector2(x + 152, 7);
        _begin.Size = new Vector2(104, 25);

        float top = StripHeight + 4, h = BarHeight - top - 8;
        float left = 8, gap = 6;
        float mini = h;
        float rest = w - left * 2 - 320 - 500 - mini - gap * 4;
        float pad = Mathf.Clamp(rest - 230, 220, 330);
        float close = rest - pad;
        if (close < 120) { pad = Mathf.Max(160, rest); close = 0; }
        _teams.Position = new Vector2(left, top); _teams.Size = new Vector2(320, h);
        _panel.Position = new Vector2(left + 320 + gap, top); _panel.Size = new Vector2(500, h);
        _pad.Position = new Vector2(_panel.Position.X + 500 + gap, top); _pad.Size = new Vector2(pad, h);
        _miniMap.Position = new Vector2(_pad.Position.X + pad + gap, top); _miniMap.Size = new Vector2(mini, h);
        _closeUp.Visible = close > 0;
        _closeUp.Position = new Vector2(_miniMap.Position.X + mini + gap, top); _closeUp.Size = new Vector2(Mathf.Max(0, close), h);
        _panel.LayoutButtons();
    }

    public void Build(Simulation sim, SavedBattle battle, Side side, MapData map, Texture2D? ground)
    {
        _sim = sim;
        _battle = battle;
        _side = side;
        _troopsAtStart = sim.Teams.Where(t => t.Side == side).Sum(t => t.Soldiers.Count);
        _teams.Setup(sim, side);
        _panel.Sim = sim;
        _panel.Team = null;
        _miniMap.Setup(sim, side, map, ground);
        _closeUp.Setup();
        _radio.Update(sim, battle, side);
        _pad.Rewind();
    }

    public void Refresh(State state)
    {
        _state = state;
        if (_sim == null) return;
        _radio.Update(_sim, _battle, _side);
        _teams.Selected = state.Selected;
        _teams.QueueRedraw();
        _panel.Team = state.Selected;
        _panel.Pending = state.PendingOrder;
        _panel.QueueRedraw();
        foreach (var (id, b) in _panel.Orders)
        {
            b.Disabled = state.Selected == null || _sim.IsOver;
            b.Lit = state.PendingOrder == id;
        }
        _pad.QueueRedraw();
        _miniMap.CameraView = state.CameraView;
        _miniMap.RevealAll = state.RevealAll;
        _miniMap.QueueRedraw();
        _closeUp.LookAt(state.CloseUpAt);
        _pause.Icon = state.Paused ? "play" : "pause";
        _pause.Lit = state.Paused;
        _pause.Tip = state.Paused ? "Carry on (Space)" : "Pause (Space)";
        _speed.Caption = state.Speed > 1 ? $"x{state.Speed}" : "";
        _speed.Lit = state.Speed > 1;
        _flag.Lit = state.CeasefireArmed;
        _flag.Tip = state.CeasefireArmed ? "Click again to call the ceasefire" : "Call a ceasefire: click twice (C C)";
        _sound.Icon = state.SoundOn ? "sound_on" : "sound_off";
        _help.Lit = state.HelpOpen;
        _begin.Visible = _sim.InPrep;
        foreach (var b in _strip) b.QueueRedraw();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        Look.Steel(this, r);
        Look.Bevel(this, r, raised: true, 3);
        // The strip across the top, with a groove below it
        DrawLine(new Vector2(4, StripHeight), new Vector2(Size.X - 4, StripHeight), new Color(0, 0, 0, 0.55f), 1);
        DrawLine(new Vector2(4, StripHeight + 1), new Vector2(Size.X - 4, StripHeight + 1), new Color(1, 1, 0.85f, 0.18f), 1);
        Look.Rivets(this, r, 7);
        if (_sim == null) return;

        // The battle's name, on a plate
        var plate = new Rect2(14, 7, 216, 25);
        Look.Sunken(this, plate);
        Look.Write(this, plate.Position + new Vector2(8, 17), _sim.Title, 13, Look.Text, plate.Size.X - 12);

        // Troop status: how much of your force is still fighting
        float x = _strip.Count > 0 ? _strip[^1].Position.X + 38 : 480;
        var clock = new Rect2(x, 7, 146, 25);
        Look.Sunken(this, clock, true);
        int left = (int)(_sim.TimeLimit - Mathf.Max(_sim.Time, _sim.PrepUntil));
        int prep = (int)Mathf.Ceil(_sim.PrepUntil - _sim.Time);
        if (_sim.InPrep)
        {
            Look.Write(this, clock.Position + new Vector2(8, 11), "ATTACK IN", 9, Look.DimText);
            Look.Write(this, clock.Position + new Vector2(8, 22), $"{prep / 60:00}:{prep % 60:00}", 12, Look.Amber);
            Look.Write(this, clock.Position + new Vector2(76, 11), "THEN", 9, Look.DimText);
            Look.Write(this, clock.Position + new Vector2(76, 22), $"{left / 60:00}:{left % 60:00}", 12, Look.Text);
        }
        else
        {
            Look.Write(this, clock.Position + new Vector2(8, 17), "TIME", 10, Look.DimText);
            Look.Write(this, clock.Position + new Vector2(50, 18), $"{left / 60:00}:{left % 60:00}", 15,
                left < 120 ? Look.Red.Lightened(0.3f) : Look.Amber);
            if (_state.Paused) Look.Write(this, clock.Position + new Vector2(102, 17), "HALT", 10, Look.Highlight);
        }

        // The flags and who holds them
        float fx = clock.End.X + (_begin.Visible ? 122 : 12);
        int alive = _sim.Teams.Where(t => t.Side == _side).Sum(t => t.Soldiers.Count(s => s.IsEffective));
        var troops = new Rect2(fx, 7, 132, 25);
        Look.Sunken(this, troops);
        Look.Write(this, troops.Position + new Vector2(6, 11), "TROOP STATUS", 9, Look.DimText);
        float frac = _troopsAtStart > 0 ? (float)alive / _troopsAtStart : 0;
        var bar = new Rect2(troops.Position + new Vector2(6, 15), new Vector2(troops.Size.X - 12, 6));
        DrawRect(bar, new Color(0.35f, 0.08f, 0.05f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * frac, bar.Size.Y)), Look.Grade(frac));
        fx = troops.End.X + 10;
        foreach (var f in _sim.Flags)
        {
            var c = f.Owner == null ? new Color(0.85f, 0.85f, 0.82f) : View.SideColor(f.Owner.Value);
            float tw = ThemeDB.FallbackFont.GetStringSize(f.Name, HorizontalAlignment.Left, -1, 11).X;
            if (fx + tw + 22 > Size.X - 230) break;
            DrawLine(new Vector2(fx + 1, 10), new Vector2(fx + 1, 30), new Color(0.1f, 0.09f, 0.07f), 1.5f);
            DrawRect(new Rect2(fx + 2, 10, 11, 8), Colors.Black);
            DrawRect(new Rect2(fx + 3, 11, 9, 6), c);
            Look.Write(this, new Vector2(fx + 16, 18), f.Name, 11, Look.Text);
            Look.Write(this, new Vector2(fx + 16, 30), f.Owner == null ? "nobody" : View.SideName(f.Owner.Value), 9, Look.DimText);
            fx += tw + 26;
        }

        // What is under the mouse, or what the order wants
        var info = new Rect2(Size.X - 228, 7, 214, 25);
        Look.Sunken(this, info);
        DrawMultilineString(ThemeDB.FallbackFont, info.Position + new Vector2(6, 11), _state.Info ?? "", HorizontalAlignment.Left,
            info.Size.X - 10, 10, 2, _state.PendingOrder >= 0 ? Look.Highlight : Look.DimText);
    }

    // ================================================================ the look

    /// <summary>Colours, surfaces and edges shared by the panel, the HUD and the menus.</summary>
    public static class Look
    {
        public static readonly Color Panel = new(0.32f, 0.30f, 0.21f);
        public static readonly Color Inset = new(0.15f, 0.14f, 0.10f);
        public static readonly Color Text = new(0.90f, 0.88f, 0.76f);
        public static readonly Color DimText = new(0.68f, 0.67f, 0.56f);
        public static readonly Color Highlight = new(0.95f, 0.85f, 0.45f);
        public static readonly Color Amber = new(0.98f, 0.72f, 0.30f);
        public static readonly Color Green = new(0.34f, 0.47f, 0.20f);
        public static readonly Color Ochre = new(0.66f, 0.52f, 0.14f);
        public static readonly Color Orange = new(0.74f, 0.38f, 0.10f);
        public static readonly Color Red = new(0.60f, 0.15f, 0.09f);
        public static readonly Color Black = new(0.10f, 0.09f, 0.08f);
        public static readonly Color GoodText = new(0.66f, 0.82f, 0.44f);
        public static readonly Color WarnText = new(0.96f, 0.80f, 0.32f);
        public static readonly Color BadText = new(1.0f, 0.42f, 0.30f);

        /// <summary>Green when whole, through ochre to red as it runs down.</summary>
        public static Color Grade(float frac) => frac > 0.66f ? new Color(0.42f, 0.62f, 0.25f) : frac > 0.33f ? new Color(0.80f, 0.62f, 0.18f) : new Color(0.78f, 0.22f, 0.12f);

        public static void Steel(CanvasItem ci, Rect2 r)
        {
            if (HudSprites.Get()?.Tile(ci, "steel", r) != true) ci.DrawRect(r, Panel);
        }

        /// <summary>A dark recess let into the steel.</summary>
        public static void Well(CanvasItem ci, Rect2 r)
        {
            if (HudSprites.Get()?.Tile(ci, "well", r) != true) ci.DrawRect(r, Inset);
            Bevel(ci, r, raised: false, 2);
        }

        /// <summary>A small sunken window for a readout; darker for the clock.</summary>
        public static void Sunken(CanvasItem ci, Rect2 r, bool dark = false)
        {
            ci.DrawRect(r, dark ? new Color(0.05f, 0.05f, 0.04f) : new Color(0.10f, 0.10f, 0.07f, 0.9f));
            Bevel(ci, r, raised: false, 1);
        }

        public static void Rivets(CanvasItem ci, Rect2 r, float inset)
        {
            var sprites = HudSprites.Get();
            if (sprites == null) return;
            foreach (var p in new[] { r.Position + new Vector2(inset, inset), new Vector2(r.End.X - inset, r.Position.Y + inset),
                         new Vector2(r.Position.X + inset, r.End.Y - inset), r.End - new Vector2(inset, inset) })
                sprites.Draw(ci, "rivet", new Rect2(p - new Vector2(4, 4), new Vector2(8, 8)), Colors.White);
        }

        /// <summary>Light on the top and left edges, shadow on the bottom and right; swapped for a sunken edge.</summary>
        public static void Bevel(CanvasItem ci, Rect2 r, bool raised, int width = 2)
        {
            var light = new Color(1f, 1f, 0.85f, 0.35f);
            var dark = new Color(0f, 0f, 0f, 0.55f);
            var top = raised ? light : dark;
            var bottom = raised ? dark : light;
            for (int i = 0; i < width; i++)
            {
                var a = r.Position + new Vector2(i, i);
                var b = r.End - new Vector2(i + 1, i + 1);
                ci.DrawLine(a, new Vector2(b.X, a.Y), top, 1);
                ci.DrawLine(a, new Vector2(a.X, b.Y), top, 1);
                ci.DrawLine(new Vector2(a.X, b.Y), b, bottom, 1);
                ci.DrawLine(new Vector2(b.X, a.Y), b, bottom, 1);
            }
        }

        /// <summary>A small painted tag with a word on it, like "Healthy" or "Pinned".</summary>
        public static void Badge(CanvasItem ci, Rect2 r, string text, Color fill, float size = 10)
        {
            ci.DrawRect(r, new Color(0, 0, 0, 0.7f));
            var inner = r.Grow(-1);
            ci.DrawRect(inner, fill);
            ci.DrawRect(new Rect2(inner.Position, new Vector2(inner.Size.X, 1)), new Color(1, 1, 0.9f, 0.25f));
            var ink = fill.Luminance > 0.55f ? Black : new Color(0.96f, 0.94f, 0.84f);
            var at = new Vector2(inner.Position.X, inner.Position.Y + inner.Size.Y / 2 + size * 0.36f);
            ci.DrawString(ThemeDB.FallbackFont, at, text, HorizontalAlignment.Center, inner.Size.X, (int)size, ink);
        }

        public static StyleBoxFlat PanelStyle(float alpha = 0.92f) => new()
        {
            BgColor = new Color(Panel, alpha),
            BorderColor = new Color(0.12f, 0.12f, 0.09f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
            ShadowColor = new Color(0, 0, 0, 0.35f),
            ShadowSize = 4,
        };

        public static void Write(CanvasItem ci, Vector2 at, string text, int size, Color color, float width = -1)
        {
            ci.DrawString(ThemeDB.FallbackFont, at + new Vector2(1, 1), text, HorizontalAlignment.Left, width, size, new Color(0, 0, 0, 0.6f));
            ci.DrawString(ThemeDB.FallbackFont, at, text, HorizontalAlignment.Left, width, size, color);
        }

        /// <summary>The colour of one man's square.</summary>
        public static Color ManColor(Soldier s) => s.Health switch
        {
            Health.Dead => new Color(0.12f, 0.11f, 0.10f),
            Health.Incapacitated => new Color(0.45f, 0.12f, 0.10f),
            _ when s.Morale == MoraleState.Surrendered => new Color(0.95f, 0.95f, 0.95f),
            _ when s.Morale == MoraleState.Ready => new Color(0.62f, 0.72f, 0.42f),
            _ => View.MoraleColor(s.Morale),
        };

        // ---- words for what men and teams are doing

        public enum Kit { Rifle, Lmg, Smg, Mg, Hmg, Mortar }

        public static Kit KitOf(Weapon w) =>
            w.IsIndirect ? Kit.Mortar
            : w.Burst >= 8 ? (w.Name.Contains("Colt") ? Kit.Hmg : Kit.Mg)
            : w.Burst >= 3 ? (w.Range < 300 ? Kit.Smg : Kit.Lmg)
            : Kit.Rifle;

        public static string WeaponIcon(Weapon w) => KitOf(w) switch
        {
            Kit.Lmg => "w_lmg", Kit.Smg => "w_smg", Kit.Mg => "w_mg", Kit.Hmg => "w_hmg", Kit.Mortar => "w_mortar", _ => "w_rifle",
        };

        /// <summary>The figure for a team on the list: rifle section, machine gun, mortar, headquarters or ski troops.</summary>
        public static string TeamIcon(Team t)
        {
            if (t.IsHeadquarters) return "team_hq";
            var kits = t.Soldiers.Select(s => KitOf(s.Weapon)).ToList();
            if (kits.Contains(Kit.Mortar)) return "team_mortar";
            if ((kits.Contains(Kit.Mg) || kits.Contains(Kit.Hmg)) && t.Soldiers.Count <= 5) return "team_mg";
            if (t.Nation == "norwegian" && kits.All(k => k == Kit.Rifle)) return "team_ski";
            return "team_rifle";
        }

        /// <summary>His job in the team.</summary>
        public static string Role(Soldier s)
        {
            var team = s.Team;
            if (s.IsLeader)
            {
                var leaders = team.Soldiers.Where(m => m.IsLeader).ToList();
                if (team.IsHeadquarters) return leaders.IndexOf(s) == 0 ? "Officer" : "Sergeant";
                return "Leader";
            }
            var kit = KitOf(s.Weapon);
            if (kit is Kit.Lmg or Kit.Mg or Kit.Hmg) return "Gunner";
            if (kit == Kit.Mortar) return "Mortarman";
            var crew = team.Soldiers.FirstOrDefault(m => KitOf(m.Weapon) is Kit.Mortar or Kit.Mg or Kit.Hmg);
            if (crew != null && team.Soldiers.Count <= 5) return crew.Weapon.IsIndirect ? "Loader" : "Assistant";
            if (team.IsHeadquarters) return "Runner";
            return "Rifleman";
        }

        /// <summary>What a man is doing just now, and its colour.</summary>
        public static (string, Color) Doing(Soldier s, Simulation sim)
        {
            if (s.Health == Health.Dead) return ("KIA", BadText);
            if (s.Health == Health.Incapacitated) return ("Down, hurt", BadText);
            switch (s.Morale)
            {
                case MoraleState.Surrendered: return ("Surrendered", Text);
                case MoraleState.Routed: return ("Fleeing", BadText);
                case MoraleState.Panicked: return ("Panicking", BadText);
                case MoraleState.Cowering: return ("Cowering", WarnText);
                case MoraleState.Pinned: return ("Pinned", WarnText);
            }
            if (s.IsMoving)
                return (s.Team.Mode switch { MoveMode.MoveFast => "Running", MoveMode.Sneak => "Sneaking", _ => "Moving" }, GoodText);
            if (s.BarrageLeft > 0 || sim.Time - s.LastShotTime < 1.5f) return ("Firing", WarnText);
            if (s.Ammo == 0) return ("No ammo", WarnText);
            if (s.Target != null) return ("Aiming", GoodText);
            return (s.Team.Order switch
            {
                TeamOrder.DigIn => s.DigProgress < 1 ? "Digging" : "Dug in",
                TeamOrder.Hide => "Hiding",
                TeamOrder.Defend => "On watch",
                TeamOrder.Fire => s.Weapon.IsIndirect ? "Laying" : "No target",
                _ => "Waiting",
            }, GoodText);
        }

        /// <summary>What a team is doing, for the list and the panel, and its colour.</summary>
        public static (string, Color) TeamDoing(Team t, Simulation sim)
        {
            if (t.IsDestroyed) return (t.Soldiers.All(s => !s.IsAlive) ? "KIA" : "Out of action", BadText);
            var mood = t.Morale;
            if (mood >= MoraleState.Panicked) return (mood == MoraleState.Routed ? "Routed" : "Panicking", BadText);
            if (mood >= MoraleState.Pinned) return (mood == MoraleState.Cowering ? "Cowering" : "Pinned", WarnText);
            string order = t.IsMoving ? t.Mode switch { MoveMode.MoveFast => "Moving fast", MoveMode.Sneak => "Sneaking", _ => "Moving" }
                : t.Order switch
                {
                    TeamOrder.None => "Waiting",
                    TeamOrder.Move or TeamOrder.MoveFast or TeamOrder.Sneak => "In position",
                    TeamOrder.DigIn => sim.DigProgress(t) >= 1 ? "Dug in" : $"Digging {sim.DigProgress(t) * 100:0}%",
                    TeamOrder.Fire => "Firing",
                    _ => t.Order.ToString(),
                };
            if (!t.IsMoving && sim.HouseAt(t.Center) is { } home)
                order += home.State == HouseState.Ruined ? ", ruins" : home.State == HouseState.Standing ? ", house" : "";
            return (order, GoodText);
        }

        /// <summary>The tags for a man: health, morale and fatigue.</summary>
        public static (string, Color) HealthTag(Soldier s) => s.Health switch
        {
            Health.Dead => ("Dead", Black),
            Health.Incapacitated => ("Badly hit", Red),
            Health.Wounded => ("Wounded", Ochre),
            _ => ("Healthy", Green),
        };

        public static (string, Color) MoraleTag(Soldier s) => s.Morale switch
        {
            MoraleState.Pinned => ("Pinned", Ochre),
            MoraleState.Cowering => ("Shaken", Orange),
            MoraleState.Panicked => ("Panicked", Red),
            MoraleState.Routed => ("Broken", Red),
            MoraleState.Surrendered => ("Captured", new Color(0.82f, 0.80f, 0.72f)),
            _ => s.Stress > 0.35f ? ("Nervous", Ochre.Darkened(0.15f)) : ("Steady", Green),
        };

        public static (string, Color) FatigueTag(Soldier s) =>
            s.Fatigue > Simulation.Exhausted ? ("Exhausted", Red)
            : s.Fatigue > 0.35f ? ("Tired", Ochre)
            : ("Rested", Green);
    }

    // ================================================================ buttons

    /// <summary>A small steel button with a painted picture on it.</summary>
    partial class IconButton : Control
    {
        public string Icon;
        public string Tip { set => TooltipText = value; }
        public string Caption = "";
        public bool Lit, Disabled;
        public event Action? Pressed;
        bool _down, _hover;

        public IconButton(string icon, string tip)
        {
            Icon = icon;
            TooltipText = tip;
            MouseFilter = MouseFilterEnum.Stop;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; _down = false; QueueRedraw(); };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            AcceptEvent();
            if (Disabled) return;
            if (mb.Pressed) _down = true;
            else if (_down)
            {
                _down = false;
                Pressed?.Invoke();
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            bool sunk = _down || Lit;
            DrawRect(r, new Color(0, 0, 0, 0.55f));
            var face = r.Grow(-1);
            Look.Steel(this, face);
            if (sunk) DrawRect(face, new Color(0, 0, 0, 0.28f));
            else if (_hover && !Disabled) DrawRect(face, new Color(1, 1, 0.85f, 0.10f));
            Look.Bevel(this, face, raised: !sunk, 2);
            var tint = Disabled ? new Color(0.55f, 0.53f, 0.45f, 0.55f) : Lit ? Look.Highlight : new Color(0.92f, 0.90f, 0.80f);
            var box = face.Grow(-4);
            if (sunk) box.Position += Vector2.One;
            HudSprites.Get()?.DrawFit(this, Icon, box, tint);
            if (Caption.Length > 0)
                Look.Write(this, new Vector2(face.End.X - 15, face.End.Y - 3), Caption, 9, Look.Highlight);
        }
    }

    /// <summary>A wide steel button with a word on it.</summary>
    partial class TextButton : Control
    {
        readonly string _text;
        public event Action? Pressed;
        bool _down, _hover;

        public TextButton(string text, string tip)
        {
            _text = text;
            TooltipText = tip;
            MouseFilter = MouseFilterEnum.Stop;
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; _down = false; QueueRedraw(); };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            AcceptEvent();
            if (mb.Pressed) _down = true;
            else if (_down)
            {
                _down = false;
                Pressed?.Invoke();
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, new Color(0, 0, 0, 0.55f));
            var face = r.Grow(-1);
            Look.Steel(this, face);
            DrawRect(face, _down ? new Color(0, 0, 0, 0.25f) : new Color(0.55f, 0.15f, 0.08f, _hover ? 0.45f : 0.32f));
            Look.Bevel(this, face, raised: !_down, 2);
            var at = new Vector2(face.Position.X, face.Position.Y + face.Size.Y / 2 + 4);
            DrawString(ThemeDB.FallbackFont, at + Vector2.One, _text, HorizontalAlignment.Center, face.Size.X, 11, new Color(0, 0, 0, 0.6f));
            DrawString(ThemeDB.FallbackFont, at, _text, HorizontalAlignment.Center, face.Size.X, 11, Look.Text);
        }
    }

    // ================================================================ the team list

    /// <summary>All your teams, two to a row when there are many: the figure, the name, what it is doing and a cross for its losses.</summary>
    partial class TeamList : Control
    {
        public event Action<Team>? Clicked;
        public Team? Selected;
        Simulation? _sim;
        readonly List<Team> _list = new();
        int _scroll;
        int _hover = -1;
        const float RowH = 30;

        public TeamList()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseExited += () => { _hover = -1; QueueRedraw(); };
        }

        public void Setup(Simulation sim, Side side)
        {
            _sim = sim;
            _list.Clear();
            _list.AddRange(sim.Teams.Where(t => t.Side == side));
            _scroll = 0;
        }

        int Rows => Mathf.Max(1, (int)((Size.Y - 8) / RowH));
        int Cols => _list.Count > Rows ? 2 : 1;
        int MaxScroll => Mathf.Max(0, (_list.Count + Cols - 1) / Cols - Rows);

        Rect2 Cell(int i)
        {
            int row = i / Cols - _scroll, col = i % Cols;
            float w = (Size.X - 8) / Cols;
            return new Rect2(4 + col * w, 4 + row * RowH, w - 1, RowH - 2);
        }

        int At(Vector2 p)
        {
            for (int i = 0; i < _list.Count; i++)
                if (Cell(i).HasPoint(p) && i / Cols - _scroll is var row && row >= 0 && row < Rows) return i;
            return -1;
        }

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
                    if (At(mb.Position) is var i and >= 0) Clicked?.Invoke(_list[i]);
                    AcceptEvent();
                    break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                    _scroll = Mathf.Clamp(_scroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1), 0, MaxScroll);
                    AcceptEvent();
                    QueueRedraw();
                    break;
                case InputEventMouseMotion mm:
                    int h = At(mm.Position);
                    if (h != _hover) { _hover = h; QueueRedraw(); }
                    TooltipText = h >= 0 ? _list[h].Name : "";
                    break;
            }
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            Look.Well(this, r);
            if (_sim == null) return;
            var sprites = HudSprites.Get();
            var font = ThemeDB.FallbackFont;
            for (int i = 0; i < _list.Count; i++)
            {
                int row = i / Cols - _scroll;
                if (row < 0 || row >= Rows) continue;
                var t = _list[i];
                var c = Cell(i);
                bool sel = t == Selected;
                bool gone = t.IsDestroyed;
                if (sel) DrawRect(c, new Color(0.95f, 0.85f, 0.45f, 0.16f));
                else if (i == _hover) DrawRect(c, new Color(1, 1, 0.85f, 0.06f));
                DrawLine(new Vector2(c.Position.X + 2, c.End.Y + 0.5f), new Vector2(c.End.X - 2, c.End.Y + 0.5f), new Color(1, 1, 0.85f, 0.06f));
                if (sel) DrawRect(c, Look.Highlight, false, 1);

                var tint = gone ? new Color(0.45f, 0.42f, 0.36f) : sel ? Look.Highlight : new Color(0.86f, 0.84f, 0.70f);
                sprites?.Draw(this, Look.TeamIcon(t), new Rect2(c.Position + new Vector2(2, 2), new Vector2(39, 26)), tint);

                float tx = c.Position.X + 45, tw = c.Size.X - 45 - 20;
                Look.Write(this, new Vector2(tx, c.Position.Y + 12), t.Name, 11, sel ? Look.Highlight : gone ? Look.DimText : Look.Text, tw);
                var (doing, color) = Look.TeamDoing(t, _sim);
                Look.Write(this, new Vector2(tx, c.Position.Y + 25), doing, 11, color, tw);

                // The cross: green while whole, yellow with men hit, red when half are gone, black when wiped out
                int hurt = t.Soldiers.Count(s => s.Health != Health.Healthy || s.Morale == MoraleState.Surrendered);
                float whole = 1f - (float)hurt / Mathf.Max(1, t.Soldiers.Count);
                var cross = gone ? new Color(0.18f, 0.16f, 0.14f) : hurt == 0 ? new Color(0.45f, 0.72f, 0.30f) : Look.Grade(whole * 0.9f);
                var box = new Rect2(c.End.X - 17, c.Position.Y + 7, 14, 14);
                if (sprites != null) sprites.Draw(this, "cross", box, cross);
                else DrawRect(box, cross);
                if (t.IsHeadquarters)
                    DrawString(font, new Vector2(c.End.X - 17, c.End.Y - 1), "HQ", HorizontalAlignment.Left, -1, 8, Look.Highlight);
            }
            if (MaxScroll > 0)
            {
                float track = Size.Y - 8;
                float knob = track * Rows / ((_list.Count + Cols - 1) / Cols);
                DrawRect(new Rect2(Size.X - 5, 4 + (track - knob) * _scroll / MaxScroll, 3, knob), new Color(0.8f, 0.75f, 0.55f, 0.6f));
            }
        }
    }

    // ================================================================ the selected team

    /// <summary>The selected team: its figure and name, its orders, and a line for each man.</summary>
    partial class TeamPanel : Control
    {
        public Simulation? Sim;
        public Team? Team
        {
            get => _team;
            set { if (value != _team) { _team = value; _scroll = 0; } }
        }
        public int Pending = -1;
        public readonly List<(int Id, IconButton Button)> Orders = new();
        public event Action<Soldier>? ManClicked;
        Team? _team;
        int _scroll;
        const float Head = 48, RowH = 31;

        public TeamPanel() => MouseFilter = MouseFilterEnum.Stop;

        public void LayoutButtons()
        {
            float x = Size.X - Orders.Count * 25 - 5;
            foreach (var (_, b) in Orders)
            {
                b.Position = new Vector2(x, 5);
                b.Size = new Vector2(24, 24);
                x += 25;
            }
        }

        int Visible => Mathf.Max(1, (int)((Size.Y - Head - 4) / RowH));
        int MaxScroll => _team == null ? 0 : Mathf.Max(0, _team.Soldiers.Count - Visible);

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                    _scroll = Mathf.Clamp(_scroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1), 0, MaxScroll);
                    AcceptEvent();
                    QueueRedraw();
                    break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb when _team != null:
                    int i = (int)((mb.Position.Y - Head) / RowH) + _scroll;
                    if (mb.Position.Y > Head && i >= 0 && i < _team.Soldiers.Count) ManClicked?.Invoke(_team.Soldiers[i]);
                    AcceptEvent();
                    break;
            }
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            Look.Well(this, r);
            var sprites = HudSprites.Get();
            if (_team == null || Sim == null)
            {
                Look.Write(this, new Vector2(14, 22), "No team selected", 13, Look.DimText);
                Look.Write(this, new Vector2(14, 64), "Click a man on the map, or a team on the left.", 12, Look.DimText);
                Look.Write(this, new Vector2(14, 84), "Then right-click the ground for orders, or use the buttons above.", 12, Look.DimText);
                return;
            }
            var t = _team;
            // The figure, the name plate and what the team is doing
            var icon = new Rect2(5, 5, 50, 38);
            Look.Sunken(this, icon);
            sprites?.Draw(this, Look.TeamIcon(t), new Rect2(icon.Position + new Vector2(2, 3), new Vector2(46, 31)), Look.Highlight);
            var mood = t.Morale;
            var plateFill = t.IsDestroyed ? Look.Black : mood >= MoraleState.Panicked ? Look.Red : mood >= MoraleState.Pinned ? Look.Ochre : Look.Green;
            Look.Badge(this, new Rect2(60, 5, 136, 17), t.Name, plateFill, 12);
            var (doing, color) = Look.TeamDoing(t, Sim);
            Look.Write(this, new Vector2(62, 38), doing, 12, color, 136);

            // Morale and ammunition lamps
            float ax = 202;
            var (mText, mFill) = t.IsDestroyed ? ("Gone", Look.Black) : mood switch
            {
                MoraleState.Ready => ("Steady", Look.Green),
                MoraleState.Pinned => ("Pinned", Look.Ochre),
                MoraleState.Cowering => ("Shaken", Look.Orange),
                _ => ("Broken", Look.Red),
            };
            Look.Write(this, new Vector2(ax, 16), "Morale", 9, Look.DimText);
            Look.Badge(this, new Rect2(ax + 34, 6, 52, 13), mText, mFill, 9);
            var mortar = t.Soldiers.FirstOrDefault(s => s.Weapon.IsIndirect && s.Health != Health.Dead);
            Look.Write(this, new Vector2(ax, 36), mortar != null ? "Bombs" : "Ammo", 9, Look.DimText);
            if (mortar != null)
                Look.Badge(this, new Rect2(ax + 34, 26, 52, 13), $"{mortar.Ammo}", mortar.Ammo > 5 ? Look.Green : mortar.Ammo > 0 ? Look.Ochre : Look.Red, 9);
            else
                Look.Badge(this, new Rect2(ax + 34, 26, 52, 13), $"{t.AmmoPercent}%", Look.Grade(t.AmmoPercent / 100f), 9);
            if (t.IsHeadquarters)
                Look.Write(this, new Vector2(Size.X - 196, 42), "HQ: calls the guns (right-click)", 9, Look.Highlight);
            DrawLine(new Vector2(4, Head - 1), new Vector2(Size.X - 4, Head - 1), new Color(0, 0, 0, 0.5f));

            // A line for each man
            float w = Size.X - 12;
            for (int i = _scroll; i < t.Soldiers.Count && i - _scroll < Visible; i++)
            {
                var s = t.Soldiers[i];
                float y = Head + (i - _scroll) * RowH;
                bool down = s.Health >= Health.Incapacitated || s.Morale == MoraleState.Surrendered;
                if ((i & 1) == 0) DrawRect(new Rect2(4, y, w, RowH - 1), new Color(1, 1, 0.85f, 0.03f));
                DrawRect(new Rect2(4, y, w, RowH - 1), new Color(1, 1, 0.85f, 0.07f), false, 1);
                DrawRect(new Rect2(9, y + 5, 9, 9), new Color(0, 0, 0, 0.6f));
                DrawRect(new Rect2(10, y + 6, 7, 7), Look.ManColor(s));
                if (s.IsLeader) DrawRect(new Rect2(10, y + 6, 7, 2), Look.Highlight);
                var nameColor = down ? Look.DimText.Darkened(0.2f) : Look.Text;
                Look.Write(this, new Vector2(23, y + 13), s.Name, 11, nameColor, 136);
                Look.Write(this, new Vector2(160, y + 13), Look.Role(s), 10, Look.DimText, 70);
                var dim = new Color(1, 1, 1, down ? 0.45f : 1f);
                var (ht, hf) = Look.HealthTag(s);
                var (mt, mf) = Look.MoraleTag(s);
                var (ft, ff) = Look.FatigueTag(s);
                Look.Badge(this, new Rect2(w - 220, y + 3, 72, 12), ht, hf * dim, 9);
                if (s.Health != Health.Dead)
                {
                    Look.Badge(this, new Rect2(w - 146, y + 3, 72, 12), mt, mf * dim, 9);
                    Look.Badge(this, new Rect2(w - 72, y + 3, 72, 12), ft, ff * dim, 9);
                }

                var (act, actColor) = Look.Doing(s, Sim);
                Look.Write(this, new Vector2(23, y + 27), act, 11, actColor, 132);
                sprites?.Draw(this, Look.WeaponIcon(s.Weapon), new Rect2(158, y + 16, 48, 12),
                    down ? new Color(0.55f, 0.53f, 0.45f) : new Color(0.88f, 0.86f, 0.74f));
                Look.Write(this, new Vector2(212, y + 27), s.Weapon.Name, 10, Look.DimText, w - 290);
                string ammo = s.Weapon.IsIndirect ? $"{s.Ammo} bombs" : $"{s.Ammo} rds";
                var ammoColor = s.Ammo == 0 ? Look.BadText : s.Ammo < s.Weapon.Ammo / 4 ? Look.WarnText : Look.Text;
                DrawString(ThemeDB.FallbackFont, new Vector2(w - 72, y + 27), ammo, HorizontalAlignment.Right, 72, 10, ammoColor);
            }
            if (MaxScroll > 0)
            {
                float track = Size.Y - Head - 4;
                float knob = track * Visible / t.Soldiers.Count;
                DrawRect(new Rect2(Size.X - 5, Head + (track - knob) * _scroll / MaxScroll, 3, knob), new Color(0.8f, 0.75f, 0.55f, 0.6f));
                if (_scroll < MaxScroll)
                    Look.Write(this, new Vector2(Size.X - 120, Size.Y - 4), $"{MaxScroll - _scroll} more: wheel", 9, Look.DimText);
            }
        }
    }

    // ================================================================ the signal pad

    /// <summary>
    /// The reports from your teams, newest at the top, written on a signal pad:
    /// who sent it in pencil, what they say in ink coloured by the kind of news.
    /// The coloured tabs along the top hide or show each kind.
    /// </summary>
    partial class SignalPad : Control
    {
        static readonly (ReportKind Kind, Color Tab, Color Ink, string Tip)[] Kinds =
        {
            (ReportKind.Casualty, new Color(0.80f, 0.16f, 0.10f), new Color(0.62f, 0.08f, 0.05f), "Men killed and hit"),
            (ReportKind.Morale, new Color(0.90f, 0.50f, 0.12f), new Color(0.68f, 0.30f, 0.02f), "Pinned down, panicking, giving up"),
            (ReportKind.Ammo, new Color(0.92f, 0.80f, 0.20f), new Color(0.50f, 0.40f, 0.0f), "Ammunition"),
            (ReportKind.Orders, new Color(0.38f, 0.66f, 0.26f), new Color(0.12f, 0.36f, 0.10f), "Orders, positions and the enemy seen"),
            (ReportKind.General, new Color(0.92f, 0.92f, 0.88f), new Color(0.12f, 0.14f, 0.30f), "The battle's own news"),
        };
        readonly RadioLog _log;
        readonly bool[] _hidden = new bool[5];
        int _scroll;
        int _seenVersion;
        int _seenCount;

        public SignalPad(RadioLog log)
        {
            _log = log;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public void Rewind() => _scroll = 0;

        Rect2 Tab(int i) => new(Size.X - 6 - (Kinds.Length - i) * 23, 5, 20, 11);

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
                    for (int i = 0; i < Kinds.Length; i++)
                        if (Tab(i).Grow(2).HasPoint(mb.Position))
                        {
                            _hidden[i] = !_hidden[i];
                            _scroll = 0;
                            QueueRedraw();
                        }
                    AcceptEvent();
                    break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                    _scroll = Mathf.Max(0, _scroll + (wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1));
                    AcceptEvent();
                    QueueRedraw();
                    break;
                case InputEventMouseMotion mm:
                    TooltipText = "";
                    for (int i = 0; i < Kinds.Length; i++)
                        if (Tab(i).Grow(2).HasPoint(mm.Position))
                            TooltipText = (_hidden[i] ? "Show: " : "Hide: ") + Kinds[i].Tip;
                    break;
            }
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            Look.Well(this, r);
            Look.Write(this, new Vector2(8, 15), "SIGNALS", 10, Look.DimText);
            for (int i = 0; i < Kinds.Length; i++)
            {
                var tab = Tab(i);
                DrawRect(tab, new Color(0, 0, 0, 0.7f));
                DrawRect(tab.Grow(-1), _hidden[i] ? Kinds[i].Tab.Darkened(0.7f) : Kinds[i].Tab);
                if (!_hidden[i]) DrawRect(new Rect2(tab.Position + Vector2.One, new Vector2(tab.Size.X - 2, 2)), new Color(1, 1, 1, 0.3f));
            }

            // The pad itself
            var paper = new Rect2(5, 21, Size.X - 10, Size.Y - 26);
            if (HudSprites.Get()?.Tile(this, "paper", paper) != true) DrawRect(paper, new Color(0.84f, 0.80f, 0.66f));
            const float margin = 34, line = 13;
            for (float y = paper.Position.Y + line + 2; y < paper.End.Y; y += line)
                DrawLine(new Vector2(paper.Position.X, y), new Vector2(paper.End.X, y), new Color(0.35f, 0.45f, 0.65f, 0.18f));
            DrawLine(new Vector2(paper.Position.X + margin, paper.Position.Y), new Vector2(paper.Position.X + margin, paper.End.Y), new Color(0.75f, 0.25f, 0.2f, 0.35f));
            Look.Bevel(this, paper, raised: false, 1);

            // Newest at the top; scrolling with the wheel walks back
            var shown = new List<Report>();
            for (int i = _log.Reports.Count - 1; i >= 0; i--)
                if (!_hidden[(int)_log.Reports[i].Kind]) shown.Add(_log.Reports[i]);
            if (_log.Version != _seenVersion)
            {
                // Keep the place when reading back through older reports
                if (_scroll > 0) _scroll += Mathf.Max(0, shown.Count - _seenCount);
                _seenVersion = _log.Version;
            }
            _seenCount = shown.Count;
            _scroll = Mathf.Min(_scroll, Mathf.Max(0, shown.Count - 1));

            var font = ThemeDB.FallbackFont;
            var pencil = new Color(0.30f, 0.29f, 0.26f);
            float textX = paper.Position.X + margin + 4, textW = paper.End.X - textX - 4;
            float yy = paper.Position.Y + line;
            for (int i = _scroll; i < shown.Count; i++)
            {
                var m = shown[i];
                var ink = Kinds[(int)m.Kind].Ink;
                var size = font.GetMultilineStringSize(m.Text, HorizontalAlignment.Left, textW, 11, 2);
                int lines = Mathf.Clamp((int)Mathf.Round(size.Y / font.GetHeight(11)), 1, 2);
                if (yy + line * lines > paper.End.Y + 2) break;
                DrawString(font, new Vector2(paper.Position.X + 3, yy), $"{(int)m.Time / 60:00}:{(int)m.Time % 60:00}", HorizontalAlignment.Left, -1, 9, pencil);
                DrawString(font, new Vector2(textX, yy), m.From, HorizontalAlignment.Left, textW, 10, new Color(0.18f, 0.17f, 0.14f));
                DrawString(font, new Vector2(textX + 0.6f, yy), m.From, HorizontalAlignment.Left, textW, 10, new Color(0.18f, 0.17f, 0.14f, 0.6f));
                yy += line;
                DrawMultilineString(font, new Vector2(textX, yy), m.Text, HorizontalAlignment.Left, textW, 11, 2, ink);
                yy += line * lines + 3;
            }
            if (_scroll > 0)
                Look.Badge(this, new Rect2(paper.End.X - 92, paper.Position.Y + 2, 88, 12), $"{_scroll} newer above", Look.Ochre, 9);
        }
    }

    // ================================================================ the map

    /// <summary>The whole battlefield in a small square: flags, our men, enemies in sight, and the view.</summary>
    partial class MiniMap : Control
    {
        public event Action<Vector2>? Clicked;
        public Rect2 CameraView;
        public bool RevealAll;

        Simulation? _sim;
        Side _side;
        Texture2D? _ground;
        Vector2 _world; // map size in world screen units
        string _title = "";

        public MiniMap() => MouseFilter = MouseFilterEnum.Stop;

        public void Setup(Simulation sim, Side side, MapData map, Texture2D? ground)
        {
            _sim = sim;
            _side = side;
            _ground = ground;
            _world = new Vector2(map.WorldWidth, map.WorldHeight) * View.Scale;
            _title = sim.Title.Split(',')[0];
        }

        Rect2 Inner
        {
            get
            {
                var r = new Rect2(4, 18, Size.X - 8, Size.Y - 22);
                if (_world.X <= 0) return r;
                float k = Mathf.Min(r.Size.X / _world.X, r.Size.Y / _world.Y);
                var size = _world * k;
                return new Rect2(r.Position + (r.Size - size) / 2, size);
            }
        }

        Vector2 ToMini(System.Numerics.Vector2 metres)
        {
            var inner = Inner;
            return inner.Position + View.ToScreen(metres) / _world * inner.Size;
        }

        public override void _GuiInput(InputEvent e)
        {
            bool press = e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };
            bool drag = e is InputEventMouseMotion mm && (mm.ButtonMask & MouseButtonMask.Left) != 0;
            if (!press && !drag) return;
            var inner = Inner;
            var local = ((InputEventMouse)e).Position;
            Clicked?.Invoke((local - inner.Position) / inner.Size * _world);
            AcceptEvent();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            Look.Well(this, r);
            DrawString(ThemeDB.FallbackFont, new Vector2(0, 14), _title, HorizontalAlignment.Center, Size.X, 11, Look.Text);
            if (_sim == null) return;
            var inner = Inner;
            DrawRect(inner.Grow(1), Colors.Black);
            if (_ground != null) DrawTextureRect(_ground, inner, false);
            else DrawRect(inner, new Color(0.3f, 0.4f, 0.25f));

            foreach (var f in _sim.Flags)
            {
                var c = f.Owner == null ? new Color(0.85f, 0.85f, 0.85f) : View.SideColor(f.Owner.Value);
                var p = ToMini(f.Position);
                DrawLine(p, p - new Vector2(0, 9), Colors.Black, 1.2f);
                DrawRect(new Rect2(p - new Vector2(0, 9), new Vector2(7, 5)), Colors.Black);
                DrawRect(new Rect2(p - new Vector2(-1, 8), new Vector2(5, 3)), c);
            }
            foreach (var team in _sim.Teams)
            {
                bool ours = team.Side == _side;
                foreach (var s in team.Soldiers)
                {
                    if (!s.CanAct) continue;
                    if (!ours && !RevealAll && !s.IsVisibleToEnemy(_sim.Time)) continue;
                    var c = ours ? new Color(0.25f, 0.45f, 0.95f) : new Color(0.95f, 0.25f, 0.15f);
                    var p = ToMini(s.Position);
                    DrawRect(new Rect2(p - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), Colors.Black);
                    DrawRect(new Rect2(p - Vector2.One, new Vector2(2, 2)), c);
                }
            }
            // The part of the map the camera shows
            var view = new Rect2(inner.Position + CameraView.Position / _world * inner.Size, CameraView.Size / _world * inner.Size);
            view = view.Intersection(inner);
            DrawRect(view.Grow(1), new Color(0, 0, 0, 0.6f), false, 1);
            DrawRect(view, new Color(1, 1, 0.9f, 0.95f), false, 1);
        }
    }

    // ================================================================ the close-up

    /// <summary>The ground under the mouse (or the selected team) seen close up, in a window of its own.</summary>
    partial class CloseUp : Control
    {
        public event Action<Vector2>? Clicked;
        const float Zoom = 2.6f;
        SubViewportContainer _frame = null!;
        SubViewport _view = null!;
        Camera2D _camera = null!;
        Vector2 _at;

        public CloseUp() => MouseFilter = MouseFilterEnum.Stop;

        public override void _Ready()
        {
            _frame = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_frame);
            _view = new SubViewport
            {
                HandleInputLocally = false,
                GuiDisableInput = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                AudioListenerEnable2D = false,
            };
            _frame.AddChild(_view);
            _camera = new Camera2D { Zoom = Vector2.One * Zoom };
            _view.AddChild(_camera);
            Resized += Fit;
            Fit();
        }

        void Fit()
        {
            if (_frame == null) return;
            _frame.Position = new Vector2(4, 18);
            _frame.Size = new Vector2(Mathf.Max(1, Size.X - 8), Mathf.Max(1, Size.Y - 22));
        }

        public void Setup()
        {
            // Show the battlefield itself, not a world of its own
            if (_view != null) _view.World2D = GetViewport().World2D;
        }

        public void LookAt(Vector2 at)
        {
            _at = at;
            if (_camera != null) _camera.Position = at;
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            var inner = new Rect2(_frame.Position, _frame.Size);
            Clicked?.Invoke(_at + (mb.Position - inner.GetCenter()) / Zoom);
            AcceptEvent();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            Look.Well(this, r);
            DrawString(ThemeDB.FallbackFont, new Vector2(0, 14), "Close-up", HorizontalAlignment.Center, Size.X, 11, Look.Text);
        }
    }
}
