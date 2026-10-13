using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace SoundSpace;

/// <summary>
/// Main menu: map selection (with music preview), right-click map actions, speed,
/// start-from point, import and settings. The UI is built in code.
/// </summary>
public partial class MainMenuScene : Control
{
	// Requested speed presets.
	private static readonly double[] Speeds = [0.75, 0.8, 0.87, 1.0, 1.15, 1.25, 1.35];

	private VBoxContainer _mapList;
	private TextureRect _cover;
	private Label _infoTitle;
	private Label _infoSub;
	private Label _infoStats;
	private Label _speedValue;
	private HSlider _startSlider;
	private Label _startValue;
	private Button _playButton;
	private Control _settingsOverlay;
	private FileDialog _importDialog;
	private PopupMenu _mapMenu;
	private AudioStreamPlayer _musicPlayer;

	private readonly List<(Button Btn, MapInfo Map)> _rows = [];
	private readonly List<(Button Btn, double Value)> _speedButtons = [];
	private MapInfo _selected;
	private MapInfo _menuTarget;
	private ButtonGroup _mapGroup = new();

	private static readonly Color FavoriteColor = new(1f, 0.83f, 0.35f);

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;

		// Menus use v-sync again (gameplay turns it off for low input latency).
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Enabled);
		Input.UseAccumulatedInput = true;

		SetAnchorsPreset(LayoutPreset.FullRect);

		AppSettings.Load();
		ColorSets.EnsurePresets();
		MapLibrary.LoadAll();

		BuildUi();
		RefreshMaps();
		SelectMap(MapLibrary.Maps.FirstOrDefault());
		UpdateSpeedButtons();
	}

	#region UI construction

	private void BuildUi()
	{
		var bg = new ColorRect { Color = new Color(0.05f, 0.05f, 0.08f) };
		bg.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(bg);

		_musicPlayer = new AudioStreamPlayer();
		AddChild(_musicPlayer);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		AddChild(margin);

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 12);
		margin.AddChild(root);

		BuildTopBar(root);

		var main = new HBoxContainer();
		main.AddThemeConstantOverride("separation", 18);
		main.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddChild(main);

		BuildMapListSide(main);
		BuildInfoSide(main);

		BuildMapContextMenu();
		BuildSettingsOverlay();
		BuildImportDialog();
	}

	private void BuildTopBar(VBoxContainer root)
	{
		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 10);
		root.AddChild(top);

		var logo = new Label { Text = "SoundSpace" };
		logo.AddThemeFontSizeOverride("font_size", 32);
		logo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		top.AddChild(logo);

		Button importBtn = MakeButton("Import Map", 18);
		importBtn.Pressed += () => _importDialog.PopupCentered(new Vector2I(900, 600));
		top.AddChild(importBtn);

		Button settingsBtn = MakeButton("Settings", 18);
		settingsBtn.Pressed += () => _settingsOverlay.Visible = !_settingsOverlay.Visible;
		top.AddChild(settingsBtn);
	}

	private void BuildMapListSide(HBoxContainer main)
	{
		var left = new VBoxContainer();
		left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		left.SizeFlagsStretchRatio = 1.2f;
		left.CustomMinimumSize = new Vector2(340, 0);
		left.AddThemeConstantOverride("separation", 6);
		main.AddChild(left);

		var label = new Label { Text = "Maps" };
		label.AddThemeFontSizeOverride("font_size", 20);
		left.AddChild(label);

		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		left.AddChild(scroll);

		_mapList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_mapList.AddThemeConstantOverride("separation", 6);
		scroll.AddChild(_mapList);
	}

	private void BuildInfoSide(HBoxContainer main)
	{
		var right = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		right.AddThemeConstantOverride("separation", 8);
		main.AddChild(right);

		_cover = new TextureRect
		{
			CustomMinimumSize = new Vector2(200, 200),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		right.AddChild(_cover);

		_infoTitle = new Label { Text = "No map selected", AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_infoTitle.AddThemeFontSizeOverride("font_size", 22);
		right.AddChild(_infoTitle);

		_infoSub = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_infoSub.AddThemeFontSizeOverride("font_size", 15);
		_infoSub.AddThemeColorOverride("font_color", new Color(0.72f, 0.72f, 0.78f));
		right.AddChild(_infoSub);

		_infoStats = new Label();
		_infoStats.AddThemeFontSizeOverride("font_size", 15);
		_infoStats.AddThemeColorOverride("font_color", new Color(0.72f, 0.72f, 0.78f));
		right.AddChild(_infoStats);


		// Controls are grouped and bottom aligned so their left edges line up with the info above.
		var controls = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		controls.Alignment = BoxContainer.AlignmentMode.End;
		controls.AddThemeConstantOverride("separation", 10);
		right.AddChild(controls);

		BuildStartFromRow(controls);
		BuildSpeedRow(controls);

		_playButton = MakeButton("PLAY", 26);
		_playButton.CustomMinimumSize = new Vector2(0, 60);
		_playButton.Pressed += OnPlayPressed;
		controls.AddChild(_playButton);
	}

	private void BuildStartFromRow(VBoxContainer right)
	{
		var header = new HBoxContainer();
		right.AddChild(header);

		var label = new Label { Text = "Start From", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		label.AddThemeFontSizeOverride("font_size", 18);
		header.AddChild(label);

		_startValue = new Label { Text = "0:00" };
		_startValue.AddThemeFontSizeOverride("font_size", 18);
		header.AddChild(_startValue);

		_startSlider = new HSlider
		{
			MinValue = 0,
			MaxValue = 1,
			Step = 500,
			Value = 0,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		_startSlider.ValueChanged += value =>
		{
			Session.StartFrom = value * Math.Max(0.01, Session.Speed);
			_startValue.Text = FormatTime(value / 1000.0);

			// Move the menu preview so you can hear where the map will start.
			if (_musicPlayer.Playing)
			{
				_musicPlayer.Seek((float)(Session.StartFrom / 1000.0));
			}
		};
		right.AddChild(_startSlider);
	}

	private void BuildSpeedRow(VBoxContainer right)
	{
		var header = new HBoxContainer();
		right.AddChild(header);

		var label = new Label { Text = "Speed", SizeFlagsHorizontal = SizeFlags.ExpandFill };
		label.AddThemeFontSizeOverride("font_size", 18);
		header.AddChild(label);

		_speedValue = new Label { Text = "1x" };
		_speedValue.AddThemeFontSizeOverride("font_size", 18);
		header.AddChild(_speedValue);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		right.AddChild(row);

		var group = new ButtonGroup();

		foreach (double speed in Speeds)
		{
			var button = new Button
			{
				Text = SpeedLabel(speed),
				ToggleMode = true,
				ButtonGroup = group,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			button.AddThemeFontSizeOverride("font_size", 14);

			double captured = speed;
			button.Pressed += () => SetSpeed(captured);

			row.AddChild(button);
			_speedButtons.Add((button, speed));
		}
	}

	private void BuildMapContextMenu()
	{
		_mapMenu = new PopupMenu();
		_mapMenu.AddItem("Favorite", 0);
		_mapMenu.AddItem("Delete Map", 1);
		_mapMenu.IdPressed += OnMapMenuPressed;
		AddChild(_mapMenu);
	}

	private void BuildSettingsOverlay()
	{
		_settingsOverlay = new Control { Visible = false };
		_settingsOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(_settingsOverlay);

		var dim = new ColorRect { Color = new Color(0, 0, 0, 0.65f) };
		dim.SetAnchorsPreset(LayoutPreset.FullRect);
		_settingsOverlay.AddChild(dim);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		_settingsOverlay.AddChild(center);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 0) };
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 24);
		margin.AddThemeConstantOverride("margin_right", 24);
		margin.AddThemeConstantOverride("margin_top", 20);
		margin.AddThemeConstantOverride("margin_bottom", 20);
		panel.AddChild(margin);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 12);
		margin.AddChild(box);

		var title = new Label { Text = "Settings" };
		title.AddThemeFontSizeOverride("font_size", 26);
		box.AddChild(title);

		var tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		tabs.AddThemeFontSizeOverride("font_size", 18);
		box.AddChild(tabs);

		// Tab 1 - gameplay.
		var game = new VBoxContainer();
		game.Name = "Game";
		game.AddThemeConstantOverride("separation", 12);
		tabs.AddChild(game);

		AddSlider(game, "Approach Rate", 0.5, 100, 0.5, AppSettings.Current.ApproachRate, "0.0", v => AppSettings.Current.ApproachRate = v);
		AddSlider(game, "Spawn Distance", 0.5, 100, 0.5, AppSettings.Current.SpawnDistance, "0.0", v => AppSettings.Current.SpawnDistance = v);
		AddSlider(game, "Parallax", 0, 1, 0.05, AppSettings.Current.Parallax, "0.00", v => AppSettings.Current.Parallax = v);
		AddSlider(game, "Mouse Sensitivity", 0.01, 2.5, 0.01, AppSettings.Current.Sensitivity, "0.00", v => AppSettings.Current.Sensitivity = v);
		AddSlider(game, "Music Volume", 0, 100, 1, AppSettings.Current.MusicVolume, "0", v =>
		{
			AppSettings.Current.MusicVolume = v;
			UpdateMusicVolume();
		});
		AddToggle(game, "Half Ghost", AppSettings.Current.HalfGhost, v => AppSettings.Current.HalfGhost = v);

		// Tab 2 - visuals.
		var visual = new VBoxContainer();
		visual.Name = "Visual";
		visual.AddThemeConstantOverride("separation", 12);
		tabs.AddChild(visual);

		AddOption(visual, "Note Style", Constants.NoteStyles, AppSettings.Current.NoteStyle, value => AppSettings.Current.NoteStyle = value);
		AddSlider(visual, "Note Size", 0.3, 1.5, 0.01, AppSettings.Current.NoteSize, "0.00", v => AppSettings.Current.NoteSize = v);
		AddSlider(visual, "Note Opacity", 0, 1, 0.01, AppSettings.Current.NoteOpacity, "0.00", v => AppSettings.Current.NoteOpacity = v);
		AddCursorPicker(visual);
		AddSlider(visual, "Cursor Size", 0.4, 3.0, 0.05, AppSettings.Current.CursorScale, "0.00", v => AppSettings.Current.CursorScale = v);

		List<string> colorsets = ColorSets.GetNames();
		if (colorsets.Count == 0)
		{
			colorsets = ["Default"];
		}

		if (!colorsets.Contains(AppSettings.Current.ColorSet))
		{
			AppSettings.Current.ColorSet = colorsets[0];
			AppSettings.Save();
		}

		AddOption(visual, "Colorset", colorsets, AppSettings.Current.ColorSet, value => AppSettings.Current.ColorSet = value);

		// Pinned to the bottom so the two tabs have one identical button row.
		var footer = new HBoxContainer();
		box.AddChild(footer);

		var close = MakeButton("Close", 18);
		close.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		close.Pressed += () => _settingsOverlay.Visible = false;
		footer.AddChild(close);
	}

	/// <summary>Custom cursor row: file picker (png / jpg / jpeg) next to a Clear button.</summary>
	private void AddCursorPicker(VBoxContainer parent)
	{
		var row = new VBoxContainer();
		row.AddThemeConstantOverride("separation", 2);

		var label = new Label { Text = "Custom Cursor" };
		label.AddThemeFontSizeOverride("font_size", 17);
		row.AddChild(label);

		var line = new HBoxContainer();
		line.AddThemeConstantOverride("separation", 8);
		row.AddChild(line);

		string current = AppSettings.Current.CustomCursor;
		var name = new Label
		{
			Text = string.IsNullOrWhiteSpace(current) ? "Square (default)" : System.IO.Path.GetFileName(current),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			ClipText = true,
			VerticalAlignment = VerticalAlignment.Center,
		};
		name.AddThemeFontSizeOverride("font_size", 16);
		line.AddChild(name);

		var choose = MakeButton("Choose", 16);
		line.AddChild(choose);

		var clear = MakeButton("Clear", 16);
		line.AddChild(clear);

		var dialog = new FileDialog
		{
			Title = "Choose Cursor Image",
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = false,
			Filters = ["*.png ; PNG", "*.jpg, *.jpeg ; JPEG"],
		};
		dialog.FileSelected += path =>
		{
			AppSettings.Current.CustomCursor = path;
			AppSettings.Save();
			name.Text = System.IO.Path.GetFileName(path);
		};
		_settingsOverlay.AddChild(dialog);

		choose.Pressed += () => dialog.PopupCentered(new Vector2I(720, 480));
		clear.Pressed += () =>
		{
			AppSettings.Current.CustomCursor = "";
			AppSettings.Save();
			name.Text = "Square (default)";
		};

		parent.AddChild(row);
	}
	private void AddSlider(VBoxContainer parent, string name, double min, double max, double step, double value, string format, Action<double> apply)
	{
		var row = new VBoxContainer();
		row.AddThemeConstantOverride("separation", 4);

		var header = new HBoxContainer();
		row.AddChild(header);

		var label = new Label { Text = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		label.AddThemeFontSizeOverride("font_size", 17);
		header.AddChild(label);

		var valueLabel = new Label
		{
			Text = value.ToString(format, CultureInfo.InvariantCulture),
			CustomMinimumSize = new Vector2(64, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		valueLabel.AddThemeFontSizeOverride("font_size", 17);
		header.AddChild(valueLabel);

		var slider = new HSlider
		{
			MinValue = min,
			MaxValue = max,
			Step = step,
			Value = value,
			CustomMinimumSize = new Vector2(0, 20),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		slider.ValueChanged += v =>
		{
			apply(v);
			AppSettings.Save();
			valueLabel.Text = v.ToString(format, CultureInfo.InvariantCulture);
		};
		row.AddChild(slider);

		parent.AddChild(row);
	}

	private void AddToggle(VBoxContainer parent, string name, bool value, Action<bool> apply)
	{
		var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 30) };
		parent.AddChild(row);

		var label = new Label
		{
			Text = name,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
		};
		label.AddThemeFontSizeOverride("font_size", 17);
		row.AddChild(label);

		var check = new CheckButton
		{
			ButtonPressed = value,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
		};
		check.Toggled += v =>
		{
			apply(v);
			AppSettings.Save();
		};
		row.AddChild(check);
	}

	private void AddOption(VBoxContainer parent, string name, IReadOnlyList<string> options, string current, Action<string> apply)
	{
		var row = new VBoxContainer();
		row.AddThemeConstantOverride("separation", 4);

		var label = new Label { Text = name };
		label.AddThemeFontSizeOverride("font_size", 17);
		row.AddChild(label);

		var option = new OptionButton
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 30),
		};
		option.AddThemeFontSizeOverride("font_size", 16);

		for (int i = 0; i < options.Count; i++)
		{
			option.AddItem(options[i], i);
		}

		int index = 0;
		for (int i = 0; i < options.Count; i++)
		{
			if (options[i] == current)
			{
				index = i;
				break;
			}
		}

		option.Selected = index;
		option.ItemSelected += selected =>
		{
			apply(options[(int)selected]);
			AppSettings.Save();
		};
		row.AddChild(option);

		parent.AddChild(row);
	}

	private void BuildImportDialog()
	{
		_importDialog = new FileDialog
		{
			Title = "Import Map",
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			UseNativeDialog = false,
			Filters = ["*.sspm ; SSPM Map", "*.phxm ; Phira Map", "* ; All Files"],
		};
		_importDialog.FileSelected += OnFileSelected;
		AddChild(_importDialog);
	}

	private static Button MakeButton(string text, int fontSize)
	{
		var button = new Button { Text = text };
		button.AddThemeFontSizeOverride("font_size", fontSize);
		return button;
	}

	#endregion

	#region Right-click menu

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouse || !mouse.Pressed || mouse.ButtonIndex != MouseButton.Right)
		{
			return;
		}

		if (_settingsOverlay.Visible || _importDialog.Visible)
		{
			return;
		}

		Vector2 mousePosition = GetGlobalMousePosition();

		foreach ((Button button, MapInfo map) in _rows)
		{
			if (!button.GetGlobalRect().HasPoint(mousePosition))
			{
				continue;
			}

			_menuTarget = map;
			_mapMenu.SetItemText(0, map.Favorite ? "Unfavorite" : "Favorite");
			_mapMenu.Hide();
			_mapMenu.Position = (Vector2I)GetViewport().GetMousePosition();
			_mapMenu.Popup();
			break;
		}
	}

	private void OnMapMenuPressed(long id)
	{
		if (_menuTarget == null)
		{
			return;
		}

		if (id == 0)
		{
			ToggleFavorite(_menuTarget);
		}
		else if (id == 1)
		{
			DeleteMap(_menuTarget);
		}
	}

	private void ToggleFavorite(MapInfo map)
	{
		map.Favorite = !map.Favorite;
		MapLibrary.SaveMetadata(map);
		MapLibrary.Sort();

		RefreshMaps();
		HighlightSelection();
	}

	private void DeleteMap(MapInfo map)
	{
		bool wasSelected = _selected != null && _selected.Name == map.Name;

		MapLibrary.Delete(map);
		MapLibrary.Sort();

		if (wasSelected)
		{
			_selected = null;
			StopPreview();
		}

		RefreshMaps();

		if (wasSelected)
		{
			SelectMap(MapLibrary.Maps.FirstOrDefault());
		}
		else
		{
			HighlightSelection();
		}
	}

	#endregion

	#region Maps

	private void RefreshMaps()
	{
		foreach (Node child in _mapList.GetChildren())
		{
			child.QueueFree();
		}

		_rows.Clear();
		_mapGroup = new ButtonGroup();

		foreach (MapInfo map in MapLibrary.Maps)
		{
			var row = new Button
			{
				ToggleMode = true,
				ButtonGroup = _mapGroup,
				Icon = MakeThumbnail(map),
				Text = map.Favorite ? "★  " + map.PrettyTitle : "  " + map.PrettyTitle,
				Alignment = HorizontalAlignment.Left,
				ClipText = true,
				CustomMinimumSize = new Vector2(0, 64),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			row.AddThemeFontSizeOverride("font_size", 17);

			if (map.Favorite)
			{
				row.AddThemeColorOverride("font_color", FavoriteColor);
				row.AddThemeColorOverride("font_pressed_color", FavoriteColor);
				row.AddThemeColorOverride("font_hover_color", FavoriteColor);
			}

			MapInfo captured = map;
			row.Pressed += () => SelectMap(captured);

			_mapList.AddChild(row);
			_rows.Add((row, map));
		}

		if (_rows.Count == 0)
		{
			var empty = new Label { Text = "No maps yet. Press \"Import Map\" and pick a .sspm file. Right-click a map for Favorite / Delete." };
			empty.AddThemeFontSizeOverride("font_size", 16);
			empty.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.75f));
			_mapList.AddChild(empty);
		}
	}

	private static Texture2D MakeThumbnail(MapInfo map)
	{
		Image img = map.LoadCoverImage();

		if (img == null)
		{
			return MakePlaceholder(56);
		}

		img.Convert(Image.Format.Rgba8);
		img.Resize(56, 56, Image.Interpolation.Bilinear);
		return ImageTexture.CreateFromImage(img);
	}

	private static Texture2D MakePlaceholder(int size)
	{
		var placeholder = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		placeholder.Fill(new Color(0.18f, 0.18f, 0.22f));
		return ImageTexture.CreateFromImage(placeholder);
	}

	private void SelectMap(MapInfo map)
	{
		_selected = map;

		if (map == null)
		{
			_cover.Texture = null;
			_infoTitle.Text = "No map selected";
			_infoSub.Text = "";
			_infoStats.Text = "";
			StopPreview();
			return;
		}

		_cover.Texture = map.LoadCover() ?? MakePlaceholder(200);

		_infoTitle.Text = (map.Favorite ? "★ " : "") + map.PrettyTitle;
		_infoSub.Text = $"mapped by {string.Join(", ", map.Mappers)}";
		_startSlider.Value = 0;
		Session.StartFrom = 0;
		UpdateSpeedDependentInfo();

		HighlightSelection();
		StartPreview(map);
	}

	/// <summary>Length and the start-from range are real time, so they follow the speed.</summary>
	private void UpdateSpeedDependentInfo()
	{
		if (_selected == null)
		{
			return;
		}

		double speed = Math.Max(0.01, Session.Speed);

		_infoStats.Text = $"Notes: {_selected.LoadNotes().Length}   Length: {FormatTime(_selected.Length / 1000.0 / speed)}";

		double max = Math.Max(1000, _selected.Length / speed);
		double value = Math.Clamp(_startSlider.Value, 0, max);

		_startSlider.MaxValue = max;
		_startSlider.Value = value;
		_startValue.Text = FormatTime(value / 1000.0);
	}

	private void HighlightSelection()
	{
		foreach ((Button button, MapInfo candidate) in _rows)
		{
			button.ButtonPressed = _selected != null && candidate.Name == _selected.Name;
		}
	}

	private static string SpeedLabel(double speed) => speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";

	private static string FormatTime(double seconds)
	{
		if (double.IsNaN(seconds) || seconds < 0)
		{
			seconds = 0;
		}

		int total = (int)Math.Floor(seconds);
		return $"{total / 60}:{total % 60:00}";
	}

	#endregion

	#region Music preview

	private void StartPreview(MapInfo map)
	{
		StopPreview();

		AudioStream stream = map.LoadAudio();
		if (stream == null)
		{
			return;
		}

		_musicPlayer.Stream = stream;
		_musicPlayer.PitchScale = (float)Session.Speed;
		_musicPlayer.VolumeDb = AppSettings.VolumeToDb(AppSettings.Current.MusicVolume);
		_musicPlayer.Play();
	}

	private void StopPreview()
	{
		_musicPlayer.Stop();
		_musicPlayer.Stream = null;
	}

	private void UpdateMusicVolume()
	{
		_musicPlayer.VolumeDb = AppSettings.VolumeToDb(AppSettings.Current.MusicVolume);
	}

	#endregion

	#region Actions

	private void SetSpeed(double value)
	{
		Session.Speed = value;
		UpdateSpeedButtons();
		UpdateSpeedDependentInfo();

		// The menu preview follows the chosen game speed.
		if (_musicPlayer.Playing)
		{
			_musicPlayer.PitchScale = (float)value;
		}
	}

	private void UpdateSpeedButtons()
	{
		foreach ((Button button, double value) in _speedButtons)
		{
			button.ButtonPressed = Math.Abs(value - Session.Speed) < 0.0001;
		}

		_speedValue.Text = SpeedLabel(Session.Speed);
	}

	private void OnPlayPressed()
	{
		if (_selected == null)
		{
			return;
		}

		Session.SelectedMap = _selected;
		StopPreview();
		GetTree().ChangeSceneToFile("res://scenes/Game.tscn");
	}

	private void OnFileSelected(string path)
	{
		try
		{
			MapInfo imported = MapLibrary.Import(path);
			MapLibrary.LoadAll();

			RefreshMaps();

			MapInfo match = MapLibrary.Maps.FirstOrDefault(m => m.Name == imported.Name);
			SelectMap(match);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Import failed: {e}");
		}
	}

	#endregion
}
