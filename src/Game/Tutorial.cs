using System;
using System.Collections.Generic;
using Godot;

namespace Saltdal.Game;

/// <summary>One step of the tutorial: what to do, how to tell it has been done, and what to note as it begins.</summary>
public sealed record Lesson(string Title, string Text, Func<bool>? Done, Action? Begin = null);

/// <summary>
/// The box at the top right during the practice battle that teaches the
/// controls one at a time. Each lesson moves on by itself once the player has
/// done what it asks; Skip moves on anyway.
/// </summary>
public partial class Tutorial : PanelContainer
{
    IReadOnlyList<Lesson> _lessons = Array.Empty<Lesson>();
    int _index;
    double _wellDone = -1;
    Label _heading = null!, _title = null!, _text = null!;
    Button _skip = null!;

    public bool Active => Visible && _index < _lessons.Count;

    public override void _Ready()
    {
        var style = CommandBar.Look.PanelStyle(0.94f);
        style.ContentMarginLeft = style.ContentMarginRight = 16;
        style.ContentMarginTop = style.ContentMarginBottom = 12;
        style.BorderColor = CommandBar.Look.Highlight;
        style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 1;
        AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        box.AddThemeConstantOverride("separation", 6);
        AddChild(box);
        _heading = Text(12, CommandBar.Look.DimText);
        box.AddChild(_heading);
        _title = Text(19, CommandBar.Look.Highlight);
        box.AddChild(_title);
        _text = Text(14, CommandBar.Look.Text);
        box.AddChild(_text);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        box.AddChild(row);
        _skip = new Button { Text = "Skip", FocusMode = FocusModeEnum.None };
        _skip.Pressed += Next;
        row.AddChild(_skip);
        Visible = false;
    }

    static Label Text(int size, Color color)
    {
        var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    public void Start(IReadOnlyList<Lesson> lessons)
    {
        _lessons = lessons;
        _index = 0;
        _wellDone = -1;
        Visible = true;
        ShowLesson();
    }

    public void Stop() => Visible = false;

    void ShowLesson()
    {
        if (_index >= _lessons.Count)
        {
            Visible = false;
            return;
        }
        var l = _lessons[_index];
        l.Begin?.Invoke();
        _heading.Text = $"TUTORIAL   {_index + 1} of {_lessons.Count}";
        _title.Text = l.Title;
        _text.Text = l.Text;
        _skip.Text = l.Done == null ? (_index == _lessons.Count - 1 ? "Close" : "Next") : "Skip";
    }

    void Next()
    {
        _index++;
        _wellDone = -1;
        ShowLesson();
    }

    public override void _Process(double delta)
    {
        if (!Active) return;
        if (_wellDone >= 0)
        {
            // A moment to see it worked before the next lesson
            if ((_wellDone += delta) > 1.2) Next();
            return;
        }
        var done = _lessons[_index].Done;
        if (done != null && done())
        {
            _wellDone = 0;
            _title.Text = "Well done.";
        }
    }
}
