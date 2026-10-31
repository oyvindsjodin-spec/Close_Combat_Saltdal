using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>The screen before a battle: pick one of the battles, read the briefing, choose a side.</summary>
public partial class BattleMenu : PanelContainer
{
    public event Action<Side>? SideChosen;
    public event Action<Scenario>? BattleChosen;

    /// <summary>The battles to offer; set before the menu is added to the scene.</summary>
    public IReadOnlyList<Scenario> Battles { get; set; } = Array.Empty<Scenario>();

    public event Action<Side>? OperationStarted;
    public event Action? OperationContinued;

    /// <summary>Whether an unfinished operation is saved, so it can be continued.</summary>
    public bool HasSavedOperation
    {
        get => _hasSaved;
        set
        {
            _hasSaved = value;
            if (_continue != null) _continue.Visible = value;
        }
    }
    bool _hasSaved;
    Button? _continue;

    public event Action? SavedBattleLoaded;
    /// <summary>What the battle saved with F5 is, such as "Pothus, 25 May 1940, 12:30 in", or null when there is none.</summary>
    public string? SavedBattle
    {
        get => _savedBattle;
        set
        {
            _savedBattle = value;
            if (_load == null) return;
            _load.Visible = value != null;
            _load.Text = $"Continue the saved battle: {value}";
        }
    }
    string? _savedBattle;
    Button? _load;

    readonly List<Button> _battleButtons = new();
    Label _title = null!, _briefing = null!, _alliedText = null!, _germanText = null!;

    public override void _Ready()
    {
        var style = CommandBar.Look.PanelStyle(0.96f);
        style.ContentMarginLeft = style.ContentMarginRight = 28;
        style.ContentMarginTop = style.ContentMarginBottom = 22;
        AddThemeStyleboxOverride("panel", style);
        AddThemeColorOverride("font_color", CommandBar.Look.Text);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(660, 0) };
        box.AddThemeConstantOverride("separation", 14);
        AddChild(box);

        var heading = new Label { Text = "Saltdal, May 1940", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 16);
        heading.Modulate = new Color(1, 1, 1, 0.7f);
        box.AddChild(heading);

        // The battle saved with F5, if there is one
        _load = new Button { CustomMinimumSize = new Vector2(0, 34) };
        _load.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.36f, 0.37f, 0.30f)));
        _load.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.46f, 0.47f, 0.38f)));
        _load.Pressed += () => SavedBattleLoaded?.Invoke();
        box.AddChild(_load);
        SavedBattle = _savedBattle;

        // One button per battle, in the order they were fought
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        foreach (var sc in Battles)
        {
            var b = new Button { Text = sc.Title, ToggleMode = true, CustomMinimumSize = new Vector2(200, 34) };
            b.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.18f, 0.19f, 0.17f)));
            b.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.26f, 0.27f, 0.24f)));
            b.AddThemeStyleboxOverride("pressed", ButtonStyle(new Color(0.36f, 0.37f, 0.30f)));
            b.Pressed += () => Select(sc);
            row.AddChild(b);
            _battleButtons.Add(b);
        }

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(_title);
        _briefing = Text("", 15);
        box.AddChild(_briefing);

        box.AddChild(new HSeparator());
        box.AddChild(Choice(Side.Allied, "Command the British", out _alliedText));
        box.AddChild(Choice(Side.German, "Command the Gebirgsjäger", out _germanText));

        // The whole operation: all the battles in order, losses carried over
        box.AddChild(new HSeparator());
        var op = new Label { Text = "The operation, 23 to 26 May", HorizontalAlignment = HorizontalAlignment.Center };
        op.AddThemeFontSizeOverride("font_size", 22);
        box.AddChild(op);
        box.AddChild(Text("Fight the four historical battles in order, from Viskiskoia to Rognan. Men who come through " +
                          "fight again in the next battle, a little more experienced; the dead and badly wounded do not. " +
                          "The winner of each battle gets more points for the next.", 14));
        var opRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        opRow.AddThemeConstantOverride("separation", 8);
        box.AddChild(opRow);
        foreach (var (side, label) in new[] { (Side.Allied, "As the British"), (Side.German, "As the Germans") })
        {
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(200, 36) };
            var color = View.SideColor(side);
            b.AddThemeStyleboxOverride("normal", ButtonStyle(color.Darkened(0.55f)));
            b.AddThemeStyleboxOverride("hover", ButtonStyle(color.Darkened(0.35f)));
            b.Pressed += () => OperationStarted?.Invoke(side);
            opRow.AddChild(b);
        }
        _continue = new Button { Text = "Continue the operation", CustomMinimumSize = new Vector2(220, 36), Visible = _hasSaved };
        _continue.AddThemeStyleboxOverride("normal", ButtonStyle(new Color(0.36f, 0.37f, 0.30f)));
        _continue.AddThemeStyleboxOverride("hover", ButtonStyle(new Color(0.46f, 0.47f, 0.38f)));
        _continue.Pressed += () => OperationContinued?.Invoke();
        opRow.AddChild(_continue);

        if (Battles.Count > 0) Select(Battles.FirstOrDefault(b => b.Id == "pothus") ?? Battles[0]);
    }

    void Select(Scenario sc)
    {
        for (int i = 0; i < _battleButtons.Count; i++)
            _battleButtons[i].SetPressedNoSignal(Battles[i] == sc);
        _title.Text = sc.Title;
        _briefing.Text = sc.Briefing;
        _alliedText.Text = sc.AlliedRole;
        _germanText.Text = sc.GermanRole;
        BattleChosen?.Invoke(sc);
    }

    Control Choice(Side side, string title, out Label text)
    {
        var row = new VBoxContainer();
        var button = new Button { Text = title, CustomMinimumSize = new Vector2(0, 40) };
        button.AddThemeFontSizeOverride("font_size", 18);
        var color = View.SideColor(side);
        button.AddThemeStyleboxOverride("normal", ButtonStyle(color.Darkened(0.55f)));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(color.Darkened(0.35f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(color.Darkened(0.2f)));
        button.Pressed += () => SideChosen?.Invoke(side);
        row.AddChild(button);
        text = Text("", 14);
        row.AddChild(text);
        return row;
    }

    static StyleBoxFlat ButtonStyle(Color bg) => new()
    {
        BgColor = bg,
        BorderColor = bg.Lightened(0.35f),
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
    };

    static Label Text(string text, int size)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(660, 0),
        };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
