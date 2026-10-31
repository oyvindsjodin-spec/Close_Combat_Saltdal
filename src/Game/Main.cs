using System.Collections.Generic;
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
    const int OrderMove = 0, OrderMoveFast = 1, OrderSneak = 2, OrderFire = 3, OrderDefend = 4, OrderHide = 5, OrderStop = 6, OrderDigIn = 7;
    const int OrderSupportFirst = 100; // + index into Simulation.Support

    MapData _map = null!;
    MapView _mapView = null!;
    GameData _data = null!;
    Scenario _scenario = null!;
    /// <summary>The forces the player chose for this battle and side, kept for "fight again".</summary>
    List<string>? _picks;
    ForceScreen _forceScreen = null!;
    /// <summary>The operation being fought, or null for a single battle.</summary>
    Campaign? _campaign;
    bool _recorded;
    bool _operationDemo;
    double _demoWait;
    const string OperationSave = "user://operation.json";
    string? _loadedMap;
    Simulation? _simOrNull;
    Simulation _sim => _simOrNull!;
    BattleMenu _menu = null!;
    UnitsView _units = null!;
    CameraRig _camera = null!;
    Label _hud = null!;
    PanelContainer _hudBox = null!;
    Label _log = null!;
    PopupMenu _orderMenu = null!;
    int _baseOrderItems;
    GroundworksView _groundworks = null!;
    FireView _fire = null!;
    AimView _aim = null!;
    SoundView _sound = null!;
    /// <summary>Picking a target for a Fire order: the aiming line follows the mouse.</summary>
    bool _aiming;
    /// <summary>Developer check (--follow): the camera keeps the selected team in view.</summary>
    bool _follow;
    CommandBar _panel = null!;
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
        try
        {
            _data = DataLoader.Load();
        }
        catch (GameDataException e)
        {
            // A mistake in a data file: say what and where instead of crashing
            var ui = new CanvasLayer();
            AddChild(ui);
            var msg = new Label { Text = "Could not read the game data.\n\n" + e.Message, Position = new Vector2(40, 40) };
            msg.AddThemeFontSizeOverride("font_size", 18);
            ui.AddChild(msg);
            GD.PushError(e.Message);
            return;
        }
        _scenario = _data.Find("pothus");

        _mapView = new MapView();
        AddChild(_mapView);

        // Trenches, ruins and house floors lie under the men; flames and smoke rise over them
        _groundworks = new GroundworksView();
        AddChild(_groundworks);
        _units = new UnitsView { PlayerSide = PlayerSide };
        AddChild(_units);
        _fire = new FireView();
        AddChild(_fire);
        _aim = new AimView();
        AddChild(_aim);
        _sound = new SoundView();
        AddChild(_sound);

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
                _scenario = _data.Find(a["--battle=".Length..]);
        if (args.Contains("--german")) StartBattle(Side.German);
        else if (args.Contains("--british")) StartBattle(Side.Allied);
        else if (args.Contains("--forces")) ChooseForces(Side.Allied);
        else if (args.Contains("--operation-demo"))
        {
            // Developer check: run the operation through, each battle ended by a ceasefire after a few seconds
            _operationDemo = true;
            StartOperation(Side.Allied);
            StartBattle(PlayerSide);
        }
        else if (args.Contains("--operation")) StartOperation(Side.German);
        else ShowMenu();
        if (args.Contains("--house") && _simOrNull != null)
        {
            // Developer check: the first team runs into the nearest house
            var team = _sim.Teams.First(t => t.Side == PlayerSide);
            var house = _sim.Houses.Where(h => h.Cells.Count >= 6).OrderBy(h => Vec.Distance(h.Center, team.Center)).First();
            _sim.OrderMove(team, house.Center, MoveMode.MoveFast);
            _selected = team;
            _camera.Position = View.ToScreen(house.Center);
        }
        if (args.Contains("--aim") && _simOrNull != null)
        {
            // Developer check: the first team takes aim at a spot towards the enemy
            _selected = _sim.Teams.First(t => t.Side == PlayerSide);
            _sim.EndPrep();
            _aiming = true;
            _camera.Position = View.ToScreen(_selected.Center + new Vec(0, PlayerSide == Side.Allied ? 120 : -120));
            CallDeferred(nameof(AimMouseForCheck));
        }
        _follow = args.Contains("--follow");
        if (args.Contains("--fire") && _simOrNull != null)
        {
            // Developer check: the house nearest the player's men catches fire, and the camera looks at it
            var near = View.ToWorld(_camera.Position);
            var house = _sim.Houses.Where(h => h.Cells.Count >= 4).OrderBy(h => Vec.Distance(h.Center, near)).FirstOrDefault();
            if (house != null)
            {
                _sim.SetOnFire(house);
                _camera.Position = View.ToScreen(house.Center);
            }
        }
        if (args.Contains("--dig") && _simOrNull != null)
            foreach (var t in _sim.Teams.Where(t => t.Side == PlayerSide))
                _sim.OrderDigIn(t, t.Center + new Vec(0, PlayerSide == Side.Allied ? 200 : -200));
        foreach (var a in args)
            if (a.StartsWith("--zoom=") && _simOrNull != null)
            {
                _camera.Zoom = Vector2.One * float.Parse(a["--zoom=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                _selected = _sim.Teams.First(t => t.Side == PlayerSide);
            }
    }

    void ShowMenu()
    {
        _endScreen.Visible = false;
        _panel.Visible = false;
        _hudBox.Visible = false;
        _forceScreen.Visible = false;
        _menu.Visible = true;
    }

    void LoadMap(string name)
    {
        if (_loadedMap == name) return;
        _map = MapLoader.Load($"res://data/maps/{name}");
        _mapView.Build(_map, $"res://data/maps/{name}");
        _camera.Bounds = new Rect2(Vector2.Zero, new Vector2(_map.WorldWidth, _map.WorldHeight) * View.Scale);
        _camera.Position = _camera.Bounds.GetCenter();
        _loadedMap = name;
    }

    void ChooseBattle(Scenario scenario)
    {
        if (_scenario != scenario) _picks = null;
        _scenario = scenario;
        LoadMap(scenario.Map);
    }

    /// <summary>After picking a side: choose what to bring. The last choice for this battle and side is kept.</summary>
    void ChooseForces(Side side)
    {
        if (side != PlayerSide) _picks = null;
        PlayerSide = side;
        _menu.Visible = false;
        _panel.Visible = false;
        _hudBox.Visible = false;
        string? heading = _campaign == null ? null
            : $"The operation, battle {_campaign.Index + 1} of {_data.OperationBattles.Count}";
        _forceScreen.Show(_scenario, side, _picks, heading);
    }

    void StartBattle(Side side)
    {
        LoadMap(_scenario.Map);
        PlayerSide = side;
        _menu.Visible = false;
        _forceScreen.Visible = false;
        _panel.Visible = true;
        _paused = false;
        _recorded = false;
        _hudBox.Visible = true;
        _simOrNull = _scenario.Build(_map, _seed++, side, _picks);
        _units.Sim = _sim;
        _units.PlayerSide = side;
        _groundworks.Sim = _fire.Sim = _aim.Sim = _sim;
        _sound.Sim = _sim;
        _aiming = false;
        _selected = null;
        _accumulator = 0;
        _panel.Build(_sim, PlayerSide, _map, _mapView.Ground);
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

        var hudBox = new PanelContainer { Position = new Vector2(8, 8), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hudBox = hudBox;
        hudBox.AddThemeStyleboxOverride("panel", CommandBar.Look.PanelStyle(0.88f));
        _hud = new Label();
        _hud.AddThemeFontSizeOverride("font_size", 14);
        _hud.AddThemeColorOverride("font_color", CommandBar.Look.Text);
        hudBox.AddChild(_hud);
        ui.AddChild(hudBox);

        _log = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _log.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _log.Position = new Vector2(12, -CommandBar.BarHeight - 100);
        _log.AddThemeFontSizeOverride("font_size", 14);
        _log.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _log.AddThemeConstantOverride("shadow_offset_x", 1);
        _log.AddThemeConstantOverride("shadow_offset_y", 1);
        ui.AddChild(_log);

        _panel = new CommandBar();
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        _panel.OffsetTop = -CommandBar.BarHeight;
        _panel.GrowVertical = Control.GrowDirection.Begin;
        _panel.MapClicked += p => _camera.Position = p;
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
        _orderMenu.AddItem("Dig In", OrderDigIn);
        _orderMenu.AddSeparator();
        _orderMenu.AddItem("Stop", OrderStop);
        _baseOrderItems = _orderMenu.ItemCount;
        _orderMenu.IdPressed += OnOrderChosen;
        _orderMenu.AboutToPopup += FillSupportItems;
        ui.AddChild(_orderMenu);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        ui.AddChild(center);
        _endScreen = new PanelContainer { Visible = false };
        _endScreen.AddThemeStyleboxOverride("panel", CommandBar.Look.PanelStyle(0.95f));
        center.AddChild(_endScreen);
        _endText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _endText.AddThemeFontSizeOverride("font_size", 18);
        _endText.AddThemeColorOverride("font_color", CommandBar.Look.Text);
        var margin = new MarginContainer();
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 24);
        margin.AddChild(_endText);
        _endScreen.AddChild(margin);

        _menu = new BattleMenu { Battles = _data.Battles, HasSavedOperation = LoadOperation() is { IsOver: false } };
        _menu.SideChosen += side =>
        {
            _campaign = null;
            ChooseForces(side);
        };
        _menu.OperationStarted += StartOperation;
        _menu.OperationContinued += () =>
        {
            _campaign = LoadOperation();
            if (_campaign != null) NextOperationBattle();
        };
        _menu.BattleChosen += ChooseBattle;
        center.AddChild(_menu);

        _forceScreen = new ForceScreen { Visible = false };
        _forceScreen.Confirmed += picks =>
        {
            _picks = picks;
            StartBattle(PlayerSide);
        };
        _forceScreen.Back += ShowMenu;
        center.AddChild(_forceScreen);
    }

    public override void _Process(double delta)
    {
        // Between battles (menu or choosing forces) the last battle stays drawn but nothing happens
        if (_simOrNull == null || _menu.Visible || _forceScreen.Visible) return;
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
        if (_follow && _selected != null) _camera.Position = View.ToScreen(_selected.Center);
        _units.Alpha = (float)(_accumulator / Simulation.TickSeconds);
        _units.Selected = _selected;
        _units.Zoom = _camera.Zoom.X;
        _units.QueueRedraw();
        _groundworks.PlayerSide = PlayerSide;
        _groundworks.RevealAll = _units.RevealAll;
        _groundworks.QueueRedraw();
        _fire.Zoom = _camera.Zoom.X;
        if (_aiming && (_selected == null || _selected.IsDestroyed || _sim.IsOver)) _aiming = false;
        _aim.Team = _aiming ? _selected : null;
        _aim.Target = View.ToWorld(GetGlobalMousePosition());
        _aim.Zoom = _camera.Zoom.X;
        _aim.QueueRedraw();
        _sound.Paused = _paused || _sim.IsOver;
        _fire.QueueRedraw();
        var viewSize = GetViewportRect().Size / _camera.Zoom;
        _panel.Refresh(_selected, new Rect2(_camera.GetScreenCenterPosition() - viewSize / 2, viewSize), _units.RevealAll);
        UpdateHud();
        UpdateLog();
        if (_sim.IsOver && _campaign != null && !_recorded)
        {
            _campaign.Record(_sim);
            _recorded = true;
            SaveOperation();
        }
        if (_sim.IsOver && !_endScreen.Visible)
            ShowEndScreen();
        if (_operationDemo)
        {
            if (!_sim.IsOver && _sim.Time > 5) _sim.CallCeasefire();
            else if (_sim.IsOver && _campaign is { IsOver: false } && (_demoWait += delta) > 1)
            {
                _demoWait = 0;
                NextOperationBattle();
                StartBattle(PlayerSide);
            }
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_simOrNull == null || _menu.Visible || _forceScreen.Visible) return;
        if (e is InputEventMouseButton { Pressed: true } mb && !_sim.IsOver)
        {
            var world = View.ToWorld(GetGlobalMousePosition());
            if (_aiming && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
            {
                // Left-click fires at the spot; right-click puts the aim away
                if (mb.ButtonIndex == MouseButton.Left && _selected != null) _sim.OrderFire(_selected, world);
                _aiming = false;
                GetViewport().SetInputAsHandled();
                return;
            }
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
                case Key.Enter or Key.KpEnter when _sim.InPrep:
                    _sim.EndPrep();
                    break;
                case Key.Escape:
                    if (_aiming) _aiming = false;
                    else _selected = null;
                    break;
                case Key.F when _selected != null && !_sim.IsOver:
                    StartAiming();
                    break;
                case Key.Tab:
                    CycleSelection();
                    break;
                case Key.C:
                    // Press C twice to call a ceasefire, so it cannot happen by accident
                    if (CeasefireAsked)
                    {
                        _ceasefireAskedAt = 0;
                        if (_sim.RequestCeasefire(PlayerSide) is { } refusal) _sim.Log(PlayerSide, refusal);
                    }
                    else _ceasefireAskedAt = Time.GetTicksMsec();
                    break;
                case Key.N when _sim.IsOver && _campaign is { IsOver: false }:
                    NextOperationBattle();
                    break;
                case Key.R when _sim.IsOver && _campaign == null:
                    StartBattle(PlayerSide);
                    break;
                case Key.M when _sim.IsOver:
                    ShowMenu();
                    break;
                case Key.F3:
                    // Sound on or off
                    AudioServer.SetBusMute(0, !AudioServer.IsBusMute(0));
                    _sim.Log(PlayerSide, AudioServer.IsBusMute(0) ? "Sound off (F3 to turn it on)." : "Sound on.");
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

    void AimMouseForCheck()
    {
        var spot = View.ToScreen(_selected!.Center + new Vec(60, PlayerSide == Side.Allied ? 260 : -260));
        Input.WarpMouse(GetViewport().GetCanvasTransform() * spot);
    }

    /// <summary>Take aim: the line follows the mouse until the player clicks a target.</summary>
    void StartAiming()
    {
        if (_sim.InPrep && PlayerSide == _sim.Attacker)
        {
            _sim.Log(PlayerSide, "Your men wait for the attack to begin. Press Enter to start it now.");
            return;
        }
        _aiming = true;
    }

    void OnOrderChosen(long id)
    {
        if (_selected == null) return;
        // While the defenders get ready, the attackers wait where they are
        if (_sim.InPrep && PlayerSide == _sim.Attacker && id is OrderMove or OrderMoveFast or OrderSneak or OrderFire)
        {
            _sim.Log(PlayerSide, "Your men wait for the attack to begin. Press Enter to start it now.");
            return;
        }
        switch (id)
        {
            case OrderMove: _sim.OrderMove(_selected, _orderTarget, MoveMode.Move); break;
            case OrderMoveFast: _sim.OrderMove(_selected, _orderTarget, MoveMode.MoveFast); break;
            case OrderSneak: _sim.OrderMove(_selected, _orderTarget, MoveMode.Sneak); break;
            case OrderFire: StartAiming(); break;
            case OrderDefend: _sim.OrderDefend(_selected, _orderTarget); break;
            case OrderHide: _sim.OrderHide(_selected); break;
            case OrderStop: _sim.Stop(_selected); break;
            case OrderDigIn: _sim.OrderDigIn(_selected, _orderTarget); break;
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
        while (_orderMenu.ItemCount > _baseOrderItems)
            _orderMenu.RemoveItem(_orderMenu.ItemCount - 1);
        if (_selected == null || !_selected.IsHeadquarters) return;
        for (int i = 0; i < _sim.Support.Count; i++)
        {
            var a = _sim.Support[i];
            if (a.Side != PlayerSide) continue;
            if (_orderMenu.ItemCount == _baseOrderItems) _orderMenu.AddSeparator();
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
        var map = _sim.Map;
        var (cx, cy) = map.WorldToCell(world);
        string cursor = map.InBounds(cx, cy)
            ? $"{TerrainInfo.Name(map.TerrainAt(cx, cy))}, {map.HeightAt(world):0} m"
            : "off map";
        if (_selected != null && map.InBounds(cx, cy))
        {
            float range = Vec.Distance(_selected.Center, world);
            cursor += $", {range:0} m from {_selected.Name}";
        }
        int left = (int)(_sim.TimeLimit - System.MathF.Max(_sim.Time, _sim.PrepUntil));
        int prep = (int)System.MathF.Ceiling(_sim.PrepUntil - _sim.Time);
        string clock = _sim.InPrep
            ? $"The attack begins in {prep / 60:00}:{prep % 60:00} (Enter: begin now), then {left / 60:00}:{left % 60:00} of battle"
            : $"{left / 60:00}:{left % 60:00} left";
        string status = _paused ? "   PAUSED" : _speed > 1 ? $"   speed x{_speed}" : "";
        if (CeasefireAsked) status += "   Press C again to call a ceasefire";
        string flags = string.Join("   ", _sim.Flags.Select(f => $"{f.Name}: {(f.Owner == null ? "nobody" : View.SideName(f.Owner.Value))}"));
        _hud.Text =
            $"{_sim.Title}    {clock}{status}\n" +
            $"{flags}\n" +
            $"Cursor: {cursor}\n" +
            "Left-click: select   Right-click: orders   F: aim and fire   Tab: next team   WASD: pan   Wheel: zoom   Space: pause   1/2/3: speed   C C: ceasefire";
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

    // ---------------------------------------------------------------- the operation

    void StartOperation(Side side)
    {
        _campaign = new Campaign(_data, side);
        SaveOperation();
        NextOperationBattle();
    }

    /// <summary>Choose forces for the next battle of the operation, as the last one left them.</summary>
    void NextOperationBattle()
    {
        if (_campaign == null || _campaign.IsOver) return;
        _endScreen.Visible = false;
        _scenario = _campaign.Current;
        _picks = null;
        LoadMap(_scenario.Map);
        PlayerSide = _campaign.PlayerSide;
        ChooseForces(PlayerSide);
    }

    string OperationSummary()
    {
        var c = _campaign!;
        var lines = c.Results.Select((b, i) =>
            $"{i + 1}. {b.Title}: {(b.Winner == null ? "draw" : View.SideName(b.Winner.Value) + " victory")}");
        string text = "The operation so far:\n" + string.Join("\n", lines) + "\n\n";
        if (!c.IsOver)
            return text + "Men who came through fight again in the next battle, a little more experienced.\n" +
                   "N: next battle    M: back to the menu (the operation is saved)";
        string verdict = c.Winner == null ? "The operation ends in a draw."
            : c.Winner == Side.Allied ? "British victory: the brigade got away across the fjord in good order."
            : "German victory: the road to Bodø lies open.";
        return text + verdict + (c.Winner == null ? "" : c.Winner == PlayerSide ? " You won the operation." : " You lost the operation.") +
               "\n\nM: back to the menu";
    }

    void SaveOperation()
    {
        if (_campaign == null) return;
        if (_campaign.IsOver)
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(OperationSave));
            _menu.HasSavedOperation = false;
            return;
        }
        using var f = FileAccess.Open(OperationSave, FileAccess.ModeFlags.Write);
        f?.StoreString(_campaign.Save());
        _menu.HasSavedOperation = true;
    }

    Campaign? LoadOperation()
    {
        if (!FileAccess.FileExists(OperationSave)) return null;
        try
        {
            return Campaign.Load(_data, FileAccess.GetFileAsString(OperationSave));
        }
        catch (System.Exception e)
        {
            GD.PushWarning($"Could not read the saved operation: {e.Message}");
            return null;
        }
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
            (_campaign == null ? "R: fight again    M: back to the menu" : OperationSummary());
        _endScreen.Visible = true;
    }
}
