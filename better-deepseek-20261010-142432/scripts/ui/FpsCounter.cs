using Godot;

namespace CustomRhythia.UI;

/// <summary>
/// Small top-left FPS label. Updates ~4 times per second so it doesn't
/// flicker. Uses Godot's built-in Engine.GetFramesPerSecond().
/// </summary>
public partial class FpsCounter : Label
{
    private double _accum;

    public FpsCounter()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeFontSizeOverride("font_size", 13);
        AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 1f, 0.55f));
    }

    public override void _Ready()
    {
        Text = "--- FPS";
    }

    public override void _Process(double delta)
    {
        _accum += delta;
        if (_accum < 0.25) return;
        _accum = 0;

        int fps = (int)Engine.GetFramesPerSecond();
        Text = fps + " FPS";
    }
}
