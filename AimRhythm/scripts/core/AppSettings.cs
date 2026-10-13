using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace SoundSpace;

/// <summary>
/// Gameplay + appearance settings. Stored in %APPDATA%\SoundSpace\settings.json
/// </summary>
public class AppSettings
{
	// --- Gameplay (from the Rhythia client) ---

	/// <summary>Approach Rate (AR) - how fast notes come towards the playfield.</summary>
	public double ApproachRate { get; set; } = 30.0;

	/// <summary>Spawn Distance (AD) - how far away notes spawn.</summary>
	public double SpawnDistance { get; set; } = 15.0;

	/// <summary>Camera parallax amount (0..1).</summary>
	public double Parallax { get; set; } = 0.25;

	/// <summary>Cursor sensitivity for relative mouse input.</summary>
	public double Sensitivity { get; set; } = 0.5;

	/// <summary>Music volume 0..100 (menu preview and gameplay).</summary>
	public double MusicVolume { get; set; } = 50.0;

	// --- Appearance ---

	/// <summary>Half Ghost - notes fade out right before they have to be hit.</summary>
	public bool HalfGhost { get; set; }

	/// <summary>Note style: "Square" or "Thin".</summary>
	public string NoteStyle { get; set; } = "Square";

	/// <summary>Visual size of the notes.</summary>
	public double NoteSize { get; set; } = 0.8;

	/// <summary>Maximum opacity of the notes (0..1).</summary>
	public double NoteOpacity { get; set; } = 0.95;

	/// <summary>Absolute path to a custom cursor image (png / jpg / jpeg). Empty = square cursor.</summary>
	public string CustomCursor { get; set; } = "";

	/// <summary>Visual scale of the cursor.</summary>
	public double CursorScale { get; set; } = 1.0;

	/// <summary>Name of the colorset file (without .txt) inside %APPDATA%\SoundSpace\colorsets.</summary>
	public string ColorSet { get; set; } = "Default";

	[JsonIgnore]
	public double ApproachTime => ApproachRate <= 0 ? 0.5 : SpawnDistance / ApproachRate;

	public static AppSettings Current { get; private set; } = new();

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <summary>Linear 0..100 volume to decibels (used by menu and game audio alike).</summary>
	public static float VolumeToDb(double volume) => volume <= 0 ? -80f : Mathf.LinearToDb((float)(volume / 100.0));

	public void ClampAll()
	{
		ApproachRate = Math.Clamp(ApproachRate, 0.5, 100.0);
		SpawnDistance = Math.Clamp(SpawnDistance, 0.5, 100.0);
		Parallax = Math.Clamp(Parallax, 0.0, 1.0);
		Sensitivity = Math.Clamp(Sensitivity, 0.01, 2.5);
		MusicVolume = Math.Clamp(MusicVolume, 0.0, 100.0);
		NoteSize = Math.Clamp(NoteSize, 0.3, 1.5);
		NoteOpacity = Math.Clamp(NoteOpacity, 0.0, 1.0);
		CursorScale = Math.Clamp(CursorScale, 0.4, 3.0);

		if (string.IsNullOrWhiteSpace(CustomCursor))
		{
			CustomCursor = "";
		}

		if (Array.IndexOf(Constants.NoteStyles, NoteStyle) < 0)
		{
			NoteStyle = Constants.NoteStyles[0];
		}

		if (string.IsNullOrWhiteSpace(ColorSet))
		{
			ColorSet = "Default";
		}
	}

	public static void Load()
	{
		try
		{
			Directory.CreateDirectory(Constants.UserFolder);

			if (File.Exists(Constants.SettingsFile))
			{
				string text = File.ReadAllText(Constants.SettingsFile);
				var loaded = JsonSerializer.Deserialize<AppSettings>(text);

				if (loaded != null)
				{
					Current = loaded;
				}
			}

			Current.ClampAll();
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to load settings: {e}");
			Current = new AppSettings();
		}
	}

	public static void Save()
	{
		try
		{
			Directory.CreateDirectory(Constants.UserFolder);
			Current.ClampAll();
			File.WriteAllText(Constants.SettingsFile, JsonSerializer.Serialize(Current, JsonOptions));
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to save settings: {e}");
		}
	}
}
