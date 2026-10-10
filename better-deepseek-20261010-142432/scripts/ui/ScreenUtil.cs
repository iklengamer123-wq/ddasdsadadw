using Godot;

namespace CustomRhythia.UI;

/// <summary>
/// Helper for making a Control fill its parent / viewport.
/// Using SetAnchorsPreset alone doesn't work when the Control is a direct
/// child of a CanvasLayer, since the parent rect is undefined.
/// </summary>
public static class ScreenUtil
{
    public static T MakeFullScreen<T>(T control) where T : Control
    {
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        // Belt and suspenders: size to the current viewport right away.
        var vp = control.GetViewport();
        if (vp != null)
        {
            control.Size = vp.GetVisibleRect().Size;
            control.Position = Vector2.Zero;
        }

        // Keep in sync with window resizes / going fullscreen.
        var vpRef = control.GetViewport();
        if (vpRef != null)
        {
            vpRef.SizeChanged += () =>
            {
                if (GodotObject.IsInstanceValid(control))
                {
                    control.Size = vpRef.GetVisibleRect().Size;
                    control.Position = Vector2.Zero;
                }
            };
        }

        return control;
    }
}
