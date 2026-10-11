using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomRhythia;

/// <summary>
/// Serializable settings. Persisted as JSON in the profiles folder.
/// </summary>
public class GameSettings
{
    // Gameplay
    public double Sensitivity { get; set; } = 1.0;
    public double ApproachRate { get; set; } = 30;
    public double ApproachDistance { get; set; } = 15;
    public double FadeIn { get; set; } = 10;
    public double FadeOut { get; set; } = 75;
    public bool Pushback { get; set; } = true;
    public double CameraParallax { get; set; } = 0.25;
    public double FoV { get; set; } = 70;

    // Visual
    public double NoteSize { get; set; } = 0.90;
    public double NoteOutlineThickness { get; set; } = 0.10;
    public double NoteOpacity { get; set; } = 1.0;
    public string NoteColorset { get; set; } = "";

    public string CursorFile { get; set; } = "";
    public double CursorScale { get; set; } = 1.0;
    public double CursorOpacity { get; set; } = 1.0;
    public double CursorRotation { get; set; } = 0.0;

    public bool GridGuides { get; set; } = true;
    public bool SimpleHud { get; set; } = false;
    public bool HitPopups { get; set; } = true;

    // Performance
    public bool ShowFpsCounter { get; set; } = true;

    /// <summary>
    /// Default true: an uncapped framerate on a 144+ Hz display pegs the CPU
    /// and causes stutter. 240 is a comfortable default; raise it if you
    /// have headroom.
    /// </summary>
    public bool LockFps { get; set; } = true;
    public int FpsLimit { get; set; } = 240;

    // Audio
    public double VolumeMusic { get; set; } = 1.0;
    public double VolumeHitSound { get; set; } = 1.0;
    public string HitSoundFile { get; set; } = "";

    // Other
    public bool RecordReplays { get; set; } = true;

    public double ApproachTime => ApproachDistance / ApproachRate;
}

public static class SettingsManager
{
    public static GameSettings Current { get; private set; } = new();
    public static string CurrentProfile { get; private set; } = "default";

    public static event Action Loaded;

    private static string ProfilePath(string profile) =>
        $"{Constants.ProfilesFolder}/{profile}.json";

    public static void Load(string profile = null)
    {
        profile ??= CurrentProfile;
        CurrentProfile = profile;

        Directory.CreateDirectory(Constants.ProfilesFolder);
        string path = ProfilePath(profile);

        if (!File.Exists(path))
        {
            Current = new GameSettings();
            Save();
        }
        else
        {
            try
            {
                string json = File.ReadAllText(path);
                Current = JsonSerializer.Deserialize<GameSettings>(json, JsonOpts) ?? new GameSettings();
            }
            catch (Exception ex)
            {
                Godot.GD.PushError($"Settings load failed: {ex.Message}");
                Current = new GameSettings();
            }
        }

        Loaded?.Invoke();
    }

    public static void Save(string profile = null)
    {
        profile ??= CurrentProfile;
        Directory.CreateDirectory(Constants.ProfilesFolder);
        string json = JsonSerializer.Serialize(Current, JsonOpts);
        File.WriteAllText(ProfilePath(profile), json);
    }

    public static void Reset()
    {
        Current = new GameSettings();
        Save();
        Loaded?.Invoke();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}
