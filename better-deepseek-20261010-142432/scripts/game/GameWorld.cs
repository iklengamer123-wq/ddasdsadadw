using System;
using System.IO;
using Godot;
using CustomRhythia.Map;
using CustomRhythia.UI;

namespace CustomRhythia.Game;

/// <summary>
/// Root 3D scene for gameplay. Owns camera, frame, cursor, notes, audio,
/// runner, hit sound pool and pause overlay.
/// </summary>
public partial class GameWorld : Node3D
{
    public Runner Runner { get; private set; }
    public Attempt Attempt { get; private set; }
    public Camera3D Camera { get; private set; }
    public bool Paused { get; private set; }

    private CursorController _cursor;
    private NoteRenderer _notes;
    private AudioStreamPlayer _song;
    private AudioStreamPlayer[] _hitSounds;

    private double _rawX;
    private double _rawY;
    private bool _inputCaptured = true;
    private bool _quitting = false;

    private CanvasLayer _pauseLayer;
    private PauseMenu _pauseMenu;

    private const int HitSoundVoices = 4;

    [Signal] public delegate void GameEndedEventHandler();

    public void Initialize(Map.Map map, double speed, double startFrom, bool noFail)
    {
        Attempt = new Attempt(map, speed, startFrom, noFail);

        // Godot's default is to coalesce mouse events into one per frame,
        // which is fine and much cheaper than the raw firehose.
        Input.UseAccumulatedInput = true;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        float fov = (float)SettingsManager.Current.FoV;
        Camera = new Camera3D
        {
            Position = new Vector3(0, 0, 3.75f),
            Rotation = Vector3.Zero,
            Fov = fov,
            Current = true,
        };
        AddChild(Camera);

        AddChild(BuildFrame());

        _cursor = new CursorController();
        AddChild(_cursor);

        _notes = new NoteRenderer();
        AddChild(_notes);

        Runner = new Runner();
        AddChild(Runner);
        _notes.Bind(Runner);

        _song = new AudioStreamPlayer { Bus = "Master" };
        AddChild(_song);

        if (map.HasAudio)
        {
            var stream = LoadAudio(map.AudioPath, map.AudioExt);
            if (stream != null)
            {
                _song.Stream = stream;
                _song.VolumeDb = ComputeVolume((float)SettingsManager.Current.VolumeMusic);
                _song.PitchScale = (float)speed;

                // PRE-ROLL. The heavy part of playing an MP3 at an arbitrary
                // offset is the initial seek + decode. Do it right now during
                // scene load: start the song a bit BEFORE the target position
                // so that by the time gameplay Progress reaches StartFrom,
                // the song playback position matches exactly.
                //
                // No pause/unpause involved — Godot's MP3 pause/resume has a
                // spin-up cost that shows up as a stutter right when you'd
                // otherwise be hitting the first note.
                double leadInMs = startFrom > 0 ? 2000.0 : 1000.0;
                if (startFrom >= leadInMs)
                {
                    double preStartSec = (startFrom - leadInMs) / 1000.0;
                    _song.Play((float)preStartSec);
                }
                // Else: no pre-roll possible. Runner calls Play(startSec)
                // when Progress reaches StartFrom.
            }
        }

        _hitSounds = new AudioStreamPlayer[HitSoundVoices];
        AudioStream hitStream = LoadHitSound();
        for (int i = 0; i < HitSoundVoices; i++)
        {
            var p = new AudioStreamPlayer { Bus = "Master" };
            if (hitStream != null)
            {
                p.Stream = hitStream;
                p.VolumeDb = ComputeVolume((float)SettingsManager.Current.VolumeHitSound);
            }
            AddChild(p);
            _hitSounds[i] = p;
        }

        Runner.Setup(Attempt, _song, _hitSounds);
        Runner.AttemptFinished += OnAttemptFinished;

        _pauseLayer = new CanvasLayer
        {
            Layer = 10,
            ProcessMode = ProcessModeEnum.WhenPaused,
        };
        AddChild(_pauseLayer);

        _pauseMenu = new PauseMenu { ProcessMode = ProcessModeEnum.WhenPaused };
        _pauseMenu.Visible = false;
        _pauseMenu.OnResume = TogglePause;
        _pauseMenu.OnRestart = OnRestartRequested;
        _pauseMenu.OnQuit = OnQuitRequested;
        _pauseLayer.AddChild(_pauseMenu);

        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _ExitTree()
    {
        if (GetTree() != null) GetTree().Paused = false;
    }

    public override void _Process(double delta)
    {
        if (Runner == null || Paused) return;

        float parallax = (float)SettingsManager.Current.CameraParallax;
        Camera.Position = new Vector3(
            (float)(_cursor.X * parallax),
            (float)(_cursor.Y * parallax),
            3.75f);

        Runner.SetCursor(_cursor.X, _cursor.Y);
    }

    public override void _Input(InputEvent @event)
    {
        if (_cursor == null) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Escape:
                    if (!Attempt.Stopped) TogglePause();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Space:
                    if (!Paused) Runner.Skip();
                    return;
            }
        }

