using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace SoundSpace;

/// <summary>
/// Gameplay scene.
///
/// Faithful port of the Rhythia playfield: a 3x3 grid centred on the screen is viewed by a
/// Camera3D sitting 3.75 units away with a 70 degree FOV. Notes fly in from the distance
/// (z = -depth) towards the grid plane (z = 0) and must be hit with the mouse cursor.
/// The camera is offset by cursor * parallax, so the frame, the cursor and the two counters
/// at the right of the grid all move together while notes scroll inside.
/// The song name, the running timer and the modifier line are drawn on a CanvasLayer,
/// so they have no parallax.
/// </summary>
public partial class GameScene : Node3D
{
	// "Modifiers" line: the speed value is 20% brighter than the label.
	private static readonly Color ModifiersColor = new(0.62f, 0.62f, 0.68f);
	private static readonly Color ModifiersSpeedColor = new(0.62f * 1.2f, 0.62f * 1.2f, 0.68f * 1.2f);

	private MapInfo _map;
	private NoteData[] _notes = [];
	private bool[] _resolved = [];
	private Color[] _noteColors = Constants.NoteColors;

	private Camera3D _camera;
	private Node3D _world;
	private MultiMeshInstance3D _noteInstance;
	private MultiMesh _multimesh;
	private Node3D _cursor;
	private Label3D _missLabel;
	private Label3D _accuracyLabel;
	private Label _titleLabel;
	private Label _timerLabel;
	private VBoxContainer _modifiersRow;
	private Label _modifiersSpeedLabel;
	private AudioStreamPlayer _player;

	// Settings cached once (they cannot change during a run) to keep the frame loop cheap.
	private double _approachRate = 30.0;
	private double _spawnDistance = 15.0;
	private float _noteSize = 0.8f;
	private double _noteOpacity = 0.95;

	private bool _halfGhost;


	private float _parallax = 0.25f;
	private float _sensitivity = 0.5f;

	private double _speed = 1.0;
	private double _progress;
	private double _endTime;
	private double _audioLength;
	private double _duration;
	private double _startFrom;
	private int _startIndex;
	private int _firstUnresolved;
	private int _hits;
	private int _misses;
	private double _accuracy = 100.0;
	private Vector2 _cursorPos = Vector2.Zero;
	private bool _playing;
	private bool _paused;
	private bool _finished;

	private Control _overlay;
	private ColorRect _overlayDim;
	private TextureRect _overlayCover;
	private Label _overlayTitle;
	private Label _overlayText;
	private Button _overlayPrimary;

	public override void _Ready()
	{
		_map = Session.SelectedMap;

		if (_map == null)
		{
			BackToMenu();
			return;
		}

		AppSettings.Load();
		_noteColors = ColorSets.Load(AppSettings.Current.ColorSet);

		_approachRate = AppSettings.Current.ApproachRate;
		_spawnDistance = AppSettings.Current.SpawnDistance;
		_noteSize = (float)AppSettings.Current.NoteSize;
		_noteOpacity = AppSettings.Current.NoteOpacity;
		_halfGhost = AppSettings.Current.HalfGhost;
		_parallax = (float)AppSettings.Current.Parallax;
		_sensitivity = (float)AppSettings.Current.Sensitivity;

		_speed = Math.Clamp(Session.Speed, 0.1, 4.0);
		_notes = _map.LoadNotes();
		_resolved = new bool[_notes.Length];

		// Notes before the chosen start point are skipped entirely (Rhythia's StartFrom).
		_startFrom = Math.Max(0, Session.StartFrom);
		_startIndex = 0;
		while (_startIndex < _notes.Length && _notes[_startIndex].Ms < _startFrom)
		{
			_startIndex++;
		}

		_firstUnresolved = _startIndex;

		BuildWorld();
		BuildHud();
		BuildOverlay();

		// Only show the modifier line when a speed modifier is active.
		if (Math.Abs(_speed - 1.0) > 0.0001)
		{
			_modifiersSpeedLabel.Text = _speed.ToString("0.##", CultureInfo.InvariantCulture) + "x";
			_modifiersRow.Visible = true;
		}

		_progress = _startFrom - AppSettings.Current.ApproachTime * 1000.0 - 1000.0 * _speed;

		_endTime = Math.Max(_map.Length, _audioLength * 1000.0);
		if (_endTime <= 0)
		{
			_endTime = _notes.Length > 0 ? _notes[^1].Ms : 0;
		}

		_endTime += 800.0;

		_duration = _audioLength > 0 ? _audioLength : _map.Length / 1000.0;
		_titleLabel.Text = _map.PrettyTitle;

		// Low input latency: no v-sync, uncapped frame rate, no input accumulation.
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;
		Input.UseAccumulatedInput = false;

		Input.MouseMode = Input.MouseModeEnum.Captured;
		_playing = true;
	}

