using Godot;
using CustomRhythia.Game;
using CustomRhythia.Map;
using CustomRhythia.Util;

namespace CustomRhythia.UI;

/// <summary>
/// End-of-run summary: status + grade, cover, stats grid, timing graph,
/// Retry / Menu buttons. Layout is compact so nothing overflows on small
/// viewports.
/// </summary>
public partial class ResultsScreen : Control
{
    private Attempt _attempt;
    private ScoreEntry _entry;

    public ResultsScreen() { }

    public void Bind(Attempt attempt, ScoreEntry entry)
    {
        _attempt = attempt;
        _entry = entry;
    }

    public override void _Ready()
    {
        if (_attempt == null || _entry == null) { Main.Instance.ShowMainMenu(); return; }

        ScreenUtil.MakeFullScreen(this);

        var bg = new ColorRect { Color = UiStyle.Bg };
        AddChild(bg);
        UiStyle.FullScreen(bg);

        // Card: fixed 760 x 720, always centered.
        var card = UiStyle.MakePanel(UiStyle.Surface);
        card.AnchorLeft = 0.5f; card.AnchorRight = 0.5f;
        card.AnchorTop = 0.5f; card.AnchorBottom = 0.5f;
        card.OffsetLeft = -380; card.OffsetRight = 380;
        card.OffsetTop = -360; card.OffsetBottom = 360;
        AddChild(card);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        card.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        margin.AddChild(box);

        // ---- Header ----
        var header = new HBoxContainer();
        box.AddChild(header);

        string status = _entry.Passed ? "You passed!" : "You failed!";
        var statusColor = _entry.Passed ? UiStyle.Success : new Color("ff7d3d");
        var statusLbl = UiStyle.MakeLabel(status, 30, statusColor);
        statusLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(statusLbl);

        var gradeLbl = UiStyle.MakeLabel(_entry.Grade, 36, GradeColor(_entry.Grade));
        gradeLbl.HorizontalAlignment = HorizontalAlignment.Right;
        header.AddChild(gradeLbl);

        // ---- Cover + title ----
        var midRow = new HBoxContainer();
        midRow.AddThemeConstantOverride("separation", 16);
        box.AddChild(midRow);

        var coverFrame = UiStyle.MakePanel(UiStyle.BgDeep, UiStyle.Border);
        coverFrame.CustomMinimumSize = new Vector2(120, 120);
        coverFrame.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        midRow.AddChild(coverFrame);

        var cover = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        cover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cover.OffsetLeft = 4; cover.OffsetRight = -4;
        cover.OffsetTop = 4; cover.OffsetBottom = -4;
        coverFrame.AddChild(cover);

        var coverImg = Images.LoadFromFile(_attempt.Map.CoverPath);
        if (coverImg != null) cover.Texture = ImageTexture.CreateFromImage(coverImg);

        var titleCol = new VBoxContainer();
        titleCol.AddThemeConstantOverride("separation", 4);
        titleCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        midRow.AddChild(titleCol);

        var title = UiStyle.MakeLabel(_attempt.Map.PrettyTitle, 20);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titleCol.AddChild(title);

        titleCol.AddChild(UiStyle.MakeLabel("by " + _attempt.Map.PrettyMappers, 12, UiStyle.TextMuted));
        titleCol.AddChild(UiStyle.MakeLabel(
            _attempt.Map.DifficultyName + "  |  " + _attempt.Map.Notes.Length + " notes",
            12, UiStyle.TextMuted));

        string speedTxt = _entry.Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
        titleCol.AddChild(UiStyle.MakeLabel("Speed  " + speedTxt, 12, UiStyle.TextMuted));

        box.AddChild(new HSeparator());

        // ---- Stats grid (2 columns) ----
        var stats = new GridContainer { Columns = 2 };
        stats.AddThemeConstantOverride("h_separation", 40);
        stats.AddThemeConstantOverride("v_separation", 12);
        box.AddChild(stats);

        AddStatBlock(stats, "MISSES", _entry.Misses.ToString(),
            _entry.Misses == 0 ? UiStyle.Success : new Color("ff4d6d"));
        AddStatBlock(stats, "PAUSES", _entry.Pauses == 0 ? "No Pauses" : _entry.Pauses.ToString(),
            _entry.Pauses == 0 ? UiStyle.Success : new Color("ff9d33"));

        string progressTime = Util.Strings.FormatTime(_entry.Progress / 1000.0);
        double progressPct = _entry.MapLength > 0 ? _entry.Progress / _entry.MapLength * 100.0 : 0;
        if (progressPct > 100) progressPct = 100;
        string progressPctTxt = progressPct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
        AddStatBlock(stats, "PROGRESS", progressTime + "\n" + progressPctTxt, new Color("ffd166"));

        string hitsLine = _entry.Hits + "/" + _entry.Sum;
        string accTxt = _entry.Accuracy.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "%";
        AddStatBlock(stats, "ACCURACY", hitsLine + "\n" + accTxt, new Color("8fbcff"));

        box.AddChild(new HSeparator());

        // ---- Timing graph (fixed height, no expansion) ----
        var graph = new TimingGraph();
        graph.Bind(_attempt);
        graph.CustomMinimumSize = new Vector2(0, 100);
        box.AddChild(graph);

        // ---- Buttons ----
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        box.AddChild(buttons);

        var retry = UiStyle.MakeButton("Retry", 17, true);
        retry.CustomMinimumSize = new Vector2(200, 46);
        retry.Pressed += () => Main.Instance.StartGame(_attempt.Map, _attempt.Speed, _attempt.StartFrom);
        buttons.AddChild(retry);

        var back = UiStyle.MakeButton("Menu", 17);
        back.CustomMinimumSize = new Vector2(200, 46);
        back.Pressed += () => Main.Instance.ShowMainMenu();
        buttons.AddChild(back);
    }

