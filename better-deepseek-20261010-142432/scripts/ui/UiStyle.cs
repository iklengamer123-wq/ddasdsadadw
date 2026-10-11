using Godot;

namespace CustomRhythia.UI;

/// <summary>
/// Shared visual theme. All UI elements pull colors and styleboxes from here
/// so the whole game has a consistent dark-accent look.
/// </summary>
public static class UiStyle
{
    // Palette
    public static readonly Color BgDeep     = new("08080d");
    public static readonly Color Bg          = new("0e0e15");
    public static readonly Color Surface     = new("171722");
    public static readonly Color SurfaceHi   = new("1f1f2e");
    public static readonly Color Border      = new("2a2a3a");

    /// <summary>Primary accent — a clean soft blue.</summary>
    public static readonly Color Accent      = new("4a9eff");
    public static readonly Color AccentDim   = new("4a9eff40");

    public static readonly Color TextPrimary = new("f2f2f5");
    public static readonly Color TextMuted   = new("7a7a8a");
    public static readonly Color Success     = new("4ade80");

    private const int Radius = 10;

    // ---------------------------------------------------------------------
    // Styleboxes
    // ---------------------------------------------------------------------

    public static StyleBoxFlat PanelStyle(Color? bg = null, Color? border = null, int radius = Radius)
    {
        return new StyleBoxFlat
        {
            BgColor = bg ?? Surface,
            BorderColor = border ?? Border,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 20, ContentMarginRight = 20,
            ContentMarginTop = 14, ContentMarginBottom = 14,
        };
    }

    private static StyleBoxFlat ButtonState(Color bg, Color border)
    {
        var sb = PanelStyle(bg, border, 8);
        sb.ContentMarginLeft = 18;
        sb.ContentMarginRight = 18;
        sb.ContentMarginTop = 12;
        sb.ContentMarginBottom = 12;
        return sb;
    }

    // ---------------------------------------------------------------------
    // Factories
    // ---------------------------------------------------------------------

    public static Button MakeButton(string text, int fontSize = 18, bool accent = false)
    {
        var b = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 52),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };

        var normal = ButtonState(accent ? Accent : SurfaceHi, accent ? Accent : Border);
        var hover = ButtonState(accent ? Accent.Lightened(0.08f) : SurfaceHi.Lightened(0.06f),
                                accent ? Accent.Lightened(0.15f) : Accent);
        var pressed = ButtonState(accent ? Accent.Darkened(0.2f) : SurfaceHi.Darkened(0.15f),
                                  accent ? Accent.Darkened(0.2f) : Accent);
        var disabled = ButtonState(Surface.Darkened(0.1f), Border);
        var focus = hover;

        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("disabled", disabled);
        b.AddThemeStyleboxOverride("focus", focus);

        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color", accent ? Colors.White : TextPrimary);
        b.AddThemeColorOverride("font_hover_color", accent ? Colors.White : Colors.White);
        b.AddThemeColorOverride("font_pressed_color", accent ? Colors.White : TextMuted);
        b.AddThemeColorOverride("font_disabled_color", TextMuted.Darkened(0.3f));

        return b;
    }

    public static Label MakeLabel(string text, int fontSize = 20, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", color ?? TextPrimary);
        return l;
    }

    public static Panel MakePanel(Color? color = null, Color? border = null)
    {
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", PanelStyle(color, border));
        return p;
    }

    /// <summary>
    /// Panel with a custom corner radius. Useful for small thumbnails and
    /// compact frames where the default 10px is too round.
    /// </summary>
    public static Panel MakePanel(Color? color, Color? border, int radius)
    {
        var p = new Panel();
        p.AddThemeStyleboxOverride("panel", PanelStyle(color, border, radius));
        return p;
    }

    public static ColorRect MakeBackground()
    {
        return new ColorRect { Color = Bg };
    }

    public static Control FullScreen(Control c)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        c.AnchorRight = 1;
        c.AnchorBottom = 1;
        return c;
    }

    // ---------------------------------------------------------------------
    // Widgets
    // ---------------------------------------------------------------------

    public static HSlider MakeSlider(double step, double min, double max, double value)
    {
        var s = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 24),
        };
        return s;
    }

    public static void StyleListButton(Button b)
    {
        b.FocusMode = Control.FocusModeEnum.None;
        b.Alignment = HorizontalAlignment.Left;
        b.CustomMinimumSize = new Vector2(0, 56);
        b.MouseDefaultCursorShape = Control.CursorShape.PointingHand;

        var normal = ButtonState(Surface, Border);
        var hover = ButtonState(SurfaceHi, Accent);
        var pressed = ButtonState(SurfaceHi.Darkened(0.15f), Accent);
        var focus = hover;

        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("focus", focus);

        b.AddThemeFontSizeOverride("font_size", 17);
        b.AddThemeColorOverride("font_color", TextPrimary);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Accent);
    }
}
