using System;
using Godot;

namespace CustomRhythia.UI;

/// <summary>
/// Simple pause overlay. Owned by GameWorld and shown on top of the game.
/// </summary>
public partial class PauseMenu : Control
{
    public Action OnResume;
    public Action OnRestart;
    public Action OnQuit;

    private Button _resumeButton;

    public PauseMenu() { }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Dim backdrop
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var card = UiStyle.MakePanel(UiStyle.Surface);
        card.AnchorLeft = 0.5f; card.AnchorRight = 0.5f;
        card.AnchorTop = 0.5f; card.AnchorBottom = 0.5f;
        card.OffsetLeft = -180; card.OffsetRight = 180;
        card.OffsetTop = -170; card.OffsetBottom = 170;
        AddChild(card);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        card.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 14);
        margin.AddChild(box);

        var title = UiStyle.MakeLabel("Paused", 32, UiStyle.Accent);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);

        var rule = new ColorRect { Color = UiStyle.Border, CustomMinimumSize = new Vector2(0, 1) };
        box.AddChild(rule);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        _resumeButton = UiStyle.MakeButton("Resume", 18, true);
        _resumeButton.CustomMinimumSize = new Vector2(0, 52);
        _resumeButton.Pressed += () => OnResume?.Invoke();
        box.AddChild(_resumeButton);

        var restart = UiStyle.MakeButton("Restart", 18);
        restart.CustomMinimumSize = new Vector2(0, 52);
        restart.Pressed += () => OnRestart?.Invoke();
        box.AddChild(restart);

        var quit = UiStyle.MakeButton("Quit to menu", 18);
        quit.CustomMinimumSize = new Vector2(0, 52);
        quit.Pressed += () => OnQuit?.Invoke();
        box.AddChild(quit);

        var hint = UiStyle.MakeLabel("Esc to resume", 11, UiStyle.TextMuted);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(hint);
    }

    public void SetFocus(bool focused)
    {
        if (focused && _resumeButton != null)
        {
            _resumeButton.GrabFocus();
        }
    }
}
