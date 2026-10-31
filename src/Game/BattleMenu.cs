using System;
using System.Collections.Generic;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>The screen before a battle: pick one of the battles, read the briefing, choose a side.</summary>
public partial class BattleMenu : PanelContainer
{
    public event Action<Side>? SideChosen;
    public event Action<Scenario>? BattleChosen;

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

        // One button per battle, in the order they were fought
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        foreach (var sc in Scenarios.All)
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

        Select(Scenarios.Find("pothus"));
    }

    void Select(Scenario sc)
    {
        for (int i = 0; i < _battleButtons.Count; i++)
            _battleButtons[i].SetPressedNoSignal(Scenarios.All[i] == sc);
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
