using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace SoundSpace;

/// <summary>
/// Colorsets for the notes. Stored as .txt files in %APPDATA%\SoundSpace\colorsets.
/// Every line of a file is one RGB colour ("255,128,0" or "#RRGGBB"); empty lines and
/// lines starting with "//" are ignored.
/// </summary>
public static class ColorSets
{
	public static string Folder => Path.Combine(Constants.UserFolder, "colorsets");

	// Presets are created on first run (missing files only).
	private static readonly (string Name, string[] Lines)[] Presets =
	[
		("Default", [
			"232,243,255",
			"169,205,255",
			"106,167,255",
			"201,226,255",
			"136,188,255",
			"223,240,255",
		]),
		("Killing Freeze", [
			"#b0f396",
			"#d8f8c9",
			"#abf09f",
			"#d1f5ca",
			"#a0ebb1",
			"#c1edcc",
			"#90e3cb",
			"#b5e6d6",
			"#80d7e8",
			"#b5e2eb",
			"#79c1f9",
			"#bcdff8",
			"#7e9ffd",
			"#cbd7f7",
			"#8a8bff",
			"#cacaf8",
			"#9e90ff",
			"#bfb7fc",
			"#c792f8",
			"#e0cdf4",
			"#ff93eb",
			"#f7d7f0",
		]),
		("Rainbow Freeze", [
			"#ffdde6",
			"#ff8282",
			"#ffe6dd",
			"#ffc082",
			"#fff6dd",
			"#ffff82",
			"#f6ffdd",
			"#c0ff82",
			"#e6ffdd",
			"#82ff82",
			"#ddffe6",
			"#82ffc0",
			"#e9fff6",
			"#82ffff",
			"#ddf6ff",
			"#82c0ff",
			"#dde7ff",
			"#8282ff",
			"#e6ddff",
			"#c082ff",
			"#f6ddff",
			"#ff82ff",
			"#ffddf6",
			"#ff82c0",
		]),
	];

	// Presets that were shipped earlier and are no longer wanted.
	private static readonly string[] RemovedPresets = ["Violet", "Sunset"];

	/// <summary>Creates the colorsets folder and the bundled presets if they are missing.</summary>
	public static void EnsurePresets()
	{
		try
		{
			Directory.CreateDirectory(Folder);

			foreach (string removed in RemovedPresets)
			{
				string path = Path.Combine(Folder, removed + ".txt");

				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}

			foreach ((string name, string[] lines) in Presets)
			{
				string path = Path.Combine(Folder, name + ".txt");

				if (!File.Exists(path))
				{
					File.WriteAllLines(path, lines);
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to create colorsets: {e}");
		}
	}

	/// <summary>All colorset names (file names without extension), sorted.</summary>
	public static List<string> GetNames()
	{
		try
		{
			Directory.CreateDirectory(Folder);

			return Directory
				.GetFiles(Folder, "*.txt")
				.Select(Path.GetFileNameWithoutExtension)
				.Where(n => !string.IsNullOrWhiteSpace(n))
				.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to list colorsets: {e}");
			return [];
		}
	}

	/// <summary>Loads a colorset by name; falls back to the built-in palette.</summary>
	public static Color[] Load(string name)
	{
		try
		{
			string path = Path.Combine(Folder, name + ".txt");

			if (!File.Exists(path))
			{
				return Constants.NoteColors;
			}

			var colors = new List<Color>();

			foreach (string line in File.ReadAllLines(path))
			{
				Color? color = ParseLine(line);
				if (color.HasValue)
				{
					colors.Add(color.Value);
				}
			}

			return colors.Count > 0 ? colors.ToArray() : Constants.NoteColors;
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to load colorset {name}: {e}");
			return Constants.NoteColors;
		}
	}

	private static Color? ParseLine(string line)
	{
		line = line.Trim();

		if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
		{
			return null;
		}

		// "#RRGGBB" / "#RGB" / "#RRGGBBAA"
		if (line.StartsWith('#'))
		{
			string hex = line[1..];

			bool validHex = hex.Length is 3 or 6 or 8 && hex.All(Uri.IsHexDigit);

			if (!validHex)
			{
				return null;
			}

			if (hex.Length == 3)
			{
				hex = string.Concat(hex.Select(c => new string(c, 2)));
			}

			float r = Convert.ToInt32(hex[..2], 16) / 255f;
			float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
			float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
			float a = hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) / 255f : 1f;

			return new Color(r, g, b, a);
		}

		string[] parts = line.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

		if (parts.Length < 3)
		{
			return null;
		}

		if (
			!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float red)
			|| !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float green)
			|| !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float blue)
		)
		{
			return null;
		}

		float alpha = 1f;
		if (parts.Length >= 4 && float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float a2))
		{
			alpha = a2;
		}

		return new Color(red / 255f, green / 255f, blue / 255f, alpha);
	}
}
