# CustomRhythia

A lightweight custom rhythm game client built on **Godot 4.7 (.NET)** and **C#**.
Inspired by *Sound Space* / *Rhythia* — you move your cursor across a 3×3 grid and hit notes as they fly toward you.

This is a **from-scratch** project that only borrows concepts and the `.sspm` map format from [Rhythia](https://github.com/Rhythia/Client).

---

## Requirements

| Tool | Version |
|---|---|
| [Godot Engine](https://godotengine.org/download) | **4.7** (.NET / C# build) |
| [.NET SDK](https://dotnet.microsoft.com/download) | **10.0** |

---

## Build & Run

1. Clone or extract this folder.
2. Open **Godot 4.7 (.NET)**.
3. Click **Import** → select `project.godot`.
4. Press **F5** to run.

You can also build from the command line:

```bash
dotnet build
```

---

## Project structure

```
scripts/
├── Main.cs                 # Bootstrap, folder init, screen switching
├── Constants.cs            # Gameplay constants
├── SettingsManager.cs      # JSON settings
├── util/
│   ├── FileParser.cs       # Binary reader
│   └── Strings.cs          # Time formatting helpers
├── map/
│   ├── Map.cs              # Map data model
│   ├── Note.cs             # Hit object
│   ├── MapParser.cs        # .sspm v1/v2 and .phxmo / .phxm folder decoders
│   ├── MapManager.cs       # Map index + import
│   └── Leaderboard.cs      # Local per-map scores
├── game/
│   ├── Attempt.cs
│   ├── Runner.cs           # Frame loop, hit detection, scoring
│   ├── NoteRenderer.cs     # MultiMesh renderer
│   ├── CursorController.cs
│   └── GameWorld.cs        # 3D scene root
└── ui/
    ├── MainMenuScreen.cs
    ├── MapSelectScreen.cs
    ├── HudScreen.cs
    ├── ResultsScreen.cs
    └── SettingsScreen.cs

scenes/
└── main.tscn               # Entry scene
```

---

## Where does my data live?

Everything is stored in:

- **Windows:** `%APPDATA%\CustomRhythia\`
- **Linux/macOS:** Godot's user data dir

Subfolders:

- `maps/` — imported maps, one folder per map (`metadata.json`, `objects.phxmo`, `audio.*`, `cover.png`)
- `pbs/` — per-map local leaderboards (`.json`)
- `profiles/` — settings profiles (`.json`)

---

## Importing maps

Maps use the community `.sspm` format (Sound Space Plus Map).

1. Launch the game.
2. **Play → Import** → select one or more `.sspm` files.
3. Pick a map from the list and hit **Play**.

You can also drop `.sspm` files onto the window.

---

## Controls

| Key ↕▾ | Action ↕▾ |
|---|---|
| −Mouse | Move cursor |
| −`Escape` | Pause / back |
| −`Space` | Skip section / resume |
| −`Ctrl+M` | Mute |
| −`Ctrl+O` | Settings |
⚙

---

## License

For educational / personal use. If you redistribute, respect the original **Rhythia** project's AGPL-3.0 license for any code you reuse from it.

</BDS:create_file>

<BDS:create_file fileName="scenes/main.tscn">

[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://scripts/Main.cs" id="1_main"]

[node name="Main" type="Node"]
script = ExtResource("1_main")

</BDS:create_file>

<BDS:create_file fileName="scripts/Constants.cs">

```csharp
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

