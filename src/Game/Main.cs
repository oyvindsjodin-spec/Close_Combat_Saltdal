using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// The Pothus battle: the player picks a side in the opening menu and the
/// computer commands the other. Runs the simulation in fixed ticks and draws
/// between them.
/// </summary>
public partial class Main : Node2D
{
    Side PlayerSide = Side.Allied;

    // Order menu ids
    const int OrderMove = 0, OrderMoveFast = 1, OrderSneak = 2, OrderFire = 3, OrderDefend = 4, OrderHide = 5, OrderStop = 6;
    const int OrderSupportFirst = 100; // + index into Simulation.Support

    MapData _map = null!;
    MapView _mapView = null!;
    Scenario _scenario = Scenarios.Find("pothus");
    string? _loadedMap;
    Simulation? _simOrNull;
    Simulation _sim => _simOrNull!;
    BattleMenu _menu = null!;
    UnitsView _units = null!;
    CameraRig _camera = null!;
    Label _hud = null!;
    Label _log = null!;
    PopupMenu _orderMenu = null!;
    TeamPanel _panel = null!;
    PanelContainer _endScreen = null!;
    Label _endText = null!;

    Team? _selected;
    Vec _orderTarget;
    double _accumulator;
    bool _paused;
    int _speed = 1;
    ulong _ceasefireAskedAt;  // real time in ms, so it works while paused too
    ulong _seed = 1;

    public override void _Ready()
    {
        _mapView = new MapView();
        AddChild(_mapView);

        _units = new UnitsView { PlayerSide = PlayerSide };
        AddChild(_units);

        _camera = new CameraRig();
        AddChild(_camera);
        _camera.MakeCurrent();

        BuildUi();
        LoadMap(_scenario.Map);

        var args = OS.GetCmdlineUserArgs();
        // Developer switches: `godot -- --reveal` shows every soldier, `--fast` runs at 4x,
        // `--british` or `--german` skips the menu, `--battle=rognan` picks a battle
        if (args.Contains("--reveal")) _units.RevealAll = true;
        if (args.Contains("--fast")) _speed = 4;
        foreach (var a in args)
            if (a.StartsWith("--battle="))
                _scenario = Scenarios.Find(a["--battle=".Length..]);
        if (args.Contains("--german")) StartBattle(Side.German);
        else if (args.Contains("--british")) StartBattle(Side.Allied);
        else ShowMenu();
    }

    void ShowMenu()
    {
        _endScreen.Visible = false;
        _panel.Visible = false;
        _menu.Visible = true;
    }

    void LoadMap(string name)
    {
        if (_loadedMap == name) return;
        _map = MapLoader.Load($"res://data/maps/{name}");
        _mapView.Build(_map);
        _camera.Bounds = new Rect2(Vector2.Zero, new Vector2(_map.WorldWidth, _map.WorldHeight) * View.Scale);
        _camera.Position = _camera.Bounds.GetCenter();
        _loadedMap = name;
    }

    void ChooseBattle(Scenario scenario)
    {
        _scenario = scenario;
        LoadMap(scenario.Map);
    }

    void StartBattle(Side side)
    {
        LoadMap(_scenario.Map);
        PlayerSide = side;
        _menu.Visible = false;
        _panel.Visible = true;
        _paused = false;
        _simOrNull = _scenario.Build(_map, _seed++, side);
        _units.Sim = _sim;
        _units.PlayerSide = side;
        _selected = null;
        _accumulator = 0;
        _panel.Build(_sim, PlayerSide);
        _endScreen.Visible = false;
        var ours = _sim.Teams.Where(t => t.Side == PlayerSide).ToList();
        var center = ours.Aggregate(Vec.Zero, (a, t) => a + t.Center) / ours.Count;
        // Look a little towards the enemy
        _camera.Position = View.ToScreen(center + new Vec(0, side == Side.Allied ? 150 : -150));
        _camera.Zoom = Vector2.One;
    }

