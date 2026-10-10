using System;
using System.IO;
using Godot;
using CustomRhythia.Game;
using CustomRhythia.Map;
using CustomRhythia.UI;

namespace CustomRhythia;

/// <summary>
/// Top-level orchestrator. Owns the World (Node3D) and UI (CanvasLayer)
/// subtrees and switches between screens.
/// </summary>
public partial class Main : Node
{
    public static Main Instance { get; private set; }

    public Node3D World { get; private set; }
    public CanvasLayer Ui { get; private set; }

    public static double SelectedSpeed { get; set; } = 1.0;
    public static double SelectedStartFrom { get; set; } = 0.0;

    private Attempt _pendingAttempt;

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

        ShowMainMenu();
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

        // Seed a couple of example colorsets on first run so the user has
        // something to look at and a template to copy.
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
        Ui.AddChild(new MainMenuScreen());
    }

    public void ShowMapSelect()
    {
        ClearScreens();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Ui.AddChild(new MapSelectScreen());
    }

    public void ShowSettings()
    {
        var screen = new SettingsScreen();
        Ui.AddChild(screen);
    }

    public void StartGame(Map.Map map, double speed, double startFrom)
    {
        ClearScreens();

        var world = new GameWorld();
        World.AddChild(world);
        world.Initialize(map, speed, startFrom);

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

        var screen = new ResultsScreen();
        screen.Bind(attempt, entry);
        Ui.AddChild(screen);
    }
}
