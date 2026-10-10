using Godot;
using CustomRhythia.Map;

namespace CustomRhythia.UI;

/// <summary>
/// Title screen: Play / Settings / Quit. Handles drag & drop map import.
/// </summary>
public partial class MainMenuScreen : Control
{
    public const string GameVersion = "v0.2";
    public const string GameTagline = "Aim-Based Rhythm Game by kage";

    public override void _Ready()
    {
        ScreenUtil.MakeFullScreen(this);

        AddChild(UiStyle.FullScreen(UiStyle.MakeBackground()));

        // Centered card, sized in pixels via anchors + offsets.
        var card = UiStyle.MakePanel(UiStyle.Surface);
        card.AnchorLeft = 0.5f;
        card.AnchorRight = 0.5f;
        card.AnchorTop = 0.5f;
        card.AnchorBottom = 0.5f;
        card.OffsetLeft = -190;
        card.OffsetRight = 190;
        card.OffsetTop = -210;
        card.OffsetBottom = 210;
        AddChild(card);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 32);
        margin.AddThemeConstantOverride("margin_bottom", 32);
        card.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 14);
        margin.AddChild(vbox);

        var title = UiStyle.MakeLabel("CustomRhythia", 40, UiStyle.TextPrimary);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        var underline = new ColorRect
        {
            Color = UiStyle.Accent,
            CustomMinimumSize = new Vector2(0, 2),
        };
        vbox.AddChild(underline);

        var subtitle = UiStyle.MakeLabel(GameTagline, 13, UiStyle.TextMuted);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(subtitle);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

        var play = UiStyle.MakeButton("PLAY", 20, accent: true);
        play.Pressed += () => Main.Instance.ShowMapSelect();
        vbox.AddChild(play);

        var settings = UiStyle.MakeButton("SETTINGS");
        settings.Pressed += () => Main.Instance.ShowSettings();
        vbox.AddChild(settings);

        var quit = UiStyle.MakeButton("QUIT");
        quit.Pressed += () => GetTree().Quit();
        vbox.AddChild(quit);

        vbox.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var version = UiStyle.MakeLabel(GameVersion + "  -  drop .sspm files to import", 11, UiStyle.TextMuted);
        version.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(version);

        GetWindow().Connect("files_dropped", Callable.From<string[]>(OnFilesDropped));
    }

    private async void OnFilesDropped(string[] files)
    {
        int n = await MapManager.ImportAsync(files);
        if (n > 0)
        {
            GD.Print($"Imported {n} map(s).");
            Main.Instance.ShowMapSelect();
        }
    }
}
