using System;
using System.Collections.Generic;
using Godot;
using CustomRhythia.Map;
using CustomRhythia.Util;

namespace CustomRhythia.UI;

/// <summary>
/// Map picker. Each row is a unified card: a square thumbnail (image cropped
/// in code, so it always fills the frame cleanly), title, and an optional
/// rainbow heart on the right. The currently selected row "slides out" with
/// a small horizontal stretch animation. Speed presets use the S-notation
/// scale. Right-click any row for delete / favorite; favorites sort to the
/// top.
/// </summary>
public partial class MapSelectScreen : Control
{
	public static readonly (string Tag, double Value)[] SpeedPresets =
	{
		("S----", 0.65),
		("S---",  0.75),
		("S--",   0.80),
		("S-",    0.87),
		("NM",    1.00),
		("S+",    1.15),
		("S++",   1.25),
		("S+++",  1.35),
		("S++++", 1.45),
	};

	public Map.Map InitialSelection { get; set; }

	private const float LeftPanelWidth = 560f;

	private const int ThumbSize = 48;
	private const int ThumbRenderSize = 96;
	private const int ThumbPadding = 8;

	private const int BigCoverSize = 142;
	private const int BigCoverRenderSize = 284;

	private const int HeartSize = 26;
	private const float ThumbAlphaIdle = 0.55f;
	private const float ThumbAlphaSelected = 1.0f;

	// Row "slide out" animation: selected row is stretched horizontally.
	private const float RowSelectedScaleX = 1.03f;
	private const float RowScaleLerpSpeed = 14f;

	private VBoxContainer _list;
	private LineEdit _search;
	private Label _infoTitle;
	private Label _infoSub;
	private Label _infoLen;
	private TextureRect _cover;
	private Button _playButton;
	private Map.Map _selected;

	private HSlider _startSlider;
	private LineEdit _startEdit;
	private Label _startFromValue;
	private AudioStreamPlayer _preview;
	private double _startFromMs = 0;
	private bool _updatingStartUi = false;

	private Label _speedValueLabel;
	private LineEdit _customSpeedEdit;
	private Button _noFailButton;
	private bool _updatingSpeedUi = false;

	private List<Map.Map> _visibleMaps = new();

	private FileDialog _dialog;
	private ConfirmationDialog _confirm;
	private PopupMenu _contextMenu;
	private Map.Map _contextMap;

	private readonly List<Button> _speedButtons = new();
	private readonly Dictionary<string, ImageTexture> _coverCache = new();
	private readonly List<HeartWidget> _hearts = new();

	// Per-row tracking for the slide-out animation.
	private readonly Dictionary<string, HBoxContainer> _rowByPath = new();
	private readonly Dictionary<string, float> _rowScaleX = new();

	private string _lastCoverFor = null;
	private readonly Random _rng = new();

	private VBoxContainer _passBlock;
	private Label _currentPassGrade;
	private Label _currentPassStats;
	private VBoxContainer _bestBlock;
	private Label _bestAttemptGrade;
	private Label _bestAttemptStats;
	private Label _noPassLabel;

	private sealed class HeartWidget
	{
		public Label Node;
		public float Phase;
	}

	public override void _Ready()
	{
		ScreenUtil.MakeFullScreen(this);

		AddChild(UiStyle.FullScreen(UiStyle.MakeBackground()));

		_preview = new AudioStreamPlayer { Bus = "Master" };
		_preview.VolumeDb = ComputePreviewDb();
		AddChild(_preview);

		BuildTopBar();
		BuildSplit();
		BuildContextMenu();

		_dialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFiles,
			Access = FileDialog.AccessEnum.Filesystem,
			Title = "Import maps",
			UseNativeDialog = true,
		};
		_dialog.AddFilter("*.sspm,*.phxm", "Rhythia maps");
		_dialog.FilesSelected += OnFilesSelected;
		AddChild(_dialog);

		_confirm = new ConfirmationDialog
		{
			Title = "Delete map",
			OkButtonText = "Delete",
			CancelButtonText = "Cancel",
		};
		_confirm.Confirmed += DoDelete;
		AddChild(_confirm);

		MapManager.MapsChanged += RefreshList;
		RefreshList();

