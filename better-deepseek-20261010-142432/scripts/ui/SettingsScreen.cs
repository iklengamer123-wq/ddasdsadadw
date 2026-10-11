using System;
using System.IO;
using Godot;

namespace CustomRhythia.UI;

/// <summary>
/// Settings overlay. Values persist to the active profile.
/// Compact layout: card is 640x620.
/// </summary>
public partial class SettingsScreen : Control
{
    private Label _hitSoundValueLabel;
    private FileDialog _hitSoundDialog;
    private OptionButton _colorsetDropdown;

    private Label _cursorValueLabel;
    private FileDialog _cursorDialog;

    public override void _Ready()
    {
        ScreenUtil.MakeFullScreen(this);

        var bg = new ColorRect { Color = new Color(0, 0, 0, 0.65f) };
        AddChild(bg);
        UiStyle.FullScreen(bg);

        var card = UiStyle.MakePanel(UiStyle.Surface);
        card.AnchorLeft = 0.5f; card.AnchorRight = 0.5f;
        card.AnchorTop = 0.5f; card.AnchorBottom = 0.5f;
        card.OffsetLeft = -320; card.OffsetRight = 320;
        card.OffsetTop = -310; card.OffsetBottom = 310;
        AddChild(card);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 22);
        margin.AddThemeConstantOverride("margin_right", 22);
        margin.AddThemeConstantOverride("margin_top", 18);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        card.AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        var title = UiStyle.MakeLabel("Settings", 24, UiStyle.Accent);
        root.AddChild(title);

        var rule = new ColorRect { Color = UiStyle.Border, CustomMinimumSize = new Vector2(0, 1) };
        root.AddChild(rule);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(scroll);

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(box);

        // ----- Gameplay -----
        AddSection(box, "Gameplay");

        AddSlider(box, "Sensitivity", 0.01, 0.01, 2.5,
            () => SettingsManager.Current.Sensitivity,
            v => SettingsManager.Current.Sensitivity = v);

        AddSlider(box, "Camera Parallax", 0.01, 0.0, 0.50,
            () => SettingsManager.Current.CameraParallax,
            v => SettingsManager.Current.CameraParallax = v);

        AddSlider(box, "Approach Rate", 0.5, 0.5, 100,
            () => SettingsManager.Current.ApproachRate,
            v => SettingsManager.Current.ApproachRate = v);

        AddSlider(box, "Approach Distance", 0.5, 0.5, 100,
            () => SettingsManager.Current.ApproachDistance,
            v => SettingsManager.Current.ApproachDistance = v);

        AddToggle(box, "Note Pushback",
            () => SettingsManager.Current.Pushback,
            v => SettingsManager.Current.Pushback = v);

        // ----- Visual -----
        AddSection(box, "Visual");

        AddSlider(box, "Note Size", 0.025, 0, 2,
            () => SettingsManager.Current.NoteSize,
            v => SettingsManager.Current.NoteSize = v);

        AddSlider(box, "Note Outline Width", 0.005, 0.02, 0.25,
            () => SettingsManager.Current.NoteOutlineThickness,
            v => SettingsManager.Current.NoteOutlineThickness = v);

        AddSlider(box, "Note Opacity", 0.05, 0, 1,
            () => SettingsManager.Current.NoteOpacity,
            v => SettingsManager.Current.NoteOpacity = v);

        AddColorsetRow(box);

        // ----- Cursor -----
        AddSection(box, "Cursor");

        AddCursorRow(box);

        AddSlider(box, "Cursor Size", 0.05, 0.1, 4.0,
            () => SettingsManager.Current.CursorScale,
            v => SettingsManager.Current.CursorScale = v);

        AddSlider(box, "Cursor Opacity", 0.05, 0, 1,
            () => SettingsManager.Current.CursorOpacity,
            v => SettingsManager.Current.CursorOpacity = v);

