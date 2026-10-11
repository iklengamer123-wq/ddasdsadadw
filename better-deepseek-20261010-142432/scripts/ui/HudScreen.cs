using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using CustomRhythia.Game;
using CustomRhythia.Map;

namespace CustomRhythia.UI;

/// <summary>
/// In-game overlay.
///   Top center          - map title, time
///   Left middle edge    - rank (with "rank" tag), combo
///   Right of the grid   - accuracy (upper) and misses (lower)
///   Under the grid      - health bar (follows the grid)
///   Bottom center       - "Модификаторы:" + active mods, fixed on screen
///                         (independent of camera parallax)
///
/// No countdown numbers: starting from a timer just means the notes come
/// in normally from the chosen spot, no "3.. 2.. 1.." text overlay.
/// </summary>
public partial class HudScreen : Control
{
    private Runner _runner;
    private bool _subscribed = false;

    private Label _combo;
    private Label _comboTag;
    private Label _accuracy;
    private Label _misses;
    private Label _time;
    private Label _title;
    private Label _rank;
    private Label _rankTag;

    private Label _modsLabel;
    private Label _modsValue;

    private Panel _healthBg;
    private ColorRect _healthFill;

    private Label _accuracyTag;
    private Label _missesTag;

    private const float HealthWidth = 260f;
    private const float HealthHeight = 8f;
    private const float ModsBlockWidth = 520f;

    private const int BigLabelSize = 44;

    public Camera3D Cam { get; set; }

    public HudScreen() { }

    public void Bind(Runner runner)
    {
        _runner = runner;
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        if (_runner == null) return;
        _runner.StatsUpdated += Refresh;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (_runner != null) _runner.StatsUpdated -= Refresh;
        _subscribed = false;
    }

    public override void _Ready()
    {
        ScreenUtil.MakeFullScreen(this);
        MouseFilter = MouseFilterEnum.Ignore;

        _title = UiStyle.MakeLabel("", 15, UiStyle.TextMuted);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _title.OffsetTop = 16;
        _title.OffsetBottom = 40;
        AddChild(_title);

        _time = UiStyle.MakeLabel("0:00 / 0:00", 22, UiStyle.TextPrimary);
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        _time.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _time.OffsetTop = 42;
        _time.OffsetBottom = 76;
        AddChild(_time);

        _rank = UiStyle.MakeLabel("SS", BigLabelSize, UiStyle.TextPrimary);
        _rank.HorizontalAlignment = HorizontalAlignment.Left;
        _rank.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_rank);

