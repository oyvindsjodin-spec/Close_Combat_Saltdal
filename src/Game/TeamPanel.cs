using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>The strip of team buttons along the bottom of the screen, as in Close Combat.</summary>
public partial class TeamPanel : PanelContainer
{
    public event Action<Team>? TeamClicked;

    readonly Dictionary<Team, Button> _buttons = new();
    HBoxContainer _row = null!;

    public void Build(Simulation sim, Side side)
    {
        foreach (var child in GetChildren()) child.QueueFree();
        _buttons.Clear();
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 6);
        AddChild(_row);
        foreach (var team in sim.Teams.Where(t => t.Side == side))
        {
            var b = new Button
            {
                CustomMinimumSize = new Vector2(190, 64),
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                ClipText = true,
            };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () => TeamClicked?.Invoke(team);
            _row.AddChild(b);
            _buttons[team] = b;
        }
    }

    public void Refresh(Team? selected)
    {
        foreach (var (team, b) in _buttons)
        {
            int total = team.Soldiers.Count;
            int fit = team.Soldiers.Count(s => s.CanAct);
            string state = team.IsDestroyed ? "Out of action" : team.Morale.ToString();
            string order = team.IsMoving ? team.Mode.ToString() : team.Order == TeamOrder.None ? "Idle" : team.Order.ToString();
            b.Text = $"{team.Name}\n{fit}/{total} men   {state}\n{order}   ammo {team.AmmoPercent}%";
            b.Modulate = team == selected ? new Color(1.2f, 1.2f, 1.0f) : team.IsDestroyed ? new Color(0.6f, 0.6f, 0.6f) : Colors.White;
            b.AddThemeColorOverride("font_color", StateColor(team));
        }
    }

    static Color StateColor(Team team) => team.IsDestroyed
        ? new Color(0.6f, 0.6f, 0.6f)
        : team.Morale == MoraleState.Ready ? Colors.White : View.MoraleColor(team.Morale);
}