    void BuildUi()
    {
        var ui = new CanvasLayer();
        AddChild(ui);

        var hudBox = new PanelContainer { Position = new Vector2(8, 8), MouseFilter = Control.MouseFilterEnum.Ignore };
        hudBox.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.45f),
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4,
        });
        _hud = new Label();
        _hud.AddThemeFontSizeOverride("font_size", 14);
        hudBox.AddChild(_hud);
        ui.AddChild(hudBox);

        _log = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _log.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _log.Position = new Vector2(12, -230);
        _log.AddThemeFontSizeOverride("font_size", 14);
        _log.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _log.AddThemeConstantOverride("shadow_offset_x", 1);
        _log.AddThemeConstantOverride("shadow_offset_y", 1);
        ui.AddChild(_log);

        _panel = new TeamPanel();
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _panel.GrowVertical = Control.GrowDirection.Begin;
        _panel.TeamClicked += team =>
        {
            _selected = team;
            _camera.Position = View.ToScreen(team.Center);
        };
        ui.AddChild(_panel);

        _orderMenu = new PopupMenu();
        _orderMenu.AddItem("Move", OrderMove);
        _orderMenu.AddItem("Move Fast", OrderMoveFast);
        _orderMenu.AddItem("Sneak", OrderSneak);
        _orderMenu.AddSeparator();
        _orderMenu.AddItem("Fire", OrderFire);
        _orderMenu.AddItem("Defend", OrderDefend);
        _orderMenu.AddItem("Hide", OrderHide);
        _orderMenu.AddSeparator();
        _orderMenu.AddItem("Stop", OrderStop);
        _orderMenu.IdPressed += OnOrderChosen;
        _orderMenu.AboutToPopup += FillSupportItems;
        ui.AddChild(_orderMenu);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        ui.AddChild(center);
        _endScreen = new PanelContainer { Visible = false };
        center.AddChild(_endScreen);
        _endText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _endText.AddThemeFontSizeOverride("font_size", 18);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);
        margin.AddChild(_endText);
        _endScreen.AddChild(margin);

        _menu = new BattleMenu();
        _menu.SideChosen += StartBattle;
        _menu.BattleChosen += ChooseBattle;
        center.AddChild(_menu);
    }

    public override void _Process(double delta)
    {
        if (_simOrNull == null) return;
        if (!_paused && !_sim.IsOver)
        {
            _accumulator += delta * _speed;
            // Never run more than a few ticks per frame, so a slow frame cannot snowball
            int ticks = 0;
            while (_accumulator >= Simulation.TickSeconds && ticks++ < 5 * _speed)
            {
                _sim.Tick();
                _accumulator -= Simulation.TickSeconds;
            }
            if (_accumulator > Simulation.TickSeconds) _accumulator = 0;
        }
        if (_selected != null && _selected.IsDestroyed && _selected.Soldiers.All(s => !s.CanAct))
            _selected = null;
        _units.Alpha = (float)(_accumulator / Simulation.TickSeconds);
        _units.Selected = _selected;
        _units.Zoom = _camera.Zoom.X;
        _units.QueueRedraw();
        _panel.Refresh(_selected);
        UpdateHud();
        UpdateLog();
        if (_sim.IsOver && !_endScreen.Visible)
            ShowEndScreen();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_simOrNull == null || _menu.Visible) return;
        if (e is InputEventMouseButton { Pressed: true } mb && !_sim.IsOver)
        {
            var world = View.ToWorld(GetGlobalMousePosition());
            if (mb.ButtonIndex == MouseButton.Left)
            {
                _selected = TeamAt(world);
            }
            else if (mb.ButtonIndex == MouseButton.Right && _selected != null)
            {
                _orderTarget = world;
                _orderMenu.Popup(new Rect2I((Vector2I)GetViewport().GetMousePosition(), Vector2I.Zero));
            }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Space:
                    _paused = !_paused;
                    break;
                case Key.Escape:
                    _selected = null;
                    break;
                case Key.Tab:
                    CycleSelection();
                    break;
                case Key.C:
                    // Press C twice to call a ceasefire, so it cannot happen by accident
                    if (CeasefireAsked) _sim.CallCeasefire();
                    else _ceasefireAskedAt = Time.GetTicksMsec();
                    break;
                case Key.R when _sim.IsOver:
                    StartBattle(PlayerSide);
                    break;
                case Key.M when _sim.IsOver:
                    ShowMenu();
                    break;
                case Key.F2:
                    _units.RevealAll = !_units.RevealAll;
                    break;
                case Key.Key1:
                    _speed = 1;
                    break;
                case Key.Key2:
                    _speed = 2;
                    break;
                case Key.Key3:
                    _speed = 4;
                    break;
            }
        }
    }

    bool CeasefireAsked => _ceasefireAskedAt > 0 && Time.GetTicksMsec() - _ceasefireAskedAt < 3000;

    void CycleSelection()
    {
        var teams = _sim.Teams.Where(t => t.Side == PlayerSide && !t.IsDestroyed).ToList();
        if (teams.Count == 0) return;
        int i = _selected == null ? -1 : teams.IndexOf(_selected);
        _selected = teams[(i + 1) % teams.Count];
        _camera.Position = View.ToScreen(_selected.Center);
    }

    void OnOrderChosen(long id)
    {
        if (_selected == null) return;
        switch (id)
        {
            case OrderMove: _sim.OrderMove(_selected, _orderTarget, MoveMode.Move); break;
            case OrderMoveFast: _sim.OrderMove(_selected, _orderTarget, MoveMode.MoveFast); break;
            case OrderSneak: _sim.OrderMove(_selected, _orderTarget, MoveMode.Sneak); break;
            case OrderFire: _sim.OrderFire(_selected, _orderTarget); break;
            case OrderDefend: _sim.OrderDefend(_selected, _orderTarget); break;
            case OrderHide: _sim.OrderHide(_selected); break;
            case OrderStop: _sim.Stop(_selected); break;
            case >= OrderSupportFirst:
                var asset = _sim.Support[(int)id - OrderSupportFirst];
                var problem = _sim.OrderSupport(_selected, asset, _orderTarget);
                if (problem != null) _sim.Log(PlayerSide, problem);
                break;
        }
    }

    /// <summary>Headquarters teams get one extra menu line per off-map asset they can call.</summary>
    void FillSupportItems()
    {
        while (_orderMenu.ItemCount > 9)
            _orderMenu.RemoveItem(_orderMenu.ItemCount - 1);
        if (_selected == null || !_selected.IsHeadquarters) return;
        for (int i = 0; i < _sim.Support.Count; i++)
        {
            var a = _sim.Support[i];
            if (a.Side != PlayerSide) continue;
            if (_orderMenu.ItemCount == 9) _orderMenu.AddSeparator();
            string state = a.MissionsLeft <= 0 ? "used up" : a.IsAvailable(_sim.Time) ? $"{a.MissionsLeft} left" : "busy";
            _orderMenu.AddItem($"Call {a.Name} ({state})", OrderSupportFirst + i);
            _orderMenu.SetItemDisabled(_orderMenu.ItemCount - 1, !a.IsAvailable(_sim.Time));
        }
    }

    Team? TeamAt(Vec world)
    {
        Team? best = null;
        float bestDist = 8f / _camera.Zoom.X + 2f; // metres; easier to hit when zoomed out
        foreach (var team in _sim.Teams)
        {
            if (team.Side != PlayerSide) continue;
            foreach (var s in team.Soldiers)
            {
                if (!s.CanAct) continue;
                float d = Vec.Distance(s.Position, world);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = team;
                }
            }
        }
        return best;
    }

    void UpdateHud()
    {
        var world = View.ToWorld(GetGlobalMousePosition());
        var (cx, cy) = _map.WorldToCell(world);
        string cursor = _map.InBounds(cx, cy)
            ? $"{TerrainInfo.Name(_map.TerrainAt(cx, cy))}, {_map.HeightAt(world):0} m"
            : "off map";
        if (_selected != null && _map.InBounds(cx, cy))
        {
            float range = Vec.Distance(_selected.Center, world);
            cursor += $", {range:0} m from {_selected.Name}";
        }
        int left = (int)(_sim.TimeLimit - _sim.Time);
        string status = _paused ? "   PAUSED" : _speed > 1 ? $"   speed x{_speed}" : "";
        if (CeasefireAsked) status += "   Press C again to call a ceasefire";
        string flags = string.Join("   ", _sim.Flags.Select(f => $"{f.Name}: {(f.Owner == null ? "nobody" : View.SideName(f.Owner.Value))}"));
        _hud.Text =
            $"{_sim.Title}    {left / 60:00}:{left % 60:00} left{status}\n" +
            $"{flags}\n" +
            $"Cursor: {cursor}\n" +
            "Left-click: select   Right-click: orders   Tab: next team   WASD: pan   Wheel: zoom   Space: pause   1/2/3: speed   C C: ceasefire";
    }

    /// <summary>The last few battle messages meant for the player, fading after 15 seconds.</summary>
    void UpdateLog()
    {
        var lines = _sim.Messages
            .Where(m => (m.ForSide == null || m.ForSide == PlayerSide) && _sim.Time - m.Time < 15f)
            .TakeLast(5)
            .Select(m => $"{(int)m.Time / 60:00}:{(int)m.Time % 60:00}  {m.Text}");
        _log.Text = string.Join("\n", lines);
    }

    void ShowEndScreen()
    {
        var r = _sim.Result!;
        string verdict = r.Winner == null ? "Draw" : $"{View.SideName(r.Winner.Value)} victory";
        if (r.Winner != null) verdict += r.Winner == PlayerSide ? ": you won" : ": you lost";
        string Line(string name, Losses l) =>
            $"{name}: {l.Killed} killed, {l.Incapacitated} badly wounded, {l.Wounded} wounded, {l.Surrendered} captured, of {l.Men}";
        _endText.Text =
            $"{r.Reason}\n\n{verdict}\n" +
            $"Flags held: British {r.AlliedFlagValue}, German {r.GermanFlagValue}\n\n" +
            $"{Line("British", r.AlliedLosses)}\n{Line("German", r.GermanLosses)}\n\n" +
            "R: fight again    M: back to the menu";
        _endScreen.Visible = true;
    }
}
