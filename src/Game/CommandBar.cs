using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>
/// The command bar along the bottom of the screen: a card for each of the
/// player's teams, the roster of the selected team, and a mini-map of the
/// whole battlefield. Olive panels with raised and sunken edges, in the manner
/// of a 1990s wargame.
/// </summary>
public partial class CommandBar : Control
{
    public const float BarHeight = 168f;

    public event Action<Team>? TeamClicked;
    /// <summary>The player clicked the mini-map; the argument is a point in world screen units.</summary>
    public event Action<Vector2>? MapClicked;

    readonly List<TeamCard> _cards = new();
    HBoxContainer _cardRow = null!;
    Roster _roster = null!;
    MiniMap _miniMap = null!;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, BarHeight);
        MouseFilter = MouseFilterEnum.Stop;

        var layout = new HBoxContainer();
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.OffsetLeft = 8; layout.OffsetRight = -8; layout.OffsetTop = 8; layout.OffsetBottom = -8;
        layout.AddThemeConstantOverride("separation", 10);
        AddChild(layout);

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        layout.AddChild(scroll);
        _cardRow = new HBoxContainer();
        _cardRow.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_cardRow);

        _roster = new Roster { CustomMinimumSize = new Vector2(400, 0) };
        layout.AddChild(_roster);

        _miniMap = new MiniMap { CustomMinimumSize = new Vector2(BarHeight - 16, 0) };
        _miniMap.Clicked += p => MapClicked?.Invoke(p);
        layout.AddChild(_miniMap);
    }

    public void Build(Simulation sim, Side side, MapData map, Texture2D? ground)
    {
        foreach (var c in _cards) c.QueueFree();
        _cards.Clear();
        foreach (var team in sim.Teams.Where(t => t.Side == side))
        {
            var card = new TeamCard(team);
            card.Clicked += () => TeamClicked?.Invoke(team);
            _cardRow.AddChild(card);
            _cards.Add(card);
        }
        _roster.Team = null;
        _miniMap.Setup(sim, side, map, ground);
    }

    public void Refresh(Team? selected, Rect2 cameraView, bool revealAll)
    {
        foreach (var c in _cards)
        {
            c.Selected = c.Team == selected;
            c.QueueRedraw();
        }
        _roster.Team = selected;
        _roster.QueueRedraw();
        _miniMap.CameraView = cameraView;
        _miniMap.RevealAll = revealAll;
        _miniMap.QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Look.Panel);
        // Fine horizontal grain, like painted steel
        for (float y = 2; y < Size.Y; y += 3)
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), new Color(0, 0, 0, 0.05f));
        Look.Bevel(this, r, raised: true, 3);
    }

    /// <summary>Colours and edges shared by the bar, the HUD and the menus.</summary>
    public static class Look
    {
        public static readonly Color Panel = new(0.30f, 0.31f, 0.23f);
        public static readonly Color Inset = new(0.17f, 0.18f, 0.13f);
        public static readonly Color Text = new(0.90f, 0.88f, 0.76f);
        public static readonly Color DimText = new(0.68f, 0.67f, 0.56f);
        public static readonly Color Highlight = new(0.95f, 0.85f, 0.45f);

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

        /// <summary>The colour of one man's square on a team card.</summary>
        public static Color ManColor(Soldier s) => s.Health switch
        {
            Health.Dead => new Color(0.12f, 0.11f, 0.10f),
            Health.Incapacitated => new Color(0.45f, 0.12f, 0.10f),
            _ when s.Morale == MoraleState.Surrendered => new Color(0.95f, 0.95f, 0.95f),
            _ when s.Morale == MoraleState.Ready => new Color(0.62f, 0.72f, 0.42f),
            _ => View.MoraleColor(s.Morale),
        };

        public static string State(Soldier s) => s.Health switch
        {
            Health.Dead => "Dead",
            Health.Incapacitated => "Badly wounded",
            _ when s.Morale == MoraleState.Surrendered => "Surrendered",
            Health.Wounded => s.Morale == MoraleState.Ready ? "Wounded" : $"Wounded, {s.Morale}",
            _ => s.Morale.ToString(),
        };
    }

    /// <summary>One team: its name, what it is doing, a square for each man and its ammunition.</summary>
    partial class TeamCard : Control
    {
        public readonly Team Team;
        public bool Selected;
        public event Action? Clicked;

        public TeamCard(Team team)
        {
            Team = team;
            CustomMinimumSize = new Vector2(150, BarHeight - 16);
            MouseFilter = MouseFilterEnum.Stop;
            TooltipText = team.Name;
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                Clicked?.Invoke();
                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            bool gone = Team.IsDestroyed;
            DrawRect(r, Selected ? Look.Inset.Lightened(0.12f) : Look.Inset);
            Look.Bevel(this, r, raised: false, 2);
            if (Selected) DrawRect(r.Grow(-3), Look.Highlight, false, 1.5f);

            var text = gone ? Look.DimText.Darkened(0.3f) : Look.Text;
            Look.Write(this, new Vector2(8, 18), Team.Name, 13, Selected ? Look.Highlight : text, Size.X - 14);

            string order = gone ? "Out of action"
                : Team.IsMoving ? Team.Mode switch { MoveMode.MoveFast => "Moving fast", MoveMode.Sneak => "Sneaking", _ => "Moving" }
                : Team.Order == TeamOrder.None ? "Idle" : Team.Order.ToString();
            Look.Write(this, new Vector2(8, 36), order, 12, Look.DimText);
            var mood = Team.Morale;
            Look.Write(this, new Vector2(8, 52), gone ? "" : mood.ToString(), 12,
                mood == MoraleState.Ready ? new Color(0.70f, 0.82f, 0.50f) : View.MoraleColor(mood));

            // A square per man, coloured by his state
            int i = 0;
            foreach (var s in Team.Soldiers)
            {
                var at = new Vector2(8 + (i % 8) * 16, 64 + (i / 8) * 16);
                DrawRect(new Rect2(at, new Vector2(13, 13)), new Color(0, 0, 0, 0.5f));
                DrawRect(new Rect2(at + Vector2.One, new Vector2(11, 11)), Look.ManColor(s));
                if (s.Health == Health.Wounded)
                    DrawRect(new Rect2(at + new Vector2(4, 4), new Vector2(5, 5)), new Color(0.7f, 0.08f, 0.06f));
                if (s.IsLeader)
                    DrawRect(new Rect2(at + new Vector2(1, 1), new Vector2(11, 2)), Look.Highlight);
                i++;
            }

            // Ammunition: bombs for a mortar team, otherwise a bar
            var mortar = Team.Soldiers.FirstOrDefault(s => s.Weapon.IsIndirect && s.Health != Health.Dead);
            float y = Size.Y - 26;
            if (mortar != null)
            {
                Look.Write(this, new Vector2(8, y + 12), $"Bombs {mortar.Ammo}", 12, text);
            }
            else
            {
                Look.Write(this, new Vector2(8, y + 12), "Ammo", 11, Look.DimText);
                var bar = new Rect2(46, y + 2, Size.X - 56, 10);
                DrawRect(bar, new Color(0, 0, 0, 0.6f));
                float pct = Team.AmmoPercent / 100f;
                var fill = pct > 0.5f ? new Color(0.70f, 0.66f, 0.40f) : pct > 0.2f ? new Color(0.90f, 0.70f, 0.25f) : new Color(0.85f, 0.25f, 0.15f);
                DrawRect(new Rect2(bar.Position + Vector2.One, new Vector2((bar.Size.X - 2) * pct, bar.Size.Y - 2)), fill);
            }
            if (Team.IsHeadquarters)
                Look.Write(this, new Vector2(Size.X - 34, 36), "HQ", 12, Look.Highlight);
        }
    }

    /// <summary>The men of the selected team, one line each.</summary>
    partial class Roster : Control
    {
        public Team? Team;

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, Look.Inset);
            Look.Bevel(this, r, raised: false, 2);
            if (Team == null)
            {
                Look.Write(this, new Vector2(12, 24), "No team selected", 13, Look.DimText);
                Look.Write(this, new Vector2(12, 44), "Click a man on the map or a card", 12, Look.DimText);
                return;
            }
            Look.Write(this, new Vector2(12, 20), Team.Name, 14, Look.Highlight);
            float y = 40;
            float step = Mathf.Min(15f, (Size.Y - 44) / Mathf.Max(1, Team.Soldiers.Count - 1));
            foreach (var s in Team.Soldiers)
            {
                bool down = s.Health >= Health.Incapacitated;
                var color = down ? Look.DimText.Darkened(0.25f) : Look.Text;
                DrawRect(new Rect2(12, y - 9, 8, 8), Look.ManColor(s));
                Look.Write(this, new Vector2(26, y), s.Name, 12, color, 150);
                Look.Write(this, new Vector2(180, y), s.Weapon.Name, 12, Look.DimText, 110);
                var stateColor = down ? color : s.Morale == MoraleState.Ready && s.Health == Health.Healthy ? Look.DimText : Look.ManColor(s);
                Look.Write(this, new Vector2(294, y), Look.State(s), 12, stateColor, Size.X - 300);
                y += step;
            }
        }
    }

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

        public MiniMap() => MouseFilter = MouseFilterEnum.Stop;

        public void Setup(Simulation sim, Side side, MapData map, Texture2D? ground)
        {
            _sim = sim;
            _side = side;
            _ground = ground;
            _world = new Vector2(map.WorldWidth, map.WorldHeight) * View.Scale;
        }

        Rect2 Inner
        {
            get
            {
                var r = new Rect2(Vector2.Zero, Size).Grow(-3);
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
            DrawRect(r, Colors.Black);
            Look.Bevel(this, r, raised: false, 2);
            if (_sim == null) return;
            var inner = Inner;
            if (_ground != null) DrawTextureRect(_ground, inner, false);
            else DrawRect(inner, new Color(0.3f, 0.4f, 0.25f));

            foreach (var f in _sim.Flags)
            {
                var c = f.Owner == null ? new Color(0.85f, 0.85f, 0.85f) : View.SideColor(f.Owner.Value);
                var p = ToMini(f.Position);
                DrawRect(new Rect2(p - new Vector2(3, 3), new Vector2(6, 6)), Colors.Black);
                DrawRect(new Rect2(p - new Vector2(2, 2), new Vector2(4, 4)), c);
            }
            foreach (var team in _sim.Teams)
            {
                bool ours = team.Side == _side;
                foreach (var s in team.Soldiers)
                {
                    if (!s.CanAct) continue;
                    if (!ours && !RevealAll && !s.IsVisibleToEnemy(_sim.Time)) continue;
                    var c = ours ? new Color(0.95f, 0.85f, 0.45f) : new Color(0.95f, 0.30f, 0.20f);
                    DrawRect(new Rect2(ToMini(s.Position) - Vector2.One, new Vector2(2, 2)), c);
                }
            }
            // The part of the map the camera shows
            var view = new Rect2(inner.Position + CameraView.Position / _world * inner.Size, CameraView.Size / _world * inner.Size);
            DrawRect(view.Intersection(inner), new Color(1, 1, 1, 0.9f), false, 1);
        }
    }
}