        if (Paused) return;

        if (@event is InputEventMouseMotion motion && _inputCaptured)
        {
            double sens = SettingsManager.Current.Sensitivity;
            _rawX += motion.Relative.X * sens / 120.0;
            _rawY += -motion.Relative.Y * sens / 120.0;

            _rawX = Mathf.Clamp((float)_rawX, -Constants.BOUNDS.X, Constants.BOUNDS.X);
            _rawY = Mathf.Clamp((float)_rawY, -Constants.BOUNDS.Y, Constants.BOUNDS.Y);

            _cursor.SetPosition(_rawX, _rawY);

            GetViewport().SetInputAsHandled();
        }
    }

    public void TogglePause()
    {
        SetPaused(!Paused);
    }

    public void SetPaused(bool paused)
    {
        if (Attempt == null || Attempt.Stopped) return;

        if (paused && !Paused && Attempt.Progress > Attempt.StartFrom - 500)
        {
            Attempt.Pauses = Attempt.Pauses + 1;
        }

        Paused = paused;

        if (_song != null)
        {
            _song.StreamPaused = paused;
        }

        GetTree().Paused = paused;

        if (!paused && Runner != null)
        {
            Runner.NotifyResumed();
            if (_song != null && _song.Playing)
            {
                _song.Seek((float)(Attempt.Progress / 1000.0));
            }
        }

        _pauseMenu.Visible = paused;
        _pauseMenu.SetFocus(paused);

        Input.MouseMode = paused ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    private void OnRestartRequested()
    {
        SetPaused(false);
        Main.Instance.StartGame(Attempt.Map, Attempt.Speed, Attempt.StartFrom);
    }

    private void OnQuitRequested()
    {
        SetPaused(false);
        _quitting = true;

        if (_song != null) _song.Stop();

        Main.Instance.ShowMainMenu();
    }

    private void OnAttemptFinished()
    {
        if (_quitting) return;
        EmitSignal(SignalName.GameEnded);
    }

    private MeshInstance3D BuildFrame()
    {
        var immediate = new ImmediateMesh();
        immediate.SurfaceBegin(Mesh.PrimitiveType.LineStrip);

        float ext = (float)(Constants.GRID_SIZE / 2);
        immediate.SurfaceSetColor(new Color(1, 1, 1, 0.5f));

        immediate.SurfaceAddVertex(new Vector3(-ext, -ext, -0.01f));
        immediate.SurfaceAddVertex(new Vector3( ext, -ext, -0.01f));
        immediate.SurfaceAddVertex(new Vector3( ext,  ext, -0.01f));
        immediate.SurfaceAddVertex(new Vector3(-ext,  ext, -0.01f));
        immediate.SurfaceAddVertex(new Vector3(-ext, -ext, -0.01f));

        immediate.SurfaceEnd();

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        immediate.SurfaceSetMaterial(0, mat);

        return new MeshInstance3D
        {
            Mesh = immediate,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static AudioStream LoadAudio(string path, string ext)
    {
        try
        {
            if (ext == "ogg") return AudioStreamOggVorbis.LoadFromFile(path);
            return AudioStreamMP3.LoadFromFile(path);
        }
        catch (Exception ex)
        {
            GD.PushError("Failed to load audio: " + ex.Message);
            return null;
        }
    }

    private static AudioStream LoadHitSound()
    {
        string name = SettingsManager.Current.HitSoundFile;
        if (string.IsNullOrEmpty(name)) return null;

        string path = $"{Constants.SoundsFolder}/{name}";
        if (!File.Exists(path)) return null;

        try
        {
            return AudioStreamMP3.LoadFromFile(path);
        }
        catch (Exception ex)
        {
            GD.PushWarning("Could not load hit sound: " + ex.Message);
            return null;
        }
    }

    public static float ComputeVolume(float volume)
    {
        if (volume <= 0) return -80f;
        return (float)(-80 + 70 * Math.Pow(volume / 100.0, 0.1));
    }
}