        _rankTag = UiStyle.MakeLabel("rank", 11, UiStyle.TextMuted);
        _rankTag.HorizontalAlignment = HorizontalAlignment.Left;
        _rankTag.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_rankTag);

        _combo = UiStyle.MakeLabel("0", BigLabelSize, UiStyle.TextPrimary);
        _combo.HorizontalAlignment = HorizontalAlignment.Left;
        _combo.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_combo);

        _comboTag = UiStyle.MakeLabel("combo", 11, UiStyle.TextMuted);
        _comboTag.HorizontalAlignment = HorizontalAlignment.Left;
        _comboTag.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_comboTag);

        _accuracy = UiStyle.MakeLabel("100.00%", 26, UiStyle.TextPrimary);
        _accuracy.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_accuracy);

        _accuracyTag = UiStyle.MakeLabel("accuracy", 10, UiStyle.TextMuted);
        _accuracyTag.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_accuracyTag);

        _misses = UiStyle.MakeLabel("0", 26, UiStyle.TextPrimary);
        _misses.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_misses);

        _missesTag = UiStyle.MakeLabel("misses", 10, UiStyle.TextMuted);
        _missesTag.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_missesTag);

        _healthBg = UiStyle.MakePanel(new Color(0, 0, 0, 0.55f), UiStyle.Border);
        _healthBg.CustomMinimumSize = new Vector2(HealthWidth, HealthHeight);
        _healthBg.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_healthBg);

        _healthFill = new ColorRect { Color = UiStyle.Accent };
        _healthFill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _healthBg.AddChild(_healthFill);

        _modsLabel = UiStyle.MakeLabel("Модификаторы:", 12,
            new Color(UiStyle.TextPrimary.R, UiStyle.TextPrimary.G, UiStyle.TextPrimary.B, 0.35f));
        _modsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _modsLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _modsLabel.Size = new Vector2(ModsBlockWidth, 18);
        AddChild(_modsLabel);

        _modsValue = UiStyle.MakeLabel("", 15,
            new Color(UiStyle.TextPrimary.R, UiStyle.TextPrimary.G, UiStyle.TextPrimary.B, 0.85f));
        _modsValue.HorizontalAlignment = HorizontalAlignment.Center;
        _modsValue.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _modsValue.Size = new Vector2(ModsBlockWidth, 22);
        AddChild(_modsValue);

        Subscribe();
        Refresh();
        UpdateTitleAndMods();
    }

    public override void _ExitTree()
    {
        Unsubscribe();
    }

    public override void _Process(double delta)
    {
        if (_runner?.Attempt == null) return;
        var a = _runner.Attempt;

        double cur = System.Math.Max(0, a.Progress) / 1000.0;
        double total = a.Map.Length / 1000.0;
        _time.Text = Util.Strings.FormatTime(cur) + " / " + Util.Strings.FormatTime(total);

        PositionAnchoredLabels();
    }

    private void UpdateTitleAndMods()
    {
        if (_runner?.Attempt == null) return;

        var a = _runner.Attempt;
        _title.Text = a.Map.PrettyTitle;

        var parts = new List<string>();

        if (Math.Abs(a.Speed - 1.0) > 0.001)
        {
            string tag = SpeedTag(a.Speed);
            string sp = a.Speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
            parts.Add(tag == sp ? sp : tag + " (" + sp + ")");
        }

        if (a.NoFail)
        {
            parts.Add("NoFail");
        }

        if (parts.Count == 0)
        {
            _modsLabel.Visible = false;
            _modsValue.Visible = false;
            _modsValue.Text = "";
        }
        else
        {
            _modsLabel.Visible = true;
            _modsValue.Visible = true;
            _modsValue.Text = string.Join("   ·   ", parts);
        }
    }

    private static string SpeedTag(double speed)
    {
        if (Math.Abs(speed - 0.65) < 0.001) return "S----";
        if (Math.Abs(speed - 0.75) < 0.001) return "S---";
        if (Math.Abs(speed - 0.80) < 0.001) return "S--";
        if (Math.Abs(speed - 0.87) < 0.001) return "S-";
        if (Math.Abs(speed - 1.00) < 0.001) return "NM";
        if (Math.Abs(speed - 1.15) < 0.001) return "S+";
        if (Math.Abs(speed - 1.25) < 0.001) return "S++";
        if (Math.Abs(speed - 1.35) < 0.001) return "S+++";
        if (Math.Abs(speed - 1.45) < 0.001) return "S++++";
        return speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
    }

    private void PositionAnchoredLabels()
    {
        var vp = GetViewport().GetVisibleRect().Size;

        float leftX = 32f;
        float centerY = vp.Y * 0.5f;

        float rankY = centerY - 110f;
        _rank.Position = new Vector2(leftX, rankY);
        _rankTag.Position = new Vector2(leftX + 4f, rankY + 48f);

        float comboY = centerY + 5f;
        _combo.Position = new Vector2(leftX, comboY);
        _comboTag.Position = new Vector2(leftX + 2f, comboY + 48f);

        if (Cam == null) return;

        var gridTopRight = Cam.UnprojectPosition(new Vector3(1.5f, 1.5f, 0f));
        var gridBottomRight = Cam.UnprojectPosition(new Vector3(1.5f, -1.5f, 0f));
        var gridBottomCenter = Cam.UnprojectPosition(new Vector3(0f, -1.5f, 0f));

        float sideX = gridTopRight.X + 24f;
        float topY = gridTopRight.Y;
        float height = gridBottomRight.Y - gridTopRight.Y;

        float accY = topY + height * 0.50f;
        float missY = topY + height * 0.68f;

        _accuracy.Position = new Vector2(sideX, accY);
        _accuracyTag.Position = new Vector2(sideX, accY + 30);
        _misses.Position = new Vector2(sideX, missY);
        _missesTag.Position = new Vector2(sideX, missY + 30);

        float healthX = gridBottomCenter.X - HealthWidth / 2f;
        float healthY = gridBottomCenter.Y + 12f;
        _healthBg.Position = new Vector2(healthX, healthY);

        _modsLabel.Position = new Vector2(
            vp.X * 0.5f - ModsBlockWidth / 2f,
            vp.Y - 78f);
        _modsValue.Position = new Vector2(
            vp.X * 0.5f - ModsBlockWidth / 2f,
            vp.Y - 58f);
    }

    private void Refresh()
    {
        var a = _runner?.Attempt;
        if (a == null) return;

        string grade = Leaderboard.GradeFromAccuracy(a.Accuracy, a.Alive);
        _rank.Text = grade;
        _rank.AddThemeColorOverride("font_color", GradeColor(grade));

        _combo.Text = a.Combo.ToString();
        _accuracy.Text = a.Sum == 0 ? "100.00%" : a.Accuracy.ToString("F2", CultureInfo.InvariantCulture) + "%";
        _misses.Text = a.Misses.ToString();

        float pct = Mathf.Clamp((float)(a.Health / Attempt.MaxHealth), 0f, 1f);
        _healthFill.AnchorRight = pct;
        _healthFill.OffsetRight = 0;

        if (a.Health <= 1) _healthFill.Color = new Color("ff3040");
        else if (a.Health <= 2) _healthFill.Color = new Color("ff9d33");
        else _healthFill.Color = UiStyle.Accent;

        UpdateTitleAndMods();
    }

    private static Color GradeColor(string grade)
    {
        switch (grade)
        {
            case "SS": return new Color("ffd700");
            case "S":  return new Color("8fe4ff");
            case "A":  return new Color("6ee7a8");
            case "B":  return new Color("8fbcff");
            case "C":  return new Color("ffd166");
            case "D":  return new Color("ff9d33");
            default:   return new Color("ff4d6d");
        }
    }
}
