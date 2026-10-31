using System;
using Godot;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>The screen before a battle: a short briefing and a choice of side.</summary>
public partial class BattleMenu : PanelContainer
{
    public event Action<Side>? SideChosen;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.08f, 0.92f),
            ContentMarginLeft = 28, ContentMarginRight = 28, ContentMarginTop = 22, ContentMarginBottom = 22,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(620, 0) };
        box.AddThemeConstantOverride("separation", 14);
        AddChild(box);

        var title = new Label { Text = "Pothus, 25 May 1940", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);

        box.AddChild(Text(
            "The Germans of 2. Gebirgs-Division are pushing north up Saltdal towards Rognan. " +
            "At Pothus a platoon of the Irish Guards and a Norwegian mortar team hold the bridge " +
            "over the Saltdalselva and the farm beside it. Whoever holds more of the three flags " +
            "after 45 minutes wins; the bridge counts double.", 15));

        box.AddChild(new HSeparator());
        box.AddChild(Choice(Side.Allied, "Command the Irish Guards",
            "Defend. Three rifle sections, platoon HQ and the Norwegian 81 mm mortar. " +
            "The HQ can call a troop of 25-pounders twice, if the signal gets through."));
        box.AddChild(Choice(Side.German, "Command the Gebirgsjäger",
            "Attack. Five squads with MG 34s, the platoon HQ (Zugtrupp) and a mortar. " +
            "The Zugtrupp can call the mountain guns twice and one Stuka."));
    }

    Control Choice(Side side, string title, string text)
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
        row.AddChild(Text(text, 14));
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
            CustomMinimumSize = new Vector2(620, 0),
        };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }
}
