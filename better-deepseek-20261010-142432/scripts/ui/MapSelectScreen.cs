using System;
using System.Collections.Generic;
using Godot;
using CustomRhythia.Map;
using CustomRhythia.Util;

namespace CustomRhythia.UI;

/// <summary>
/// Map picker. Right panel: cover, Start From slider with live preview,
/// speed presets, Current Pass and Best Attempt side by side, PLAY / Delete.
/// </summary>
public partial class MapSelectScreen : Control
{
	public static readonly double[] SpeedPresets =
	{
		0.75, 0.8, 0.87, 1.0, 1.15, 1.25, 1.35, 1.45
	};

	private VBoxContainer _list;
	private LineEdit _search;
	private Label _infoTitle;
	private Label _infoSub;
	private Label _infoLen;
	private TextureRect _cover;
	private Button _playButton;
	private Button _deleteButton;
	private Map.Map _selected;

	private HSlider _startSlider;
	private LineEdit _startEdit;
	private Label _startFromValue;
	private AudioStreamPlayer _preview;
	private double _startFromMs = 0;
	private bool _updatingStartUi = false;

	private List<Map.Map> _visibleMaps = new();

	private FileDialog _dialog;
	private ConfirmationDialog _confirm;
	private readonly List<Button> _speedButtons = new();
	private readonly Dictionary<string, ImageTexture> _coverCache = new();
	private string _lastCoverFor = null;
	private readonly Random _rng = new();

	private VBoxContainer _passBlock;
	private Label _currentPassGrade;
	private Label _currentPassStats;
	private VBoxContainer _bestBlock;
	private Label _bestAttemptGrade;
	private Label _bestAttemptStats;
	private Label _noPassLabel;

	public override void _Ready()
	{
		ScreenUtil.MakeFullScreen(this);

		AddChild(UiStyle.FullScreen(UiStyle.MakeBackground()));

		_preview = new AudioStreamPlayer { Bus = "Master" };
		_preview.VolumeDb = ComputePreviewDb();
		AddChild(_preview);

		BuildTopBar();
		BuildSplit();

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
	}

	public override void _ExitTree()
	{
		MapManager.MapsChanged -= RefreshList;
		if (_preview != null) _preview.Stop();
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
		HSplitContainer split = new HSplitContainer();
		split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		split.OffsetTop = 88;
		split.OffsetLeft = 16;
		split.OffsetRight = -16;
		split.OffsetBottom = -16;
		split.SplitOffsets = new[] { 620 };
		AddChild(split);

		Panel listCard = UiStyle.MakePanel(UiStyle.Surface);
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
		_list.AddThemeConstantOverride("separation", 6);
		listScroll.AddChild(_list);

		Panel info = UiStyle.MakePanel(UiStyle.Surface);
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
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
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

		Label speedLabel = UiStyle.MakeLabel("Speed", 12, UiStyle.TextMuted);
		speedLabel.HorizontalAlignment = HorizontalAlignment.Center;
		infoBox.AddChild(speedLabel);

		HBoxContainer speedRow = new HBoxContainer
		{
			Alignment = BoxContainer.AlignmentMode.Center,
		};
		speedRow.AddThemeConstantOverride("separation", 4);
		infoBox.AddChild(speedRow);

		ButtonGroup group = new ButtonGroup();
		for (int i = 0; i < SpeedPresets.Length; i++)
		{
			Button b = MakeSpeedButton(SpeedPresets[i], group);
			speedRow.AddChild(b);
			_speedButtons.Add(b);
		}
		UpdateSpeedButtons();

		// No-attempt hint sits above the side-by-side row.
		_noPassLabel = UiStyle.MakeLabel("No attempts at this speed yet.", 12, UiStyle.TextMuted);
		_noPassLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_noPassLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_noPassLabel.Visible = false;
		infoBox.AddChild(_noPassLabel);

		// ---- Current Pass (left) + Best Attempt (right) side by side ----
		var statsRow = new HBoxContainer();
		statsRow.AddThemeConstantOverride("separation", 20);
		infoBox.AddChild(statsRow);

		_passBlock = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_passBlock.AddThemeConstantOverride("separation", 2);
		_passBlock.Visible = false;
		statsRow.AddChild(_passBlock);

		var currentHeader = UiStyle.MakeLabel("Current Pass", 11, UiStyle.Accent);
		_passBlock.AddChild(currentHeader);

		_currentPassGrade = UiStyle.MakeLabel("", 18, UiStyle.TextPrimary);
		_passBlock.AddChild(_currentPassGrade);

		_currentPassStats = UiStyle.MakeLabel("", 11);
		_currentPassStats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_passBlock.AddChild(_currentPassStats);

		_bestBlock = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
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

		_deleteButton = UiStyle.MakeButton("Delete", 13);
		_deleteButton.CustomMinimumSize = new Vector2(0, 36);
		_deleteButton.Disabled = true;
		_deleteButton.AddThemeColorOverride("font_color", new Color(1f, 0.5f, 0.5f));
		_deleteButton.AddThemeColorOverride("font_hover_color", new Color(1f, 0.25f, 0.25f));
		_deleteButton.Pressed += AskDelete;
		infoBox.AddChild(_deleteButton);
	}

