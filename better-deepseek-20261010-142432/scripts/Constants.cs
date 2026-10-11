using Godot;

namespace CustomRhythia;

/// <summary>
/// Gameplay constants shared across the codebase.
/// </summary>
public static class Constants
{
    public const double CURSOR_SIZE = 0.2625;
    public const double GRID_SIZE = 3.0;
    public static readonly Vector2 BOUNDS =
        new((float)(GRID_SIZE / 2 - CURSOR_SIZE / 2), (float)(GRID_SIZE / 2 - CURSOR_SIZE / 2));

    public const double HIT_BOX_SIZE = 0.07;
    public const double HIT_WINDOW = 55;    // ms
    public const int BREAK_TIME = 4000;     // ms

    public const string APP_NAME = "CustomRhythia";

    public static string UserFolder { get; private set; } = DefaultUserFolder();

    public static string MapsFolder => $"{UserFolder}/maps";
    public static string PbsFolder => $"{UserFolder}/pbs";
    public static string ProfilesFolder => $"{UserFolder}/profiles";
    public static string ReplaysFolder => $"{UserFolder}/replays";
    public static string SoundsFolder => $"{UserFolder}/sounds";
    public static string ColorsetsFolder => $"{UserFolder}/colorsets";
    public static string CursorsFolder => $"{UserFolder}/cursors";

    public static void SetUserFolder(string path) => UserFolder = path;

    public static string DefaultUserFolder()
    {
        if (OS.GetName() == "Windows")
        {
            string appdata = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.ApplicationData);
            return $"{appdata}\\{APP_NAME}";
        }
        return OS.GetUserDataDir();
    }
}
