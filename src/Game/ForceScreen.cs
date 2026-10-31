using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>
/// Choosing forces before a battle: tick the teams and support to bring, within
/// the side's budget of points. The historical force is ticked to begin with.
/// </summary>
public partial class ForceScreen : PanelContainer
{
    public event Action<List<string>>? Confirmed;
    public event Action? Back;

    SideForces _forces = null!;
    readonly Dictionary<string, CheckBox> _boxes = new();
    Label _title = null!, _budget = null!, _problem = null!;
    VBoxContainer _list = null!;
    Button _start = null!;

    public override void _Ready()
    {
        var style = CommandBar.Look.PanelStyle(0.96f);
        style.ContentMarginLeft = style.ContentMarginRight = 24;
        style.ContentMarginTop = style.ContentMarginBottom = 18;
        AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(780, 0) };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        _title = MakeLabel(24);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_title);
        var help = MakeLabel(14, CommandBar.Look.DimText);
        help.Text = "Tick what you bring. Your budget is what the real force cost; leave something out to bring something else.";
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(help);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 470), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);

        _budget = MakeLabel(18);
        box.AddChild(_budget);
        _problem = MakeLabel(14, new Color(1f, 0.55f, 0.35f));
        box.AddChild(_problem);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        box.AddChild(buttons);
        var reset = MakeButton("Historical force");
        reset.Pressed += () => SetPicks(_forces.DefaultPicks);
        buttons.AddChild(reset);
        var back = MakeButton("Back");
        back.Pressed += () => Back?.Invoke();
        buttons.AddChild(back);
        _start = MakeButton("Start battle");
        _start.Pressed += () => Confirmed?.Invoke(Picks());
        buttons.AddChild(_start);
    }

    /// <summary>Fills the list for one side of a battle, ticking the given picks (the historical force if null).</summary>
    public void Show(Scenario scenario, Side side, IReadOnlyCollection<string>? picks, string? heading = null)
    {
        _forces = scenario.Forces(side);
        _title.Text = (heading == null ? "" : heading + "\n") +
                      $"{scenario.Title}: choose the {(side == Side.Allied ? "British" : "German")} force";
        foreach (var child in _list.GetChildren()) child.QueueFree();
        _boxes.Clear();

        AddHeading("Teams");
        foreach (var o in _forces.Options.Where(o => !o.IsSupport)) AddRow(o);
        if (_forces.Options.Any(o => o.IsSupport))
        {
            AddHeading("Off-map support (called by the HQ)");
            foreach (var o in _forces.Options.Where(o => o.IsSupport)) AddRow(o);
        }
        SetPicks(picks ?? _forces.DefaultPicks);
        Visible = true;
    }

    void AddHeading(string text)
    {
        var l = MakeLabel(15, CommandBar.Look.Highlight);
        l.Text = text;
        _list.AddChild(l);
    }

    void AddRow(ForceOption o)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var check = new CheckBox
        {
            Text = o.Name + (o.Default ? "" : "  (reserve)"),
            CustomMinimumSize = new Vector2(300, 0),
            Disabled = o.Required,
            FocusMode = FocusModeEnum.None,
        };
        check.AddThemeColorOverride("font_color", CommandBar.Look.Text);
        check.AddThemeColorOverride("font_disabled_color", CommandBar.Look.Text);
        check.AddThemeFontSizeOverride("font_size", 15);
        check.Toggled += _ => Refresh();
        row.AddChild(check);
        var cost = MakeLabel(15);
        cost.Text = $"{o.Cost} pts";
        cost.CustomMinimumSize = new Vector2(64, 0);
        cost.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(cost);
        var about = MakeLabel(13, CommandBar.Look.DimText);
        about.Text = o.Required ? o.Summary + " Must come." : o.Summary;
        about.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        about.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(about);
        _list.AddChild(row);
        _boxes[o.Name] = check;
    }

    void SetPicks(IEnumerable<string> picks)
    {
        var set = picks.ToHashSet();
        foreach (var (name, box) in _boxes)
            box.SetPressedNoSignal(set.Contains(name) || _forces.Options.First(o => o.Name == name).Required);
        Refresh();
    }

    List<string> Picks() => _boxes.Where(b => b.Value.ButtonPressed).Select(b => b.Key).ToList();

    void Refresh()
    {
        var picks = Picks();
        int cost = _forces.CostOf(picks);
        _budget.Text = $"Points: {cost} of {_forces.Budget}   ({_forces.Budget - cost} left)";
        _budget.AddThemeColorOverride("font_color", cost > _forces.Budget ? new Color(1f, 0.55f, 0.35f) : CommandBar.Look.Text);
        var problem = _forces.Problem(picks);
        _problem.Text = problem ?? "";
        _start.Disabled = problem != null;
    }

    static Label MakeLabel(int size, Color? color = null)
    {
        var l = new Label();
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? CommandBar.Look.Text);
        return l;
    }

    static Button MakeButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(150, 36), FocusMode = FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 16);
        return b;
    }
}