    private static void AddStatBlock(GridContainer grid, string label, string value, Color color)
    {
        var col = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        col.AddThemeConstantOverride("separation", 2);
        grid.AddChild(col);

        var lbl = UiStyle.MakeLabel(label, 11, color);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(lbl);

        var val = UiStyle.MakeLabel(value, 20, UiStyle.TextPrimary);
        val.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(val);
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

    public override void _Input(InputEvent @event)
    {
        if (_attempt == null) return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Escape) Main.Instance.ShowMainMenu();
            else if (key.PhysicalKeycode == Key.Enter || key.PhysicalKeycode == Key.KpEnter)
                Main.Instance.StartGame(_attempt.Map, _attempt.Speed, _attempt.StartFrom);
        }
    }
}

public partial class TimingGraph : Control
{
    private Attempt _attempt;

    public TimingGraph() { }

    public void Bind(Attempt attempt) => _attempt = attempt;

    public override void _Draw()
    {
        if (_attempt == null) return;

        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.35f), filled: true);

        var notes = _attempt.Map.Notes;
        if (notes.Length == 0) return;

        float w = Size.X;
        float h = Size.Y;
        float mapLen = Mathf.Max(1, _attempt.Map.Length);
        float hitWindow = (float)Constants.HIT_WINDOW;

        foreach (var note in notes)
        {
            float x = note.Millisecond / mapLen * w;
            if (note.LastResult == HitResult.Miss)
            {
                DrawLine(new Vector2(x, 0), new Vector2(x, h), new Color(1, 0.2f, 0.3f, 0.7f), 1.0f);
            }
            else if (note.LastResult == HitResult.Hit)
            {
                float y = Mathf.Clamp(note.HitOffset / hitWindow, -1f, 1f) * 0.5f + 0.5f;
                DrawRect(new Rect2(x, y * h - 1, 2, 2), new Color(0.4f, 1f, 0.4f, 0.9f));
            }
        }
    }
}
