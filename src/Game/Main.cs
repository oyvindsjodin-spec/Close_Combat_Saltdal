using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>
/// Phase 0 test battle at Pothus: load the map, place a few teams and let the
/// player move them. Runs the simulation in fixed ticks and draws between them.
/// </summary>
public partial class Main : Node2D
{
    const string MapFolder = "res://data/maps/pothus";

    Simulation _sim = null!;
    UnitsView _units = null!;
    CameraRig _camera = null!;
    Label _hud = null!;
    PopupMenu _orderMenu = null!;

    Team? _selected;
    Vec _orderTarget;
    double _accumulator;
    bool _paused;

    public override void _Ready()
    {
        var map = MapLoader.Load(MapFolder);
        _sim = new Simulation(map);
        SpawnTestForces(map);

        var mapView = new MapView();
        mapView.Build(map);
        AddChild(mapView);

        _units = new UnitsView { Sim = _sim };
        AddChild(_units);

        var size = new Vector2(map.WorldWidth, map.WorldHeight) * View.Scale;
        _camera = new CameraRig { Bounds = new Rect2(Vector2.Zero, size), Position = size / 2 };
        AddChild(_camera);
        _camera.MakeCurrent();

        var ui = new CanvasLayer();
        AddChild(ui);
        _hud = new Label { Position = new Vector2(12, 8) };
        _hud.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _hud.AddThemeConstantOverride("shadow_offset_x", 1);
        _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        ui.AddChild(_hud);

        _orderMenu = new PopupMenu();
        _orderMenu.AddItem("Move", (int)MoveMode.Move);
        _orderMenu.AddItem("Move Fast", (int)MoveMode.MoveFast);
        _orderMenu.AddItem("Sneak", (int)MoveMode.Sneak);
        _orderMenu.AddSeparator();
        _orderMenu.AddItem("Stop", 100);
        _orderMenu.IdPressed += OnOrderChosen;
        ui.AddChild(_orderMenu);

        // Developer check: `godot -- --demo` sends the German squad north on its own
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--demo") >= 0)
        {
            _selected = _sim.Teams[3];
            _sim.OrderMove(_selected, FindOpenGround(map, 0.35f, 0.5f), MoveMode.MoveFast);
            _camera.Position = View.ToScreen(_selected.Center);
            _camera.Zoom = Vector2.One * 1.5f;
        }
    }

    void SpawnTestForces(MapData map)
    {
        // Placeholder forces near the road: British to the north, Germans to the south.
        // Names are invented for testing.
        _sim.SpawnTeam("Irish Gds, 1 Sec", Side.Allied, FindOpenGround(map, 0.3f, 0.45f),
            new[] { "Cpl Byrne", "Gdsm Walsh", "Gdsm Doyle", "Gdsm Ryan", "Gdsm Kelly", "Gdsm Nolan", "Gdsm Quinn", "Gdsm Burke" });
        _sim.SpawnTeam("Irish Gds, 2 Sec", Side.Allied, FindOpenGround(map, 0.3f, 0.6f),
            new[] { "LCpl Moran", "Gdsm Flynn", "Gdsm Hayes", "Gdsm Lynch", "Gdsm Daly", "Gdsm Power", "Gdsm Casey" });
        _sim.SpawnTeam("Bren team", Side.Allied, FindOpenGround(map, 0.25f, 0.52f),
            new[] { "Gdsm Duffy", "Gdsm Keane" });
        _sim.SpawnTeam("1. Gruppe", Side.German, FindOpenGround(map, 0.8f, 0.45f),
            new[] { "Uffz. Huber", "Gefr. Mayr", "Jg. Gruber", "Jg. Hofer", "Jg. Wimmer", "Jg. Brandl", "Jg. Lechner", "Jg. Stadler", "Jg. Moser" });
        _sim.SpawnTeam("MG 34 Trupp", Side.German, FindOpenGround(map, 0.85f, 0.5f),
            new[] { "Gefr. Pichler", "Jg. Aigner", "Jg. Reiter" });
    }

    /// <summary>Nearest passable cell to a point given as fractions of the map size.</summary>
    static Vec FindOpenGround(MapData map, float fy, float fx)
    {
        int cx = (int)(fx * map.Width), cy = (int)(fy * map.Height);
        for (int r = 0; r < 100; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (map.IsPassable(x, y) && map.TerrainAt(x, y) != TerrainType.Rock)
                        return map.CellCenter(x, y);
                }
        return map.CellCenter(cx, cy);
    }

    public override void _Process(double delta)
    {
        if (!_paused)
        {
            _accumulator += delta;
            // Never run more than a few ticks per frame, so a slow frame cannot snowball
            int ticks = 0;
            while (_accumulator >= Simulation.TickSeconds && ticks++ < 5)
            {
                _sim.Tick();
                _accumulator -= Simulation.TickSeconds;
            }
        }
        _units.Alpha = (float)(_accumulator / Simulation.TickSeconds);
        _units.Selected = _selected;
        _units.Zoom = _camera.Zoom.X;
        _units.QueueRedraw();
        UpdateHud();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: true } mb)
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
            if (key.Keycode == Key.Space)
                _paused = !_paused;
            else if (key.Keycode == Key.Escape)
                _selected = null;
        }
    }

    void OnOrderChosen(long id)
    {
        if (_selected == null) return;
        if (id == 100)
            _sim.Stop(_selected);
        else
            _sim.OrderMove(_selected, _orderTarget, (MoveMode)id);
    }

    Team? TeamAt(Vec world)
    {
        Team? best = null;
        float bestDist = 6f; // metres
        foreach (var team in _sim.Teams)
            foreach (var s in team.Soldiers)
            {
                float d = Vec.Distance(s.Position, world);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = team;
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
        string selected = _selected == null
            ? "Left-click a team to select it"
            : $"{_selected.Name} ({_selected.Soldiers.Count} men, {_selected.Mode}{(_selected.IsMoving ? ", moving" : "")}). Right-click to give an order";
        int seconds = (int)_sim.Time;
        _hud.Text =
            $"Pothus, 25 May 1940    {seconds / 60:00}:{seconds % 60:00}{(_paused ? "   PAUSED" : "")}\n" +
            $"{selected}\n" +
            $"Cursor: {cursor}\n" +
            "WASD or arrows: pan   Wheel: zoom   Middle drag: pan   Space: pause   Esc: deselect";
    }
}
