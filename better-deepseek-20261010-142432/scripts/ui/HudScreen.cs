using System;
using System.Globalization;
using Godot;
using CustomRhythia.Game;
using CustomRhythia.Map;

namespace CustomRhythia.UI;

/// <summary>
/// In-game overlay.
///   Top center          - map title, time, live rank
///   Left middle edge    - combo
///   Right of the grid   - accuracy (upper) and misses (lower)
///   Under the grid      - health bar
///   Below health bar    - speed modifier (only if speed != 1x)
///   Center              - 3-2-1 countdown before the map actually starts
/// </summary>
public partial class HudScreen : Control
{
    private Runner _runner;

    private Label _combo;
    private Label _comboTag;
    private Label _accuracy;
    private Label _misses;
    private Label _time;
    private Label _title;
    private Label _speed;
    private Label _rank;
    private Label _countdown;

    private Panel _healthBg;
    private ColorRect _healthFill;

    private Label _accuracyTag;
    private Label _missesTag;

    private const float HealthWidth = 260f;
    private const float HealthHeight = 8f;
    private const float SpeedLabelWidth = 240f;
    private const float RankWidth = 260f;

    public Camera3D Cam { get; set; }

    public HudScreen() { }

    public void Bind(Runner runner)
    {
        _runner = runner;
        if (_runner != null) _runner.StatsUpdated += Refresh;
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

        _rank = UiStyle.MakeLabel("SS", 24, UiStyle.TextPrimary);
        _rank.HorizontalAlignment = HorizontalAlignment.Left;
        _rank.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _rank.Size = new Vector2(RankWidth, 32);
        AddChild(_rank);

        _combo = UiStyle.MakeLabel("0", 52, UiStyle.TextPrimary);
        _combo.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(_combo);

        _comboTag = UiStyle.MakeLabel("combo", 11, UiStyle.TextMuted);
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

        _speed = UiStyle.MakeLabel("", 22, UiStyle.TextPrimary);
        _speed.HorizontalAlignment = HorizontalAlignment.Center;
        _speed.VerticalAlignment = VerticalAlignment.Center;
        _speed.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _speed.Size = new Vector2(SpeedLabelWidth, 30);
        AddChild(_speed);

        // Big centered countdown number.
        _countdown = UiStyle.MakeLabel("", 160, UiStyle.Accent);
        _countdown.HorizontalAlignment = HorizontalAlignment.Center;
        _countdown.VerticalAlignment = VerticalAlignment.Center;
        _countdown.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _countdown.Visible = false;
        AddChild(_countdown);

        if (_runner != null) _runner.StatsUpdated += Refresh;
        Refresh();
        UpdateTitleAndSpeed();
    }

    public override void _ExitTree()
    {
        if (_runner != null) _runner.StatsUpdated -= Refresh;
    }

    public override void _Process(double delta)
    {
        if (_runner?.Attempt == null) return;
        var a = _runner.Attempt;

        double cur = System.Math.Max(0, a.Progress) / 1000.0;
        double total = a.Map.Length / 1000.0;
        _time.Text = Util.Strings.FormatTime(cur) + " / " + Util.Strings.FormatTime(total);

        UpdateCountdown();
        PositionAnchoredLabels();
    }

    private void UpdateCountdown()
    {
        if (_runner == null) return;

        if (_runner.IsCountingDown && _runner.CountdownRemaining > 0)
        {
            int secs = (int)System.Math.Ceiling(_runner.CountdownRemaining);
            if (secs > 0)
            {
                _countdown.Text = secs.ToString();
                _countdown.Visible = true;
                return;
            }
        }

        _countdown.Visible = false;
    }

    private void UpdateTitleAndSpeed()
    {
        if (_runner?.Attempt == null) return;

        _title.Text = _runner.Attempt.Map.PrettyTitle;

        double sp = _runner.Attempt.Speed;
        if (Math.Abs(sp - 1.0) < 0.001)
        {
            _speed.Text = "";
        }
        else
        {
            _speed.Text = sp.ToString("0.##", CultureInfo.InvariantCulture) + "x";
        }
    }

    private void PositionAnchoredLabels()
    {
        var vp = GetViewport().GetVisibleRect().Size;

        float comboX = 32f;
        float comboY = vp.Y * 0.5f - 10f;
        _rank.Position = new Vector2(comboX, comboY - 46f);
        _combo.Position = new Vector2(comboX, comboY);
        _comboTag.Position = new Vector2(comboX + 2f, comboY + 56f);

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

        _speed.Position = new Vector2(
            gridBottomCenter.X - SpeedLabelWidth / 2f,
            healthY + HealthHeight + 24f);
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
