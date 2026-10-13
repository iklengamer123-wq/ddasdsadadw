using System.IO;
using Godot;

namespace SoundSpace;

/// <summary>
/// Global constants. Playfield values mirror the Rhythia client
/// (github.com/rhythia/client, scripts/Constants.cs) so the feel matches the original.
/// </summary>
public static class Constants
{
	// Playfield / gameplay constants (taken from Rhythia's Constants.cs)
	public const double GridSize = 3.0; // the play area is a 3x3 grid
	public const double CursorSize = 0.2625; // cursor mesh size (does not change hitboxes)
	public const double HitBoxSize = 0.07; // extra tolerance around a note cell
	public const double HitWindow = 110.0; // ms a note stays hittable after its timestamp
	public const double CameraZ = 3.75; // camera distance from the 3x3 plane (Rhythia default)
	public const double Fov = 70.0; // field of view (Rhythia default)
	public const double NoteSize = 0.8; // default visual size of a note square (1.0 = one grid cell)

	// Selectable note styles (see the note meshes in GameScene).
	public static readonly string[] NoteStyles = ["Square", "Thin"];

	public static readonly Vector2 Bounds = new(
		(float)(GridSize / 2 - CursorSize / 2),
		(float)(GridSize / 2 - CursorSize / 2)
	);

	// All user data lives in %APPDATA%\SoundSpace (config/use_custom_user_dir in project.godot)
	public static string UserFolder => OS.GetUserDataDir();
	public static string MapsFolder => Path.Combine(UserFolder, "maps");
	public static string SettingsFile => Path.Combine(UserFolder, "settings.json");

	public static readonly string[] Difficulties = ["N/A", "Easy", "Medium", "Hard", "Insane", "Illogical"];

	public static readonly Color[] DifficultyColors =
	[
		Color.FromHtml("ffffffff"),
		Color.FromHtml("77f379ff"),
		Color.FromHtml("fff832ff"),
		Color.FromHtml("e24479ff"),
		Color.FromHtml("9d6effff"),
		Color.FromHtml("0094fcff"),
	];

	// Note colours cycle with the note order (blue -> white palette).
	public static readonly Color[] NoteColors =
	[
		Color.FromHtml("e8f3ffff"),
		Color.FromHtml("a9cdffff"),
		Color.FromHtml("6aa7ffff"),
		Color.FromHtml("c9e2ffff"),
		Color.FromHtml("88bcffff"),
		Color.FromHtml("dff0ffff"),
	];
}