        AddSlider(box, "Cursor Rotation (deg/s)", 5, -360, 360,
            () => SettingsManager.Current.CursorRotation,
            v => SettingsManager.Current.CursorRotation = v);

        // ----- Performance -----
        AddSection(box, "Performance");

        AddToggle(box, "Show FPS Counter",
            () => SettingsManager.Current.ShowFpsCounter,
            v =>
            {
                SettingsManager.Current.ShowFpsCounter = v;
                Main.Instance?.ApplyPerformanceSettings();
            });

        AddToggle(box, "Lock FPS",
            () => SettingsManager.Current.LockFps,
            v =>
            {
                SettingsManager.Current.LockFps = v;
                Main.Instance?.ApplyPerformanceSettings();
            });

        AddSlider(box, "FPS Limit", 5, 30, 1000,
            () => SettingsManager.Current.FpsLimit,
            v =>
            {
                SettingsManager.Current.FpsLimit = (int)System.Math.Round(v);
                if (SettingsManager.Current.LockFps)
                    Main.Instance?.ApplyPerformanceSettings();
            });

        // ----- Audio -----
        AddSection(box, "Audio");

        AddSlider(box, "Music Volume", 1, 0, 100,
            () => SettingsManager.Current.VolumeMusic,
            v => SettingsManager.Current.VolumeMusic = v);

        AddSlider(box, "Hit Sound Volume", 1, 0, 100,
            () => SettingsManager.Current.VolumeHitSound,
            v => SettingsManager.Current.VolumeHitSound = v);

        AddHitSoundRow(box);

        // ----- Footer -----
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        root.AddChild(footer);

        var reset = UiStyle.MakeButton("Reset", 13);
        reset.CustomMinimumSize = new Vector2(120, 40);
        reset.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        reset.Pressed += () =>
        {
            SettingsManager.Reset();
            Main.Instance?.ApplyPerformanceSettings();
            QueueFree();
            Main.Instance.ShowSettings();
        };
        footer.AddChild(reset);

        var save = UiStyle.MakeButton("Save & Close", 13, true);
        save.CustomMinimumSize = new Vector2(170, 40);
        save.Pressed += () =>
        {
            SettingsManager.Save();
            QueueFree();
        };
        footer.AddChild(save);