	// ---------------------------------------------------------------------
	// Start-from + preview
	// ---------------------------------------------------------------------

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

	// ---------------------------------------------------------------------
	// List
	// ---------------------------------------------------------------------

	private async void OnFilesSelected(string[] paths)
	{
		int n = await MapManager.ImportAsync(paths);
		GD.Print("Imported " + n + " map(s).");
		RefreshList();
	}

	private void RefreshList()
	{
		foreach (Node c in _list.GetChildren()) c.QueueFree();

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
				: "No maps match \"" + query + "\".";
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
			Button btn = new Button
			{
				Text = "  " + map.PrettyTitle,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			UiStyle.StyleListButton(btn);
			if (map == _selected) btn.AddThemeColorOverride("font_color", UiStyle.Accent);

			Map.Map captured = map;
			btn.Pressed += () => SelectMap(captured);
			_list.AddChild(btn);
		}
	}

	private void SelectMap(Map.Map map)
	{
		_selected = map;
		_infoTitle.Text = map.PrettyTitle;
		_infoSub.Text = "by " + map.PrettyMappers;
		_infoLen.Text = map.DifficultyName + "  |  " + map.Notes.Length + " notes  |  "
			+ Util.Strings.FormatTime(map.Length / 1000.0);
		_playButton.Disabled = false;
		_deleteButton.Disabled = false;

		_startFromMs = 0;
		_updatingStartUi = true;
		_startSlider.MaxValue = Math.Max(1, map.Length);
		_startSlider.Value = 0;
		_updatingStartUi = false;
		_startEdit.Text = "0:00";
		_startFromValue.Text = "0.0%";

		LoadCover(map);
		LoadPreview(map);
		RefreshPassStats();
		RefreshListHighlight();
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

		// Best always shown even if it equals last, so the panel doesn't
		// look empty when you only have one run.
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
		return "Misses: " + e.Misses
			+ "\nAcc: " + e.Accuracy.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "%"
			+ "\nCombo: " + e.MaxCombo
			+ "\nPauses: " + (e.Pauses == 0 ? "none" : e.Pauses.ToString());
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
		_deleteButton.Disabled = true;
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
			if (node is Button b)
			{
				bool selected = i < _visibleMaps.Count && _visibleMaps[i] == _selected;
				b.AddThemeColorOverride("font_color", selected ? UiStyle.Accent : UiStyle.TextPrimary);
				i++;
			}
		}
	}

	private void LoadCover(Map.Map map)
	{
		if (_lastCoverFor == map.FolderPath) return;
		_lastCoverFor = map.FolderPath;
		_cover.Texture = null;

		if (_coverCache.TryGetValue(map.FolderPath, out ImageTexture cached) && GodotObject.IsInstanceValid(cached))
		{
			_cover.Texture = cached;
			return;
		}

		Image img = Images.LoadFromFile(map.CoverPath);
		if (img == null)
		{
			_coverCache[map.FolderPath] = null;
			return;
		}

		ImageTexture tex = ImageTexture.CreateFromImage(img);
		_coverCache[map.FolderPath] = tex;
		_cover.Texture = tex;
	}

	private Button MakeSpeedButton(double speed, ButtonGroup group)
	{
		Button b = new Button
		{
			Text = speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x",
			ToggleMode = true,
			ButtonGroup = group,
			CustomMinimumSize = new Vector2(0, 30),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			FocusMode = FocusModeEnum.None,
		};
		b.AddThemeFontSizeOverride("font_size", 11);

		double capturedSpeed = speed;
		b.Pressed += () =>
		{
			Main.SelectedSpeed = capturedSpeed;
			UpdateSpeedButtons();
			RefreshPassStats();
			if (_preview != null && _preview.Stream != null)
				_preview.PitchScale = (float)capturedSpeed;
		};
		return b;
	}

	private void UpdateSpeedButtons()
	{
		for (int i = 0; i < _speedButtons.Count; i++)
		{
			bool on = Math.Abs(SpeedPresets[i] - Main.SelectedSpeed) < 0.001;
			_speedButtons[i].ButtonPressed = on;
			_speedButtons[i].AddThemeColorOverride("font_color", on ? UiStyle.Accent : UiStyle.TextPrimary);
		}
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
