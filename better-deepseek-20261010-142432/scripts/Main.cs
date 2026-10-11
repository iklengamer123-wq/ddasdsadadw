using System;
using System.IO;
using Godot;
using CustomRhythia.Game;
using CustomRhythia.Map;
using CustomRhythia.UI;
using CustomRhythia.Util;

namespace CustomRhythia;

/// <summary>
/// Top-level orchestrator. Owns the World (Node3D) and UI (CanvasLayer)
/// subtrees and switches between screens. Also owns the persistent FPS
/// counter layer and applies FPS-limit settings globally.
/// </summary>
public partial class Main : Node
{
    public static Main Instance { get; private set; }

    public Node3D World { get; private set; }
    public CanvasLayer Ui { get; private set; }

    public static double SelectedSpeed { get; set; } = 1.0;
    public static double SelectedStartFrom { get; set; } = 0.0;

    /// <summary>Session modifier: when true, misses don't cost HP.</summary>
    public static bool NoFailEnabled { get; set; } = false;

    private Attempt _pendingAttempt;

    private CanvasLayer _fpsLayer;
    private FpsCounter _fpsCounter;

    public override void _Ready()
    {
        Instance = this;

        InitializeFolders();
        SettingsManager.Load();
        MapManager.Initialize();

        World = new Node3D { Name = "World" };
        AddChild(World);
        Ui = new CanvasLayer { Name = "UI" };
        AddChild(Ui);

        GetViewport().GuiEmbedSubwindows = true;

        _fpsLayer = new CanvasLayer { Layer = 100 };
        AddChild(_fpsLayer);

        _fpsCounter = new FpsCounter();
        _fpsCounter.SetAnchorsPreset(Control.LayoutPreset.TopLeft, keepOffsets: true);
        _fpsCounter.OffsetLeft = 12;
        _fpsCounter.OffsetTop = 8;
        _fpsCounter.OffsetRight = 160;
        _fpsCounter.OffsetBottom = 30;
        _fpsLayer.AddChild(_fpsCounter);

        SettingsManager.Loaded += ApplyPerformanceSettings;
        ApplyPerformanceSettings();

        DiscordRpc.Initialize();

        ShowMainMenu();
    }

    public override void _ExitTree()
    {
        SettingsManager.Loaded -= ApplyPerformanceSettings;
        DiscordRpc.Shutdown();
    }

    public void ApplyPerformanceSettings()
    {
        var s = SettingsManager.Current;

        if (s.LockFps)
        {
            int limit = s.FpsLimit;
            if (limit < 30) limit = 30;
            if (limit > 1000) limit = 1000;
            Engine.MaxFps = limit;
        }
        else
        {
            Engine.MaxFps = 0;
        }

        if (_fpsCounter != null && GodotObject.IsInstanceValid(_fpsCounter))
        {
            _fpsCounter.Visible = s.ShowFpsCounter;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.F11)
            {
                ToggleFullscreen();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public static void ToggleFullscreen()
    {
        var current = DisplayServer.WindowGetMode();
        if (current == DisplayServer.WindowMode.Fullscreen || current == DisplayServer.WindowMode.ExclusiveFullscreen)
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        }
        else
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
        }
    }

    private static void InitializeFolders()
    {
        Directory.CreateDirectory(Constants.UserFolder);
        Directory.CreateDirectory(Constants.MapsFolder);
        Directory.CreateDirectory(Constants.PbsFolder);
        Directory.CreateDirectory(Constants.ProfilesFolder);
        Directory.CreateDirectory(Constants.ReplaysFolder);
        Directory.CreateDirectory(Constants.SoundsFolder);
        Directory.CreateDirectory(Constants.ColorsetsFolder);
        Directory.CreateDirectory(Constants.CursorsFolder);

        SeedExampleColorset("default", "ffffff,4a9eff");
        SeedExampleColorset("sunset", "ff6d33,ff3363,9f75d1");
        SeedExampleColorset("nebula", "ff64b1,ba64ec,78c5f9");
    }

    private static void SeedExampleColorset(string name, string colors)
    {
        string path = $"{Constants.ColorsetsFolder}/{name}.txt";
        if (!File.Exists(path))
        {
            File.WriteAllText(path, colors);
        }
    }

    public void ClearScreens()
    {
        GetTree().Paused = false;

        foreach (Node child in World.GetChildren()) child.QueueFree();
        foreach (Node child in Ui.GetChildren()) child.QueueFree();
    }

    public void ShowMainMenu()
    {
        ClearScreens();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DiscordRpc.SetIdle();
        Ui.AddChild(new MainMenuScreen());
    }

    /// <summary>
    /// Opens the map select screen. If a map is provided, that map is
    /// selected automatically (used to return to the played song after a
    /// run ends, instead of bouncing back to the main menu).
    /// </summary>
    public void ShowMapSelect(Map.Map preselect = null)
    {
        ClearScreens();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        DiscordRpc.SetIdle();

        var screen = new MapSelectScreen { InitialSelection = preselect };
        Ui.AddChild(screen);
    }

    public void ShowSettings()
    {
        DiscordRpc.SetIdle();
        var screen = new SettingsScreen();
        Ui.AddChild(screen);
    }

    public void StartGame(Map.Map map, double speed, double startFrom)
    {
        ClearScreens();

        DiscordRpc.SetPlaying(map.PrettyTitle, speed);

        var world = new GameWorld();
        World.AddChild(world);
        world.Initialize(map, speed, startFrom, NoFailEnabled);

        var hud = new HudScreen();
        hud.Cam = world.Camera;
        hud.Bind(world.Runner);
        Ui.AddChild(hud);

        world.GameEnded += () =>
        {
            _pendingAttempt = world.Runner.Attempt;
            CallDeferred(nameof(OnGameEnded));
        };
    }

    private void OnGameEnded()
    {
        var attempt = _pendingAttempt;
        _pendingAttempt = null;
        if (attempt == null) return;

        var entry = new ScoreEntry
        {
            Passed = attempt.FullClear,
            Score = attempt.Score,
            Accuracy = attempt.Accuracy,
            Misses = attempt.Misses,
            MaxCombo = attempt.MaxCombo,
            Pauses = attempt.Pauses,
            Hits = attempt.Hits,
            Sum = attempt.Sum,
            Progress = System.Math.Max(0, attempt.Progress),
            MapLength = attempt.Map.Length,
            Speed = attempt.Speed,
            Time = Time.GetUnixTimeFromSystem(),
            StartFrom = System.Math.Max(0, attempt.StartFrom),
        };
        entry.Grade = Leaderboard.ComputeGrade(entry);

        var lb = Leaderboard.Load(attempt.Map.Name);
        lb.Add(entry);
        lb.Save(attempt.Map.Name);

        attempt.Map.PlayCount++;
        try { attempt.Map.Save(); } catch { /* ignore */ }

        ShowResults(attempt, entry);
    }

    public void ShowResults(Attempt attempt, ScoreEntry entry)
    {
        ClearScreens();
        Input.MouseMode = Input.MouseModeEnum.Visible;

        foreach (Node n in World.GetChildren()) n.ProcessMode = ProcessModeEnum.Disabled;

        DiscordRpc.SetIdle();

        var screen = new ResultsScreen();
        screen.Bind(attempt, entry);
        Ui.AddChild(screen);
    }
}