		ApplyInitialSelection();
	}

	public override void _ExitTree()
	{
		MapManager.MapsChanged -= RefreshList;
		if (_preview != null) _preview.Stop();
	}

	private void ApplyInitialSelection()
	{
		if (InitialSelection == null) return;

		Map.Map target = null;
		foreach (var m in MapManager.Maps)
		{
			if (m.FolderPath == InitialSelection.FolderPath)
			{
				target = m;
				break;
			}
		}
		if (target == null) target = InitialSelection;

		SelectMap(target);
	}

	private static float ComputePreviewDb()
	{
		double vol = SettingsManager.Current.VolumeMusic;
		if (vol <= 0) return -80f;
		return (float)(-80 + 70 * Math.Pow(vol / 100.0, 0.1));
	}

	private void BuildTopBar()
	{
		Panel topBar = UiStyle.MakePanel(UiStyle.Bg, UiStyle.Border);
		topBar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
		topBar.OffsetBottom = 72;
		AddChild(topBar);

		HBoxContainer box = new HBoxContainer();
		box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		box.OffsetLeft = 20;
		box.OffsetRight = -20;
		box.OffsetTop = 12;
		box.OffsetBottom = -12;
		box.AddThemeConstantOverride("separation", 10);
		topBar.AddChild(box);

		Button back = UiStyle.MakeButton("Back", 15);
		back.CustomMinimumSize = new Vector2(110, 44);
		back.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		back.Pressed += () => Main.Instance.ShowMainMenu();
		box.AddChild(back);

		Label heading = UiStyle.MakeLabel("Map Select", 22);
		heading.VerticalAlignment = VerticalAlignment.Center;
		box.AddChild(heading);

		_search = new LineEdit
		{
			PlaceholderText = "Search maps...",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 44),
			ClearButtonEnabled = true,
		};
		_search.AddThemeFontSizeOverride("font_size", 15);
		_search.TextChanged += _ => RefreshList();
		box.AddChild(_search);

		Button random = UiStyle.MakeButton("Random", 15);
		random.CustomMinimumSize = new Vector2(120, 44);
		random.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		random.TooltipText = "Pick a random map (F2)";
		random.Pressed += PickRandom;
		box.AddChild(random);

		Button import = UiStyle.MakeButton("Import", 15, true);
		import.CustomMinimumSize = new Vector2(120, 44);
		import.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		import.Pressed += () => _dialog.PopupCentered(new Vector2I(900, 600));
		box.AddChild(import);

		Button refresh = UiStyle.MakeButton("Refresh", 15);
		refresh.CustomMinimumSize = new Vector2(120, 44);
		refresh.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		refresh.Pressed += () => { MapManager.Reload(); RefreshList(); };
		box.AddChild(refresh);
	}

	private void BuildSplit()
	{
		HBoxContainer split = new HBoxContainer();
		split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		split.OffsetTop = 88;
		split.OffsetLeft = 16;
		split.OffsetRight = -16;
		split.OffsetBottom = -16;
		split.AddThemeConstantOverride("separation", 12);
		AddChild(split);

		Panel listCard = UiStyle.MakePanel(UiStyle.Surface);
		listCard.CustomMinimumSize = new Vector2(LeftPanelWidth, 0);
		listCard.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		split.AddChild(listCard);

		ScrollContainer listScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		listScroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		listScroll.OffsetLeft = 10;
		listScroll.OffsetRight = -10;
		listScroll.OffsetTop = 10;
		listScroll.OffsetBottom = -10;
		listCard.AddChild(listScroll);

		_list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_list.AddThemeConstantOverride("separation", 4);
		listScroll.AddChild(_list);

		Panel info = UiStyle.MakePanel(UiStyle.Surface);
		info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		split.AddChild(info);

		ScrollContainer infoScroll = new ScrollContainer
		{
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		infoScroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		infoScroll.OffsetLeft = 22;
		infoScroll.OffsetRight = -22;
		infoScroll.OffsetTop = 22;
		infoScroll.OffsetBottom = -22;
		info.AddChild(infoScroll);

		VBoxContainer infoBox = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		infoBox.AddThemeConstantOverride("separation", 10);
		infoScroll.AddChild(infoBox);

		CenterContainer coverCenter = new CenterContainer { CustomMinimumSize = new Vector2(0, 160) };
		infoBox.AddChild(coverCenter);

		Panel coverFrame = UiStyle.MakePanel(UiStyle.BgDeep, UiStyle.Border);
		coverFrame.CustomMinimumSize = new Vector2(150, 150);
		coverCenter.AddChild(coverFrame);

		_cover = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_cover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_cover.OffsetLeft = 4; _cover.OffsetRight = -4;
		_cover.OffsetTop = 4; _cover.OffsetBottom = -4;
		coverFrame.AddChild(_cover);

		_infoTitle = UiStyle.MakeLabel("No map selected", 20);
		_infoTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_infoTitle.HorizontalAlignment = HorizontalAlignment.Center;
		infoBox.AddChild(_infoTitle);

		ColorRect accentLine = new ColorRect
		{
			Color = UiStyle.Accent,
			CustomMinimumSize = new Vector2(48, 2),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
		};
		infoBox.AddChild(accentLine);

		_infoSub = UiStyle.MakeLabel("", 13, UiStyle.TextMuted);
		_infoSub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_infoSub.HorizontalAlignment = HorizontalAlignment.Center;
		infoBox.AddChild(_infoSub);

		_infoLen = UiStyle.MakeLabel("", 12, UiStyle.TextMuted);
		_infoLen.HorizontalAlignment = HorizontalAlignment.Center;
		infoBox.AddChild(_infoLen);

		var startLabel = UiStyle.MakeLabel("Start From", 12, UiStyle.TextMuted);
		startLabel.HorizontalAlignment = HorizontalAlignment.Center;
		infoBox.AddChild(startLabel);

		var startRow = new HBoxContainer();
		startRow.AddThemeConstantOverride("separation", 8);
		infoBox.AddChild(startRow);

		_startSlider = new HSlider
		{
			MinValue = 0,
			MaxValue = 1,
			Step = 100,
			Value = 0,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 24),
			FocusMode = FocusModeEnum.None,
		};
		startRow.AddChild(_startSlider);

		_startEdit = new LineEdit
		{
			Text = "0:00",
			CustomMinimumSize = new Vector2(72, 30),
			Alignment = HorizontalAlignment.Center,
		};
		startRow.AddChild(_startEdit);

		_startFromValue = UiStyle.MakeLabel("", 11, UiStyle.TextMuted);
		_startFromValue.HorizontalAlignment = HorizontalAlignment.Center;
		infoBox.AddChild(_startFromValue);

		_startSlider.ValueChanged += OnStartFromSliderChanged;
		_startEdit.TextSubmitted += _ => CommitStartFromEdit();
		_startEdit.FocusExited += CommitStartFromEdit;

		var speedHeader = new HBoxContainer();
		speedHeader.AddThemeConstantOverride("separation", 8);
		infoBox.AddChild(speedHeader);

		var speedLabel = UiStyle.MakeLabel("Speed", 12, UiStyle.TextMuted);
		speedLabel.VerticalAlignment = VerticalAlignment.Center;
		speedHeader.AddChild(speedLabel);

		_speedValueLabel = UiStyle.MakeLabel("", 12, UiStyle.Accent);
		_speedValueLabel.VerticalAlignment = VerticalAlignment.Center;
		_speedValueLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		speedHeader.AddChild(_speedValueLabel);

		var customLbl = UiStyle.MakeLabel("Custom", 11, UiStyle.TextMuted);
		customLbl.VerticalAlignment = VerticalAlignment.Center;
		speedHeader.AddChild(customLbl);

		_customSpeedEdit = new LineEdit
		{
			CustomMinimumSize = new Vector2(70, 26),
			Alignment = HorizontalAlignment.Center,
		};
		_customSpeedEdit.AddThemeFontSizeOverride("font_size", 12);
		_customSpeedEdit.TextSubmitted += _ => CommitCustomSpeed();
		_customSpeedEdit.FocusExited += CommitCustomSpeed;
		speedHeader.AddChild(_customSpeedEdit);

		var speedRow = new HBoxContainer();
		speedRow.AddThemeConstantOverride("separation", 3);
		infoBox.AddChild(speedRow);

		ButtonGroup group = new ButtonGroup();
		for (int i = 0; i < SpeedPresets.Length; i++)
		{
			Button b = MakeSpeedButton(SpeedPresets[i].Tag, SpeedPresets[i].Value, group);
			speedRow.AddChild(b);
			_speedButtons.Add(b);
		}

		var modsRow = new HBoxContainer();
		modsRow.AddThemeConstantOverride("separation", 10);
		infoBox.AddChild(modsRow);

		var modsLbl = UiStyle.MakeLabel("Modifiers", 12, UiStyle.TextMuted);
		modsLbl.VerticalAlignment = VerticalAlignment.Center;
		modsLbl.CustomMinimumSize = new Vector2(80, 0);
		modsRow.AddChild(modsLbl);

		_noFailButton = BuildNoFailToggle();
		modsRow.AddChild(_noFailButton);

		modsRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

		UpdateSpeedButtons();
		UpdateNoFailVisual();

		_noPassLabel = UiStyle.MakeLabel("No attempts at this speed yet.", 12, UiStyle.TextMuted);
		_noPassLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_noPassLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_noPassLabel.Visible = false;
		infoBox.AddChild(_noPassLabel);

		var statsRow = new HBoxContainer();
		statsRow.AddThemeConstantOverride("separation", 20);
		infoBox.AddChild(statsRow);

		_passBlock = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_passBlock.AddThemeConstantOverride("separation", 2);
		_passBlock.Visible = false;
		statsRow.AddChild(_passBlock);

		var lastTryHeader = UiStyle.MakeLabel("Last Try", 11, UiStyle.Accent);
		_passBlock.AddChild(lastTryHeader);

		_currentPassGrade = UiStyle.MakeLabel("", 18, UiStyle.TextPrimary);
		_passBlock.AddChild(_currentPassGrade);

		_currentPassStats = UiStyle.MakeLabel("", 11);
		_currentPassStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_passBlock.AddChild(_currentPassStats);

		_bestBlock = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_bestBlock.AddThemeConstantOverride("separation", 2);
		_bestBlock.Visible = false;
		statsRow.AddChild(_bestBlock);

		var bestHeader = UiStyle.MakeLabel("Best Attempt", 11, UiStyle.Success);
		_bestBlock.AddChild(bestHeader);

		_bestAttemptGrade = UiStyle.MakeLabel("", 18, UiStyle.TextPrimary);
		_bestBlock.AddChild(_bestAttemptGrade);

		_bestAttemptStats = UiStyle.MakeLabel("", 11);
		_bestAttemptStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_bestBlock.AddChild(_bestAttemptStats);

		infoBox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

		_playButton = UiStyle.MakeButton("PLAY", 18, true);
		_playButton.CustomMinimumSize = new Vector2(0, 52);
		_playButton.Disabled = true;
		_playButton.Pressed += PlaySelected;
		infoBox.AddChild(_playButton);
	}

	private Button BuildNoFailToggle()
	{
		var b = new Button
		{
			Text = "NoFail",
			ToggleMode = true,
			CustomMinimumSize = new Vector2(150, 30),
			SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
			FocusMode = FocusModeEnum.None,
			MouseDefaultCursorShape = CursorShape.PointingHand,
			ButtonPressed = Main.NoFailEnabled,
			TooltipText = "NoFail - misses don't cost health",
		};
		b.AddThemeFontSizeOverride("font_size", 12);

		b.Pressed += () =>
		{
			Main.NoFailEnabled = b.ButtonPressed;
			UpdateNoFailVisual();
		};

		return b;
	}

	private static StyleBoxFlat MakeNoFailBox(Color bg, Color border, bool glow)
	{
		var sb = UiStyle.PanelStyle(bg, border, 8);
		sb.ContentMarginLeft = 14;
		sb.ContentMarginRight = 14;
		sb.ContentMarginTop = 6;
		sb.ContentMarginBottom = 6;

		if (glow)
		{
			Color accent = UiStyle.Accent;
			sb.ShadowColor = new Color(accent.R, accent.G, accent.B, 0.85f);
			sb.ShadowSize = 6;
			sb.ShadowOffset = Vector2.Zero;
		}

		return sb;
	}

	private void UpdateNoFailVisual()
	{
		if (_noFailButton == null) return;

		bool on = _noFailButton.ButtonPressed;

		Color accent = UiStyle.Accent;
		Color offSurface = UiStyle.SurfaceHi;
		Color offBorder = UiStyle.Border;

		if (on)
		{
			_noFailButton.AddThemeStyleboxOverride("normal",
				MakeNoFailBox(accent, accent.Lightened(0.15f), true));
			_noFailButton.AddThemeStyleboxOverride("hover",
				MakeNoFailBox(accent.Lightened(0.08f), accent.Lightened(0.2f), true));
			_noFailButton.AddThemeStyleboxOverride("pressed",
				MakeNoFailBox(accent.Darkened(0.15f), accent.Darkened(0.15f), true));
			_noFailButton.AddThemeStyleboxOverride("focus",
				MakeNoFailBox(accent.Lightened(0.08f), accent.Lightened(0.2f), true));
			_noFailButton.AddThemeColorOverride("font_color", Colors.White);
			_noFailButton.AddThemeColorOverride("font_hover_color", Colors.White);
			_noFailButton.AddThemeColorOverride("font_pressed_color", Colors.White);
		}
		else
		{
			_noFailButton.AddThemeStyleboxOverride("normal",
				MakeNoFailBox(offSurface, offBorder, false));
			_noFailButton.AddThemeStyleboxOverride("hover",
				MakeNoFailBox(offSurface.Lightened(0.06f), accent, false));
			_noFailButton.AddThemeStyleboxOverride("pressed",
				MakeNoFailBox(offSurface.Darkened(0.1f), accent, false));
			_noFailButton.AddThemeStyleboxOverride("focus",
				MakeNoFailBox(offSurface.Lightened(0.06f), accent, false));
			_noFailButton.AddThemeColorOverride("font_color", UiStyle.TextMuted);
			_noFailButton.AddThemeColorOverride("font_hover_color", UiStyle.TextPrimary);
			_noFailButton.AddThemeColorOverride("font_pressed_color", UiStyle.TextPrimary);
		}
	}

	private void BuildContextMenu()
	{
		_contextMenu = new PopupMenu();
		_contextMenu.AddItem("Add to favorites", 0);
		_contextMenu.AddItem("Delete", 1);
		_contextMenu.IdPressed += OnContextMenuChoice;
		AddChild(_contextMenu);
	}

	private void OpenContextMenu(Map.Map map)
	{
		_contextMap = map;
		_contextMenu.SetItemText(0, map.Favorite ? "Remove from favorites" : "Add to favorites");
		_contextMenu.Position = (Vector2I)GetViewport().GetMousePosition();
		_contextMenu.Popup();
	}

	private void OnContextMenuChoice(long id)
	{
		var map = _contextMap;
		_contextMap = null;
		if (map == null) return;

		switch (id)
		{
			case 0: MapManager.ToggleFavorite(map); break;
			case 1:
				_selected = map;
				AskDelete();
				break;
		}
	}

	private void CommitCustomSpeed()
	{
		if (_updatingSpeedUi) return;

		string txt = _customSpeedEdit.Text.Trim().TrimEnd('x', 'X', ' ');
		if (!double.TryParse(txt, System.Globalization.NumberStyles.Any,
			System.Globalization.CultureInfo.InvariantCulture, out double value))
		{
			UpdateSpeedButtons();
			return;
		}

		if (value < 0.10) value = 0.10;
		if (value > 10.0) value = 10.0;

		Main.SelectedSpeed = value;
		UpdateSpeedButtons();
		RefreshPassStats();
		if (_preview != null && _preview.Stream != null)
			_preview.PitchScale = (float)value;
	}

	private void OnStartFromSliderChanged(double value)
	{
		if (_updatingStartUi) return;
		_startFromMs = value;
		_startEdit.Text = Util.Strings.FormatTime(value / 1000.0);
		_startFromValue.Text = FormatPercent(value);
		SeekPreview(value);
	}

	private string FormatPercent(double ms)
	{
		if (_selected == null || _selected.Length <= 0) return "";
		double pct = ms / _selected.Length * 100.0;
		if (pct > 100) pct = 100;
		return pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
	}

	private void CommitStartFromEdit()
	{
		if (_selected == null) return;

		string txt = _startEdit.Text.Trim();
		double parsed = ParseTimeInput(txt);
		if (parsed < 0) parsed = 0;
		if (parsed > _selected.Length) parsed = _selected.Length;

		_startFromMs = parsed;

		_updatingStartUi = true;
		_startSlider.MaxValue = Math.Max(1, _selected.Length);
		_startSlider.Value = parsed;
		_updatingStartUi = false;

		_startEdit.Text = Util.Strings.FormatTime(parsed / 1000.0);
		_startFromValue.Text = FormatPercent(parsed);
		SeekPreview(parsed);
	}

	private static double ParseTimeInput(string txt)
	{
		if (string.IsNullOrEmpty(txt)) return 0;

		if (txt.Contains(':'))
		{
			string[] parts = txt.Split(':');
			double total = 0;
			if (parts.Length >= 1 && double.TryParse(parts[0], System.Globalization.NumberStyles.Any,
				System.Globalization.CultureInfo.InvariantCulture, out double m))
				total += m * 60;
			if (parts.Length >= 2 && double.TryParse(parts[1], System.Globalization.NumberStyles.Any,
				System.Globalization.CultureInfo.InvariantCulture, out double s))
				total += s;
			return total * 1000.0;
		}

		if (double.TryParse(txt, System.Globalization.NumberStyles.Any,
			System.Globalization.CultureInfo.InvariantCulture, out double sec))
			return sec * 1000.0;

		return 0;
	}

	private void SeekPreview(double ms)
	{
		if (_preview == null || _preview.Stream == null) return;
		try
		{
			_preview.VolumeDb = ComputePreviewDb();
			if (!_preview.Playing) _preview.Play((float)(ms / 1000.0));
			else _preview.Seek((float)(ms / 1000.0));
			_preview.PitchScale = (float)Main.SelectedSpeed;
		}
		catch { /* ignore */ }
	}

	private void LoadPreview(Map.Map map)
	{
		if (_preview == null) return;
		_preview.Stop();
		_preview.Stream = null;

		if (map == null || !map.HasAudio) return;

		try
		{
			_preview.Stream = map.AudioExt == "ogg"
				? AudioStreamOggVorbis.LoadFromFile(map.AudioPath)
				: AudioStreamMP3.LoadFromFile(map.AudioPath);
			_preview.VolumeDb = ComputePreviewDb();
			_preview.PitchScale = (float)Main.SelectedSpeed;
			_preview.Play((float)(_startFromMs / 1000.0));
		}
		catch
		{
			_preview.Stream = null;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;

		UpdateRowScales(dt);
		UpdateHearts();
	}

	/// <summary>
	/// Smoothly stretches the selected row horizontally. The row keeps its
	/// layout size, only Scale.X changes, with a pivot at the left edge so
	/// it grows outward to the right ("slide out").
	/// </summary>
	private void UpdateRowScales(float dt)
	{
		if (_rowByPath.Count == 0) return;

		string selectedPath = _selected?.FolderPath;
		float lerp = Mathf.Min(1f, RowScaleLerpSpeed * dt);

		foreach (var kv in _rowByPath)
		{
			var row = kv.Value;
			if (row == null || !GodotObject.IsInstanceValid(row)) continue;

			// Pivot needs a known height, so set it on the first frame the
			// container has laid the row out.
			if (row.PivotOffset.Y <= 0 && row.Size.Y > 0)
			{
				row.PivotOffset = new Vector2(0, row.Size.Y / 2f);
			}

			float target = (kv.Key == selectedPath) ? RowSelectedScaleX : 1f;
			float cur = _rowScaleX.TryGetValue(kv.Key, out var v) ? v : 1f;
			cur = Mathf.Lerp(cur, target, lerp);

			_rowScaleX[kv.Key] = cur;
			row.Scale = new Vector2(cur, 1f);
		}
	}

	private void UpdateHearts()
	{
		if (_hearts.Count == 0) return;

		float t = Time.GetTicksMsec() / 1000f;

		for (int i = _hearts.Count - 1; i >= 0; i--)
		{
			var h = _hearts[i];
			if (h.Node == null || !GodotObject.IsInstanceValid(h.Node))
			{
				_hearts.RemoveAt(i);
				continue;
			}

			float hue = Mathf.PosMod(t * 0.25f + h.Phase / Mathf.Tau, 1.0f);
			float pulse = 0.55f + 0.45f * Mathf.Sin(t * 2.5f + h.Phase);
			float value = 0.75f + 0.25f * pulse;

			Color c = Color.FromHsv(hue, 0.85f, value);
			c.A = 0.55f + 0.45f * pulse;

			h.Node.Modulate = c;
		}
	}

	private async void OnFilesSelected(string[] paths)
	{
		int n = await MapManager.ImportAsync(paths);
		GD.Print("Imported " + n + " map(s).");
		RefreshList();
	}

	private void RefreshList()
	{
		foreach (Node c in _list.GetChildren()) c.QueueFree();
		_hearts.Clear();
		_rowByPath.Clear();
		_rowScaleX.Clear();

		string query = _search?.Text?.Trim() ?? "";
		_visibleMaps.Clear();

		foreach (Map.Map m in MapManager.Maps)
		{
			if (query.Length == 0
				|| m.PrettyTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				_visibleMaps.Add(m);
			}
		}

		if (_selected != null && !_visibleMaps.Contains(_selected))
		{
			_selected = null;
			ClearInfo();
		}

		if (_visibleMaps.Count == 0)
		{
			string msg = MapManager.Maps.Count == 0
				? "No maps yet. Press Import or drop .sspm files."
				: "No maps match your search.";
			Label empty = UiStyle.MakeLabel(msg, 14, UiStyle.TextMuted);
			empty.HorizontalAlignment = HorizontalAlignment.Center;
			empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			empty.CustomMinimumSize = new Vector2(0, 120);
			_list.AddChild(empty);
			return;
		}

		for (int i = 0; i < _visibleMaps.Count; i++)
		{
			Map.Map map = _visibleMaps[i];
			var row = BuildMapRow(map);
			_list.AddChild(row);
			_rowByPath[map.FolderPath] = row;
			_rowScaleX[map.FolderPath] = (map == _selected) ? RowSelectedScaleX : 1f;
		}
	}

	private HBoxContainer BuildMapRow(Map.Map map)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		row.MouseFilter = MouseFilterEnum.Ignore;

		var btn = new Button
		{
			Text = map.PrettyTitle,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, ThumbSize + 2 * ThumbPadding),
			ClipText = true,
		};
		UiStyle.StyleListButton(btn);
		ApplyThumbPadding(btn);

		if (map == _selected)
		{
			btn.AddThemeColorOverride("font_color", UiStyle.Accent);
		}

		Map.Map captured = map;
		btn.Pressed += () => SelectMap(captured);

		btn.GuiInput += (InputEvent ev) =>
		{
			if (ev is InputEventMouseButton mb
				&& mb.Pressed
				&& mb.ButtonIndex == MouseButton.Right)
			{
				OpenContextMenu(captured);
				GetViewport().SetInputAsHandled();
			}
		};
		row.AddChild(btn);

		var thumbFrame = UiStyle.MakePanel(UiStyle.BgDeep, UiStyle.Border, 6);
		thumbFrame.CustomMinimumSize = new Vector2(ThumbSize, ThumbSize);
		thumbFrame.MouseFilter = MouseFilterEnum.Ignore;
		thumbFrame.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterLeft);
		thumbFrame.OffsetLeft = ThumbPadding;
		thumbFrame.OffsetTop = -ThumbSize / 2f;
		thumbFrame.OffsetRight = ThumbPadding + ThumbSize;
		thumbFrame.OffsetBottom = ThumbSize / 2f;
		var sb = thumbFrame.GetThemeStylebox("panel") as StyleBoxFlat;
		if (sb != null)
		{
			sb.ContentMarginLeft = 0;
			sb.ContentMarginRight = 0;
			sb.ContentMarginTop = 0;
			sb.ContentMarginBottom = 0;
		}
		btn.AddChild(thumbFrame);

		var thumb = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Modulate = new Color(1, 1, 1,
				map == _selected ? ThumbAlphaSelected : ThumbAlphaIdle),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		thumb.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterLeft);
		thumb.OffsetLeft = ThumbPadding;
		thumb.OffsetTop = -ThumbSize / 2f;
		thumb.OffsetRight = ThumbPadding + ThumbSize;
		thumb.OffsetBottom = ThumbSize / 2f;
		btn.AddChild(thumb);

		thumb.Texture = GetCoverTexture(map, ThumbRenderSize);

		if (map.Favorite)
		{
			var heart = new Label
			{
				Text = "\u2665",
				CustomMinimumSize = new Vector2(HeartSize, 0),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				SizeFlagsVertical = SizeFlags.ExpandFill,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			heart.AddThemeFontSizeOverride("font_size", 20);
			row.AddChild(heart);

			_hearts.Add(new HeartWidget
			{
				Node = heart,
				Phase = (float)(_rng.NextDouble() * Mathf.Tau),
			});
		}
		else
		{
			var spacer = new Control
			{
				CustomMinimumSize = new Vector2(HeartSize, 0),
				MouseFilter = MouseFilterEnum.Ignore,
			};
			row.AddChild(spacer);
		}

		return row;
	}

	private static void ApplyThumbPadding(Button btn)
	{
		int pad = ThumbPadding + ThumbSize + 12;

		foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
		{
			if (btn.GetThemeStylebox(state) is StyleBoxFlat sb)
			{
				sb.ContentMarginLeft = pad;
			}
		}
	}

	private ImageTexture GetCoverTexture(Map.Map map, int size)
	{
		string key = map.FolderPath + "|" + size;
		if (_coverCache.TryGetValue(key, out var cached))
		{
			return cached;
		}

		ImageTexture tex = BuildCoverTexture(map.CoverPath, size);
		_coverCache[key] = tex;
		return tex;
	}

	private static ImageTexture BuildCoverTexture(string path, int size)
	{
		if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;

		Image img = Images.LoadFromFile(path);
		if (img == null) return null;

		int w = img.GetWidth();
		int h = img.GetHeight();
		if (w <= 0 || h <= 0) return null;

		int s = Math.Min(w, h);
		int cx = (w - s) / 2;
		int cy = (h - s) / 2;

		Image cropped;
		if (w == h)
		{
			cropped = img;
		}
		else
		{
			cropped = img.GetRegion(new Rect2I(cx, cy, s, s));
		}

		if (cropped.GetWidth() != size || cropped.GetHeight() != size)
		{
			cropped.Resize(size, size, Image.Interpolation.Bilinear);
		}

		return ImageTexture.CreateFromImage(cropped);
	}

	private void SelectMap(Map.Map map)
	{
		_selected = map;
		_infoTitle.Text = map.PrettyTitle;
		_infoSub.Text = "by " + map.PrettyMappers;
		_infoLen.Text = map.DifficultyName + "  |  " + map.Notes.Length + " notes  |  "
			+ Util.Strings.FormatTime(map.Length / 1000.0);
		_playButton.Disabled = false;

		_startFromMs = 0;
		_updatingStartUi = true;
		_startSlider.MaxValue = Math.Max(1, map.Length);
		_startSlider.Value = 0;
		_updatingStartUi = false;
		_startEdit.Text = "0:00";
		_startFromValue.Text = "0.0%";

		LoadBigCover(map);
		LoadPreview(map);
		RefreshPassStats();
		RefreshListHighlight();
	}

	private void LoadBigCover(Map.Map map)
	{
		if (_lastCoverFor == map.FolderPath) return;
		_lastCoverFor = map.FolderPath;

		_cover.Texture = GetCoverTexture(map, BigCoverRenderSize);
	}

	private void RefreshPassStats()
	{
		_passBlock.Visible = false;
		_bestBlock.Visible = false;
		_noPassLabel.Visible = false;
		_currentPassStats.Text = "";
		_currentPassGrade.Text = "";
		_bestAttemptStats.Text = "";
		_bestAttemptGrade.Text = "";

		if (_selected == null) return;

		var lb = Leaderboard.Load(_selected.Name);
		var rec = lb.Speeds != null && lb.Speeds.TryGetValue(Leaderboard.SpeedKey(Main.SelectedSpeed), out var r)
			? r
			: null;

		if (rec == null || (rec.Last == null && rec.Best == null))
		{
			_noPassLabel.Visible = true;
			return;
		}

		if (rec.Last != null)
		{
			_passBlock.Visible = true;
			_currentPassGrade.Text = rec.Last.Grade + "  -  " + (rec.Last.Passed ? "PASSED" : "FAILED");
			_currentPassGrade.AddThemeColorOverride("font_color", GradeColor(rec.Last.Grade));
			_currentPassStats.Text = FormatStats(rec.Last);
		}

		if (rec.Best != null)
		{
			_bestBlock.Visible = true;
			_bestAttemptGrade.Text = rec.Best.Grade + "  -  " + (rec.Best.Passed ? "PASSED" : "FAILED");
			_bestAttemptGrade.AddThemeColorOverride("font_color", GradeColor(rec.Best.Grade));
			_bestAttemptStats.Text = FormatStats(rec.Best);
		}
	}

	private static string FormatStats(ScoreEntry e)
	{
		const string NL = "\u000A";

		string timeLine;
		if (e.StartFrom > 1.0)
		{
			timeLine = Util.Strings.FormatTime(e.StartFrom / 1000.0)
				+ "  ->  " + Util.Strings.FormatTime(e.Progress / 1000.0);
		}
		else
		{
			timeLine = "Ended at " + Util.Strings.FormatTime(e.Progress / 1000.0);
		}

		string acc = e.Accuracy.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "%";
		string pauses = (e.Pauses == 0 ? "none" : e.Pauses.ToString());

		return timeLine
			+ NL + "Misses: " + e.Misses
			+ NL + "Acc: " + acc
			+ NL + "Combo: " + e.MaxCombo
			+ NL + "Pauses: " + pauses;
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

	private void ClearInfo()
	{
		_infoTitle.Text = "No map selected";
		_infoSub.Text = "";
		_infoLen.Text = "";
		_cover.Texture = null;
		_lastCoverFor = null;
		_playButton.Disabled = true;
		_passBlock.Visible = false;
		_bestBlock.Visible = false;
		_noPassLabel.Visible = false;
		_startFromMs = 0;
		if (_startEdit != null) _startEdit.Text = "0:00";
		if (_startFromValue != null) _startFromValue.Text = "";
		if (_preview != null) _preview.Stop();
	}

	private void RefreshListHighlight()
	{
		int i = 0;
		foreach (Node node in _list.GetChildren())
		{
			if (node is HBoxContainer row && row.GetChildCount() >= 1)
			{
				var btn = row.GetChild(0) as Button;
				if (btn == null) continue;

				bool selected = i < _visibleMaps.Count && _visibleMaps[i] == _selected;
				btn.AddThemeColorOverride("font_color", selected ? UiStyle.Accent : UiStyle.TextPrimary);

				TextureRect thumb = null;
				for (int c = btn.GetChildCount() - 1; c >= 0; c--)
				{
					if (btn.GetChild(c) is TextureRect tr)
					{
						thumb = tr;
						break;
					}
				}
				if (thumb != null)
				{
					thumb.Modulate = new Color(1, 1, 1,
						selected ? ThumbAlphaSelected : ThumbAlphaIdle);
				}

				i++;
			}
		}
	}

	private Button MakeSpeedButton(string tag, double value, ButtonGroup group)
	{
		Button b = new Button
		{
			Text = tag,
			ToggleMode = true,
			ButtonGroup = group,
			CustomMinimumSize = new Vector2(0, 28),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			FocusMode = FocusModeEnum.None,
			TooltipText = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x",
		};
		b.AddThemeFontSizeOverride("font_size", 10);

		Color pos = new Color("6ee7a8");
		Color neg = new Color("ff9d33");
		Color baseColor = Math.Abs(value - 1.0) < 0.001
			? UiStyle.Accent
			: (value > 1.0 ? pos : neg);

		double captured = value;
		b.Pressed += () =>
		{
			Main.SelectedSpeed = captured;
			UpdateSpeedButtons();
			RefreshPassStats();
			if (_preview != null && _preview.Stream != null)
				_preview.PitchScale = (float)captured;
		};

		b.SetMeta("base_color", baseColor);
		return b;
	}

	private void UpdateSpeedButtons()
	{
		for (int i = 0; i < _speedButtons.Count; i++)
		{
			var btn = _speedButtons[i];
			double value = SpeedPresets[i].Value;
			bool on = Math.Abs(value - Main.SelectedSpeed) < 0.001;
			btn.ButtonPressed = on;

			Color baseColor = (Color)btn.GetMeta("base_color");
			btn.AddThemeColorOverride("font_color", on ? UiStyle.Accent : baseColor);
		}

		if (_speedValueLabel != null)
		{
			double sp = Main.SelectedSpeed;
			string spText = sp.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";

			if (Math.Abs(sp - 1.0) < 0.001)
			{
				_speedValueLabel.Text = "  " + spText;
			}
			else
			{
				string tag = CurrentTag();
				_speedValueLabel.Text = tag == "Custom"
					? "  " + spText
					: "  " + tag + "  (" + spText + ")";
			}
		}

		if (_customSpeedEdit != null)
		{
			_updatingSpeedUi = true;
			_customSpeedEdit.Text = Main.SelectedSpeed.ToString("0.##",
				System.Globalization.CultureInfo.InvariantCulture) + "x";
			_updatingSpeedUi = false;
		}
	}

	private string CurrentTag()
	{
		for (int i = 0; i < SpeedPresets.Length; i++)
		{
			if (Math.Abs(SpeedPresets[i].Value - Main.SelectedSpeed) < 0.001)
				return SpeedPresets[i].Tag;
		}
		return "Custom";
	}

	private void PickRandom()
	{
		if (_visibleMaps.Count == 0) return;
		int idx = _rng.Next(_visibleMaps.Count);
		SelectMap(_visibleMaps[idx]);
	}

	private void PlaySelected()
	{
		if (_selected == null) return;
		if (_preview != null) _preview.Stop();

		Main.SelectedStartFrom = _startFromMs;
		Main.Instance.StartGame(_selected, Main.SelectedSpeed, _startFromMs);
	}

	private void AskDelete()
	{
		if (_selected == null) return;
		_confirm.DialogText = "Delete this map? The folder will be removed from disk.";
		_confirm.PopupCentered();
	}

	private void DoDelete()
	{
		if (_selected == null) return;
		Map.Map target = _selected;
		_selected = null;
		ClearInfo();
		MapManager.Delete(target);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			bool editing = GetViewport().GuiGetFocusOwner() is LineEdit;

			switch (key.PhysicalKeycode)
			{
				case Key.Escape:
					if (!_dialog.Visible && !_confirm.Visible)
						Main.Instance.ShowMainMenu();
					break;
				case Key.Delete:
					if (_selected != null && !_dialog.Visible && !_confirm.Visible && !editing)
						AskDelete();
					break;
				case Key.F2:
					if (!_dialog.Visible && !_confirm.Visible && !editing)
						PickRandom();
					break;
				case Key.Enter:
				case Key.KpEnter:
					if (_selected != null && !_dialog.Visible && !_confirm.Visible && !editing)
						PlaySelected();
					break;
			}
		}
	}
}