	#region Scene construction

	private void BuildWorld()
	{
		_camera = new Camera3D
		{
			Position = new Vector3(0, 0, (float)Constants.CameraZ),
			Fov = (float)Constants.Fov,
			Near = 0.05f,
			Far = 4000f,
			Current = true,
		};
		AddChild(_camera);

		var environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color(0.03f, 0.03f, 0.05f),
		};
		AddChild(new WorldEnvironment { Environment = environment });

		_world = new Node3D();
		AddChild(_world);

		BuildBackground();
		BuildFrame();
		BuildNotes();
		BuildCursor();
		BuildCounters();
		BuildAudio();
	}

	private void BuildBackground()
	{
		// A very distant gradient acts like a sky: it is always behind the notes.
		var material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoTexture = MakeGradientTexture(new Color(0.11f, 0.10f, 0.17f), new Color(0.02f, 0.02f, 0.04f)),
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		AddQuad(_world, new Vector3(0, 0, -1500), new Vector2(6000, 6000), material);
	}

	private void BuildFrame()
	{
		// Only the square frame - no grid guides.
		var frameMaterial = MakeMaterial(new Color(1, 1, 1, 0.85f));

		const float z = -0.01f;
		const float thickness = 0.04f;
		const float half = 1.5f;

		AddQuad(_world, new Vector3(0, half, z), new Vector2(2 * half + thickness, thickness), frameMaterial);
		AddQuad(_world, new Vector3(0, -half, z), new Vector2(2 * half + thickness, thickness), frameMaterial);
		AddQuad(_world, new Vector3(-half, 0, z), new Vector2(thickness, 2 * half + thickness), frameMaterial);
		AddQuad(_world, new Vector3(half, 0, z), new Vector2(thickness, 2 * half + thickness), frameMaterial);
	}

	private void BuildNotes()
	{
		var material = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = Colors.White,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

		ArrayMesh mesh = BuildNoteMesh(AppSettings.Current.NoteStyle);
		mesh.SurfaceSetMaterial(0, material);

		_multimesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			UseColors = true,
			Mesh = mesh,
			InstanceCount = 0,
			VisibleInstanceCount = 0,
		};

		_noteInstance = new MultiMeshInstance3D
		{
			Multimesh = _multimesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_world.AddChild(_noteInstance);
	}

	/// <summary>
	/// Builds the hollow note outline mesh (spans -0.5 .. 0.5, scaled per note later).
	/// "Square" = rounded corners, "Thin" = sharp corners with a thinner border.
	/// </summary>
	private static ArrayMesh BuildNoteMesh(string style)
	{
		(float thickness, float radius, int cornerSegments) = style switch
		{
			"Thin" => (0.087f, 0.0f, 1),
			_ => (0.09f, 0.25f, 8),
		};

		List<Vector2> outer = RoundedRectPoints(0.5f, radius, cornerSegments);
		List<Vector2> inner = RoundedRectPoints(0.5f - thickness, Mathf.Max(0f, radius - thickness), cornerSegments);

		var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);

		int count = outer.Count;

		for (int i = 0; i < count; i++)
		{
			int j = (i + 1) % count;

			var oi = new Vector3(outer[i].X, outer[i].Y, 0);
			var oj = new Vector3(outer[j].X, outer[j].Y, 0);
			var ii = new Vector3(inner[i].X, inner[i].Y, 0);
			var ij = new Vector3(inner[j].X, inner[j].Y, 0);

			surface.AddVertex(oi);
			surface.AddVertex(ii);
			surface.AddVertex(oj);

			surface.AddVertex(oj);
			surface.AddVertex(ii);
			surface.AddVertex(ij);
		}

		return surface.Commit();
	}

	/// <summary>Point loop of a rounded rectangle (clockwise, starting on the top edge).</summary>
	private static List<Vector2> RoundedRectPoints(float half, float radius, int cornerSegments)
	{
		var points = new List<Vector2>();
		float straight = Mathf.Max(0f, half - radius);

		points.Add(new Vector2(-straight, half));

		// top-right
		for (int i = 1; i <= cornerSegments; i++)
		{
			float a = Mathf.DegToRad(Mathf.Lerp(90f, 0f, (float)i / cornerSegments));
			points.Add(new Vector2(straight + radius * Mathf.Cos(a), straight + radius * Mathf.Sin(a)));
		}

		points.Add(new Vector2(half, -straight));

		// bottom-right
		for (int i = 1; i <= cornerSegments; i++)
		{
			float a = Mathf.DegToRad(Mathf.Lerp(0f, -90f, (float)i / cornerSegments));
			points.Add(new Vector2(straight + radius * Mathf.Cos(a), -straight + radius * Mathf.Sin(a)));
		}

		points.Add(new Vector2(-straight, -half));

		// bottom-left
		for (int i = 1; i <= cornerSegments; i++)
		{
			float a = Mathf.DegToRad(Mathf.Lerp(-90f, -180f, (float)i / cornerSegments));
			points.Add(new Vector2(-straight + radius * Mathf.Cos(a), -straight + radius * Mathf.Sin(a)));
		}

		points.Add(new Vector2(-half, straight));

		// top-left
		for (int i = 1; i <= cornerSegments; i++)
		{
			float a = Mathf.DegToRad(Mathf.Lerp(180f, 90f, (float)i / cornerSegments));
			points.Add(new Vector2(-straight + radius * Mathf.Cos(a), straight + radius * Mathf.Sin(a)));
		}

		return points;
	}

	private void BuildCursor()
	{
		_cursor = new Node3D
		{
			Scale = Vector3.One * (float)AppSettings.Current.CursorScale,
		};
		_world.AddChild(_cursor);

		// A custom cursor image replaces the square; without one the square is kept.
		Texture2D custom = LoadCustomCursor();

		if (custom != null)
		{
			var sprite = new StandardMaterial3D
			{
				AlbedoTexture = custom,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			};

			const float spriteSize = 0.34f;
			AddQuad(_cursor, new Vector3(0, 0, 0.03f), new Vector2(spriteSize, spriteSize), sprite);
			return;
		}

		var ring = MakeMaterial(new Color(1, 1, 1, 0.9f));
		var halo = MakeMaterial(new Color(1, 1, 1, 0.12f));

		AddQuad(_cursor, new Vector3(0, 0, 0.03f), new Vector2(0.30f, 0.30f), halo);

		const float size = 0.13f;
		const float thickness = 0.016f;

		AddQuad(_cursor, new Vector3(0, size, 0.02f), new Vector2(2 * size + thickness, thickness), ring);
		AddQuad(_cursor, new Vector3(0, -size, 0.02f), new Vector2(2 * size + thickness, thickness), ring);
		AddQuad(_cursor, new Vector3(-size, 0, 0.02f), new Vector2(thickness, 2 * size + thickness), ring);
		AddQuad(_cursor, new Vector3(size, 0, 0.02f), new Vector2(thickness, 2 * size + thickness), ring);
	}

	/// <summary>Loads the custom cursor image (png / jpg / jpeg). Returns null when not set or unreadable.</summary>
	private static Texture2D LoadCustomCursor()
	{
		string path = AppSettings.Current.CustomCursor;

		if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
		{
			return null;
		}

		try
		{
			Image image = DecodeCursorImage(System.IO.File.ReadAllBytes(path));
			return image == null ? null : ImageTexture.CreateFromImage(image);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to load cursor image: {e}");
			return null;
		}
	}

	/// <summary>Decodes a cursor image by its content (PNG / JPEG / BMP / WEBP).</summary>
	private static Image DecodeCursorImage(byte[] buffer)
	{
		return MapInfo.DecodeImage(buffer);
	}

	private void BuildCounters()
	{
		// Placed at z = 0, right of the grid, vertically centred, so they move with the frame.
		_accuracyLabel = MakeLabel(new Vector3(1.72f, 0.20f, 0), "Accuracy: 100.00%");
		_missLabel = MakeLabel(new Vector3(1.72f, -0.20f, 0), "Misses: 0");

		_world.AddChild(_accuracyLabel);
		_world.AddChild(_missLabel);
	}

	private void BuildAudio()
	{
		_player = new AudioStreamPlayer();
		AddChild(_player);

		if (_map.HasAudio)
		{
			AudioStream stream = _map.LoadAudio();
			if (stream != null)
			{
				_player.Stream = stream;
				_player.PitchScale = (float)_speed;
				_player.VolumeDb = AppSettings.VolumeToDb(AppSettings.Current.MusicVolume);
				_audioLength = stream.GetLength();
			}
		}
	}

	private void BuildHud()
	{
		var layer = new CanvasLayer();
		AddChild(layer);

		var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(root);

		// Top of the screen, no parallax.
		var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		box.OffsetTop = 12;
		box.OffsetBottom = 96;
		box.AddThemeConstantOverride("separation", 2);
		root.AddChild(box);

		_titleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titleLabel.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(_titleLabel);

		_timerLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_timerLabel.AddThemeFontSizeOverride("font_size", 20);
		_timerLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.86f));
		box.AddChild(_timerLabel);

		// Bottom of the screen, no parallax (hidden unless a speed modifier is active).
		_modifiersRow = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_modifiersRow.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_modifiersRow.OffsetLeft = 24;
		_modifiersRow.OffsetTop = -104;
		_modifiersRow.OffsetBottom = -24;
		_modifiersRow.AddThemeConstantOverride("separation", 2);
		root.AddChild(_modifiersRow);

		var modifiersLabel = new Label { Text = "Modifiers:" };
		modifiersLabel.AddThemeFontSizeOverride("font_size", 20);
		modifiersLabel.AddThemeColorOverride("font_color", ModifiersColor);
		_modifiersRow.AddChild(modifiersLabel);

		_modifiersSpeedLabel = new Label();
		_modifiersSpeedLabel.AddThemeFontSizeOverride("font_size", 24);
		_modifiersSpeedLabel.AddThemeColorOverride("font_color", ModifiersSpeedColor);
		_modifiersRow.AddChild(_modifiersSpeedLabel);
	}

	private void BuildOverlay()
	{
		var layer = new CanvasLayer { Layer = 10 };
		AddChild(layer);

		_overlay = new Control { Visible = false };
		_overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_overlay);

		_overlayDim = new ColorRect { Color = new Color(0, 0, 0, 0.72f) };
		_overlayDim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_overlay.AddChild(_overlayDim);

		var center = new CenterContainer();
		center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_overlay.AddChild(center);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 14);
		center.AddChild(box);

		_overlayCover = new TextureRect
		{
			CustomMinimumSize = new Vector2(200, 200),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Visible = false,
		};
		box.AddChild(_overlayCover);

		_overlayTitle = new Label { Text = "Paused", HorizontalAlignment = HorizontalAlignment.Center };
		_overlayTitle.AddThemeFontSizeOverride("font_size", 32);
		box.AddChild(_overlayTitle);

		_overlayText = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_overlayText.AddThemeFontSizeOverride("font_size", 20);
		box.AddChild(_overlayText);

		_overlayPrimary = new Button { Text = "Resume", CustomMinimumSize = new Vector2(260, 54) };
		_overlayPrimary.AddThemeFontSizeOverride("font_size", 22);
		_overlayPrimary.Pressed += OnOverlayPrimary;
		box.AddChild(_overlayPrimary);

		var menu = new Button { Text = "Back to Menu", CustomMinimumSize = new Vector2(260, 54) };
		menu.AddThemeFontSizeOverride("font_size", 22);
		menu.Pressed += BackToMenu;
		box.AddChild(menu);
	}

	#endregion

	#region Helpers

	private static StandardMaterial3D MakeMaterial(Color color) =>
		new()
		{
			AlbedoColor = color,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};

	private static void AddQuad(Node parent, Vector3 position, Vector2 size, Material material)
	{
		var instance = new MeshInstance3D
		{
			Mesh = new QuadMesh { Size = size },
			Position = position,
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(instance);
	}

	private static Label3D MakeLabel(Vector3 position, string text) =>
		new()
		{
			Text = text,
			Position = position,
			FontSize = 40,
			PixelSize = 0.0035f,
			OutlineSize = 8,
			OutlineModulate = new Color(0, 0, 0, 0.85f),
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center,
			NoDepthTest = true,
		};

	private static Texture2D MakeOverlayPlaceholder()
	{
		var image = Image.CreateEmpty(256, 256, false, Image.Format.Rgba8);
		image.Fill(new Color(0.18f, 0.18f, 0.22f));
		return ImageTexture.CreateFromImage(image);
	}

	private static Texture2D MakeGradientTexture(Color top, Color bottom)
	{
		var image = Image.CreateEmpty(2, 256, false, Image.Format.Rgba8);

		for (int y = 0; y < 256; y++)
		{
			float t = y / 255f;
			Color color = bottom.Lerp(top, 1f - t); // image row 0 (top) -> top color
			image.SetPixel(0, y, color);
			image.SetPixel(1, y, color);
		}

		return ImageTexture.CreateFromImage(image);
	}

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

	#region Input

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion motion)
		{
			if (_playing && !_paused && !_finished)
			{
				var delta = new Vector2(motion.Relative.X, -motion.Relative.Y) * (_sensitivity / 120f);
				float bound = (float)Constants.Bounds.X;

				_cursorPos = new Vector2(
					Mathf.Clamp(_cursorPos.X + delta.X, -bound, bound),
					Mathf.Clamp(_cursorPos.Y + delta.Y, -bound, bound));
			}

			return;
		}

		if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
		{
			if (_finished)
			{
				BackToMenu();
			}
			else
			{
				TogglePause();
			}
		}
	}

	#endregion

	#region Loop

	public override void _Process(double delta)
	{
		if (!_playing || _paused || _finished)
		{
			return;
		}

		_progress += delta * 1000.0 * _speed;

		UpdateAudio();
		ProcessNotes();
		UpdateCursorAndCamera();
		RenderNotes();
		UpdateHud();

		if (_progress >= _endTime)
		{
			Finish();
		}
	}

	private void UpdateAudio()
	{
		if (_player.Stream == null)
		{
			return;
		}

		double songPosition = _progress / 1000.0;

		// Before the song starts, or after it ended.
		if (_progress < 0 || songPosition >= _audioLength)
		{
			if (_player.Playing)
			{
				_player.Stop();
			}

			return;
		}

		// Start / resume / restart at the current chart position
		// (this is what makes the song come back after a pause).
		if (!_player.Playing)
		{
			_player.PitchScale = (float)_speed;
			_player.Play((float)songPosition);
			return;
		}

		// Gentle pitch correction keeps audio and chart in sync (same idea as Rhythia).
		double drift = songPosition - _player.GetPlaybackPosition();

		if (Math.Abs(drift) > 0.04)
		{
			_player.PitchScale = (float)Math.Clamp(_speed + drift, _speed - 0.25, _speed + 0.25);
		}
		else
		{
			_player.PitchScale = (float)_speed;
		}
	}

	private void ProcessNotes()
	{
		while (_firstUnresolved < _notes.Length && _resolved[_firstUnresolved])
		{
			_firstUnresolved++;
		}

		double window = Constants.HitWindow * _speed;

		for (int i = _firstUnresolved; i < _notes.Length; i++)
		{
			NoteData note = _notes[i];

			if (note.Ms > _progress + window)
			{
				break;
			}

			if (_resolved[i] || _progress < note.Ms)
			{
				continue;
			}

			if (_progress > note.Ms + window)
			{
				Resolve(i, false);
			}
			else if (IsCursorOnNote(note))
			{
				Resolve(i, true);
			}
		}
	}

	private bool IsCursorOnNote(NoteData note)
	{
		const float tolerance = (float)Constants.HitBoxSize;

		return _cursorPos.X + tolerance >= note.X - 0.5f
			&& _cursorPos.X - tolerance <= note.X + 0.5f
			&& _cursorPos.Y + tolerance >= note.Y - 0.5f
			&& _cursorPos.Y - tolerance <= note.Y + 0.5f;
	}

	private void Resolve(int index, bool hit)
	{
		_resolved[index] = true;

		if (hit)
		{
			_hits++;
		}
		else
		{
			_misses++;
		}

		int sum = _hits + _misses;
		_accuracy = sum == 0 ? 100.0 : Math.Floor((double)_hits / sum * 10000.0) / 100.0;
	}

	private void UpdateCursorAndCamera()
	{
		_camera.Position = new Vector3(_cursorPos.X * _parallax, _cursorPos.Y * _parallax, (float)Constants.CameraZ);
		_cursor.Position = new Vector3(_cursorPos.X, _cursorPos.Y, 0);
	}

	private void RenderNotes()
	{
		double ar = _approachRate;
		double ad = _spawnDistance;
		double scale = 1000.0 * _speed;

		// First pass: how many notes are currently visible.
		int visible = 0;
		for (int i = _firstUnresolved; i < _notes.Length; i++)
		{
			if (_resolved[i])
			{
				continue;
			}

			double depth = (_notes[i].Ms - _progress) / scale * ar;
			if (depth > ad)
			{
				break;
			}

			// Notes that already passed the playfield are not drawn.
			if (depth < 0)
			{
				continue;
			}

			visible++;
		}

		// Grow the buffer in chunks so we do not reallocate every frame.
		if (visible > _multimesh.InstanceCount)
		{
			_multimesh.InstanceCount = (visible + 63) / 64 * 64;
		}

		if (_multimesh.VisibleInstanceCount != visible)
		{
			_multimesh.VisibleInstanceCount = visible;
		}

		Basis basis = Basis.Identity.Scaled(new Vector3(_noteSize, _noteSize, 1));
		int colorCount = _noteColors.Length;

		int index = 0;
		for (int i = _firstUnresolved; i < _notes.Length && index < visible; i++)
		{
			if (_resolved[i])
			{
				continue;
			}

			NoteData note = _notes[i];
			double depth = (note.Ms - _progress) / scale * ar;

			if (depth > ad)
			{
				break;
			}

			if (depth < 0)
			{
				continue;
			}

			float fadeIn = Mathf.Clamp((float)(1.0 - depth / ad) / 0.12f, 0f, 1f);

			float alpha = fadeIn * (float)_noteOpacity;

			// Half Ghost = Rhythia's Fade Out at 100% (NoteRenderer.cs, the legacy nightly
			// "Half Ghost"): notes fade out while approaching the hit moment, i.e. as their
			// progress towards the plane goes from 0 to 1.
			if (_halfGhost)
			{
				float progress = Mathf.Clamp((float)(1.0 - depth / ad), 0f, 1f);
				alpha *= 1f - progress;
			}

			alpha = Mathf.Clamp(alpha, 0f, 1f);

			Color color = _noteColors[i % colorCount];
			color.A = alpha;

			_multimesh.SetInstanceTransform(index, new Transform3D(basis, new Vector3(note.X, note.Y, (float)-depth)));
			_multimesh.SetInstanceColor(index, color);
			index++;
		}
	}

	private void UpdateHud()
	{
		// Only touch the labels when their text actually changes (text updates are expensive).
		string missText = $"Misses: {_misses}";
		if (_missLabel.Text != missText)
		{
			_missLabel.Text = missText;
		}

		string accuracyText = $"Accuracy: {_accuracy:0.00}%";
		if (_accuracyLabel.Text != accuracyText)
		{
			_accuracyLabel.Text = accuracyText;
		}

		string timerText = $"{FormatTime(Math.Max(0, _progress) / 1000.0)} / {FormatTime(_duration)}";
		if (_timerLabel.Text != timerText)
		{
			_timerLabel.Text = timerText;
		}
	}

	#endregion

	#region States

	private void TogglePause()
	{
		if (_finished)
		{
			return;
		}

		_paused = !_paused;

		if (_paused)
		{
			Input.MouseMode = Input.MouseModeEnum.Visible;
			_overlayDim.Color = new Color(0, 0, 0, 0.72f);
			_overlayCover.Visible = false;
			_overlayTitle.Text = "Paused";
			_overlayText.Text = "";
			_overlayPrimary.Text = "Resume";
			_overlay.Visible = true;

			// Pause the stream where it is instead of stopping it: resuming then
			// never has to decode the track up to the pause position again.
			_player.StreamPaused = true;
		}
		else
		{
			Input.MouseMode = Input.MouseModeEnum.Captured;
			_overlay.Visible = false;
			_player.StreamPaused = false;
		}
	}

	private void Finish()
	{
		_playing = false;
		_finished = true;

		if (_player.Playing)
		{
			_player.Stop();
		}

		Input.MouseMode = Input.MouseModeEnum.Visible;

		// Fully black background so the finished game is not visible behind the results.
		_overlayDim.Color = Colors.Black;
		_overlayCover.Texture = _map.LoadCover() ?? MakeOverlayPlaceholder();
		_overlayCover.Visible = true;
		_overlayTitle.Text = _map.PrettyTitle;
		_overlayText.Text =
			$"Duration: {FormatTime(_duration)}\n"
			+ $"Start: {FormatTime(_startFrom / 1000.0)}\n"
			+ $"Speed: {_speed.ToString("0.##", CultureInfo.InvariantCulture)}x\n"
			+ $"Accuracy: {_accuracy.ToString("0.00", CultureInfo.InvariantCulture)}%\n"
			+ $"Misses: {_misses}";
		_overlayPrimary.Text = "Retry";
		_overlay.Visible = true;

		UpdateHud();
	}

	private void OnOverlayPrimary()
	{
		if (_finished)
		{
			GetTree().ReloadCurrentScene();
		}
		else if (_paused)
		{
			TogglePause();
		}
	}

	private void BackToMenu()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;

		if (IsInsideTree())
		{
			GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
		}
	}

	#endregion
}