        // File dialogs.
        _hitSoundDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Choose a hit sound (MP3)",
            UseNativeDialog = true,
        };
        _hitSoundDialog.AddFilter("*.mp3", "MP3 audio");
        _hitSoundDialog.FileSelected += OnHitSoundChosen;
        AddChild(_hitSoundDialog);

        _cursorDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Choose a cursor image",
            UseNativeDialog = true,
        };
        _cursorDialog.AddFilter("*.png,*.jpg,*.jpeg,*.webp,*.bmp", "Images");
        _cursorDialog.FileSelected += OnCursorChosen;
        AddChild(_cursorDialog);
    }

    // ---------------------------------------------------------------------
    // Cursor
    // ---------------------------------------------------------------------

    private void AddCursorRow(VBoxContainer parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var lbl = UiStyle.MakeLabel("Cursor Image", 12);
        lbl.CustomMinimumSize = new Vector2(160, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);

        _cursorValueLabel = UiStyle.MakeLabel(CurrentCursorLabel(), 11, UiStyle.TextMuted);
        _cursorValueLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _cursorValueLabel.VerticalAlignment = VerticalAlignment.Center;
        _cursorValueLabel.ClipText = true;
        row.AddChild(_cursorValueLabel);

        var choose = UiStyle.MakeButton("Choose...", 11);
        choose.CustomMinimumSize = new Vector2(90, 28);
        choose.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        choose.Pressed += () => _cursorDialog.PopupCentered(new Vector2I(900, 600));
        row.AddChild(choose);

        var clear = UiStyle.MakeButton("Default", 11);
        clear.CustomMinimumSize = new Vector2(76, 28);
        clear.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        clear.Pressed += () =>
        {
            SettingsManager.Current.CursorFile = "";
            _cursorValueLabel.Text = CurrentCursorLabel();
            SettingsManager.Save();
        };
        row.AddChild(clear);

        var openFolder = UiStyle.MakeButton("Folder", 11);
        openFolder.CustomMinimumSize = new Vector2(80, 28);
        openFolder.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        openFolder.Pressed += () =>
        {
            OS.ShellShowInFileManager(Constants.CursorsFolder);
        };
        row.AddChild(openFolder);

        var hint = UiStyle.MakeLabel(
            "Default: plain white circle. PNG / JPG / WEBP / BMP supported.",
            10, UiStyle.TextMuted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(hint);
    }

    private static string CurrentCursorLabel()
    {
        string name = SettingsManager.Current.CursorFile;
        return string.IsNullOrEmpty(name) ? "Default (circle)" : name;
    }

    private void OnCursorChosen(string path)
    {
        try
        {
            Directory.CreateDirectory(Constants.CursorsFolder);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            string dest = $"{Constants.CursorsFolder}/cursor{ext}";
            File.Copy(path, dest, overwrite: true);

            SettingsManager.Current.CursorFile = Path.GetFileName(dest);
            SettingsManager.Save();
            _cursorValueLabel.Text = CurrentCursorLabel();
        }
        catch (Exception ex)
        {
            GD.PushError("Could not import cursor: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------
    // Colorset
    // ---------------------------------------------------------------------

    private void AddColorsetRow(VBoxContainer parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var lbl = UiStyle.MakeLabel("Note Colorset", 12);
        lbl.CustomMinimumSize = new Vector2(160, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);

        _colorsetDropdown = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28),
        };
        _colorsetDropdown.AddThemeFontSizeOverride("font_size", 12);
        RefreshColorsetList();
        row.AddChild(_colorsetDropdown);

        var openFolder = UiStyle.MakeButton("Folder", 11);
        openFolder.CustomMinimumSize = new Vector2(80, 28);
        openFolder.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        openFolder.Pressed += () =>
        {
            OS.ShellShowInFileManager(Constants.ColorsetsFolder);
        };
        row.AddChild(openFolder);

        var hint = UiStyle.MakeLabel(
            "Drop .txt files with hex colors (e.g. ff0059,ffd8e6) into the Colorsets folder.",
            10, UiStyle.TextMuted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(hint);
    }

    private void RefreshColorsetList()
    {
        _colorsetDropdown.Clear();
        _colorsetDropdown.AddItem("Default");

        try
        {
            Directory.CreateDirectory(Constants.ColorsetsFolder);
            string[] files = Directory.GetFiles(Constants.ColorsetsFolder, "*.txt");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.Equals("default", StringComparison.OrdinalIgnoreCase)) continue;
                _colorsetDropdown.AddItem(name);
            }
        }
        catch { /* ignore */ }

        string cur = SettingsManager.Current.NoteColorset ?? "";
        int selected = 0;
        for (int i = 0; i < _colorsetDropdown.ItemCount; i++)
        {
            string txt = _colorsetDropdown.GetItemText(i);
            if ((cur == "" && txt == "Default") || txt == cur)
            {
                selected = i;
                break;
            }
        }
        _colorsetDropdown.Selected = selected;

        _colorsetDropdown.ItemSelected += (idx) =>
        {
            string val = _colorsetDropdown.GetItemText((int)idx);
            SettingsManager.Current.NoteColorset = val == "Default" ? "" : val;
            SettingsManager.Save();
        };
    }

    // ---------------------------------------------------------------------
    // Hit sound
    // ---------------------------------------------------------------------

    private void AddHitSoundRow(VBoxContainer parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var lbl = UiStyle.MakeLabel("Hit Sound", 12);
        lbl.CustomMinimumSize = new Vector2(160, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);

        _hitSoundValueLabel = UiStyle.MakeLabel(CurrentHitSoundLabel(), 11, UiStyle.TextMuted);
        _hitSoundValueLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _hitSoundValueLabel.VerticalAlignment = VerticalAlignment.Center;
        _hitSoundValueLabel.ClipText = true;
        row.AddChild(_hitSoundValueLabel);

        var choose = UiStyle.MakeButton("Choose...", 11);
        choose.CustomMinimumSize = new Vector2(90, 28);
        choose.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        choose.Pressed += () => _hitSoundDialog.PopupCentered(new Vector2I(900, 600));
        row.AddChild(choose);

        var clear = UiStyle.MakeButton("None", 11);
        clear.CustomMinimumSize = new Vector2(70, 28);
        clear.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        clear.Pressed += () =>
        {
            SettingsManager.Current.HitSoundFile = "";
            _hitSoundValueLabel.Text = CurrentHitSoundLabel();
            SettingsManager.Save();
        };
        row.AddChild(clear);

        var hint = UiStyle.MakeLabel(
            "Default: no hit sound. MP3 is copied into your user folder.",
            10, UiStyle.TextMuted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(hint);
    }

    private static string CurrentHitSoundLabel()
    {
        string name = SettingsManager.Current.HitSoundFile;
        return string.IsNullOrEmpty(name) ? "None" : name;
    }

    private void OnHitSoundChosen(string path)
    {
        try
        {
            Directory.CreateDirectory(Constants.SoundsFolder);
            string dest = $"{Constants.SoundsFolder}/hit.mp3";
            File.Copy(path, dest, overwrite: true);

            SettingsManager.Current.HitSoundFile = "hit.mp3";
            SettingsManager.Save();
            _hitSoundValueLabel.Text = CurrentHitSoundLabel();
        }
        catch (Exception ex)
        {
            GD.PushError("Could not import hit sound: " + ex.Message);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && key.PhysicalKeycode == Key.Escape)
        {
            if (_hitSoundDialog != null && _hitSoundDialog.Visible) return;
            if (_cursorDialog != null && _cursorDialog.Visible) return;
            SettingsManager.Save();
            QueueFree();
        }
    }

    private static void AddSection(VBoxContainer parent, string name)
    {
        parent.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });

        var lbl = UiStyle.MakeLabel(name, 14, UiStyle.Accent);
        parent.AddChild(lbl);

        var rule = new ColorRect
        {
            Color = new Color(UiStyle.Border.R, UiStyle.Border.G, UiStyle.Border.B, 0.6f),
            CustomMinimumSize = new Vector2(0, 1),
        };
        parent.AddChild(rule);
    }

    private static void AddSlider(VBoxContainer parent, string label, double step,
        double min, double max, Func<double> getter, Action<double> setter)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var lbl = UiStyle.MakeLabel(label, 12);
        lbl.CustomMinimumSize = new Vector2(160, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);

        var slider = UiStyle.MakeSlider(step, min, max, getter());
        slider.CustomMinimumSize = new Vector2(0, 20);
        row.AddChild(slider);

        var valueLabel = UiStyle.MakeLabel(getter().ToString("F2"), 12, UiStyle.Accent);
        valueLabel.CustomMinimumSize = new Vector2(60, 0);
        valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
        valueLabel.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(valueLabel);

        slider.ValueChanged += v =>
        {
            setter(v);
            valueLabel.Text = v.ToString("F2");
        };
    }

    private static void AddToggle(VBoxContainer parent, string label,
        Func<bool> getter, Action<bool> setter)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var lbl = UiStyle.MakeLabel(label, 12);
        lbl.CustomMinimumSize = new Vector2(160, 0);
        lbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(lbl);

        var check = new CheckButton
        {
            ButtonPressed = getter(),
            FocusMode = Control.FocusModeEnum.None,
        };
        check.Toggled += v => setter(v);
        row.AddChild(check);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);
    }
}
