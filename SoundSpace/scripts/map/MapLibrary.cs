using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Godot;

namespace SoundSpace;

/// <summary>
/// The map library. Scans %APPDATA%\SoundSpace\maps and imports new .sspm / .phxm files
/// into their own sub-folder (metadata.json, notes.bin, cover.png, audio.*).
/// </summary>
public static class MapLibrary
{
	public static List<MapInfo> Maps { get; } = [];

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static void LoadAll()
	{
		Maps.Clear();

		try
		{
			Directory.CreateDirectory(Constants.MapsFolder);

			foreach (string dir in Directory.GetDirectories(Constants.MapsFolder))
			{
				string metaPath = Path.Combine(dir, "metadata.json");
				if (!File.Exists(metaPath))
				{
					continue;
				}

				try
				{
					var info = JsonSerializer.Deserialize<MapInfo>(File.ReadAllText(metaPath));
					if (info == null)
					{
						continue;
					}

					if (string.IsNullOrWhiteSpace(info.Name))
					{
						info.Name = Path.GetFileName(dir);
					}

					info.ClampLoaded();
					Maps.Add(info);
				}
				catch (Exception e)
				{
					GD.PrintErr($"[SoundSpace] Skipping map {dir}: {e.Message}");
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to scan maps: {e}");
		}

		Sort();
	}

	/// <summary>Favourites first, then alphabetical.</summary>
	public static void Sort()
	{
		Maps.Sort(static (a, b) =>
		{
			if (a.Favorite != b.Favorite)
			{
				return a.Favorite ? -1 : 1;
			}

			return string.Compare(a.PrettyTitle, b.PrettyTitle, StringComparison.CurrentCultureIgnoreCase);
		});
	}

	public static void SaveMetadata(MapInfo info)
	{
		try
		{
			Directory.CreateDirectory(info.Folder);
			File.WriteAllText(info.MetadataPath, JsonSerializer.Serialize(info, JsonOptions));
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to save metadata for {info.Name}: {e}");
		}
	}

	public static void Delete(MapInfo info)
	{
		try
		{
			if (Directory.Exists(info.Folder))
			{
				Directory.Delete(info.Folder, true);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to delete {info.Name}: {e}");
		}

		Maps.Remove(info);
	}

	/// <summary>Replaces a map's cover with the given image file (png / jpg / jpeg).</summary>
	public static void SetCover(MapInfo info, string imagePath)
	{
		try
		{
			byte[] buffer = File.ReadAllBytes(imagePath);
			Godot.Image image = MapInfo.DecodeImage(buffer);

			if (image == null)
			{
				throw new Exception("Unsupported image format");
			}

			Directory.CreateDirectory(info.Folder);
			File.WriteAllBytes(info.CoverPath, image.SavePngToBuffer());

			info.HasCover = true;
			info.RefreshCover();
			SaveMetadata(info);
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to set cover for {info.Name}: {e}");
		}
	}

	public static MapInfo Import(string sourcePath)
	{
		ParsedMap parsed = MapParser.ParseFile(sourcePath);

		if (parsed.Notes.Length == 0)
		{
			throw new Exception("Map contains no notes");
		}

		string baseName = string.IsNullOrWhiteSpace(parsed.Artist)
			? parsed.Title
			: $"{parsed.Artist} - {parsed.Title}";

		if (string.IsNullOrWhiteSpace(baseName))
		{
			baseName = Path.GetFileNameWithoutExtension(sourcePath);
		}

		if (!string.IsNullOrWhiteSpace(parsed.DifficultyName) && parsed.DifficultyName != "N/A")
		{
			baseName += $" [{parsed.DifficultyName}]";
		}

		var info = new MapInfo
		{
			Name = MakeUnique(Sanitize(baseName)),
			Artist = parsed.Artist,
			Title = parsed.Title,
			Mappers = parsed.Mappers,
			Difficulty = parsed.Difficulty,
			DifficultyName = parsed.DifficultyName,
			Length = parsed.Length,
			AudioExt = parsed.AudioExt,
			HasAudio = parsed.Audio is { Length: > 0 },
			HasCover = parsed.Cover is { Length: > 0 },
			Notes = parsed.Notes,
		};

		Directory.CreateDirectory(info.Folder);

		if (info.HasCover)
		{
			// Always store a real PNG, whatever format the source cover used.
			Godot.Image image = MapInfo.DecodeImage(parsed.Cover);
			File.WriteAllBytes(info.CoverPath, image != null ? image.SavePngToBuffer() : parsed.Cover);
		}

		if (info.HasAudio)
		{
			File.WriteAllBytes(info.AudioPath, parsed.Audio);
		}

		WriteNotes(info.NotesPath, parsed.Notes);
		File.WriteAllText(info.MetadataPath, JsonSerializer.Serialize(info, JsonOptions));

		return info;
	}

	private static void WriteNotes(string path, NoteData[] notes)
	{
		using var fs = File.Create(path);
		using var bw = new BinaryWriter(fs);

		bw.Write(notes.Length);
		foreach (NoteData note in notes)
		{
			bw.Write(note.Ms);
			bw.Write(note.X);
			bw.Write(note.Y);
		}
	}

	private static string Sanitize(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		string cleaned = new(name.Where(c => !invalid.Contains(c)).ToArray());
		cleaned = cleaned.Trim().Trim('.');

		if (cleaned.Length == 0)
		{
			cleaned = "Map";
		}

		return cleaned.Length > 80 ? cleaned[..80].Trim() : cleaned;
	}

	private static string MakeUnique(string name)
	{
		string candidate = name;
		int suffix = 2;

		while (Directory.Exists(Path.Combine(Constants.MapsFolder, candidate)))
		{
			candidate = $"{name} ({suffix})";
			suffix++;
		}

		return candidate;
	}
}

/// <summary>Sanity-clamps values read from metadata.json.</summary>
internal static class MapInfoExtensions
{
	public static void ClampLoaded(this MapInfo info)
	{
		info.Difficulty = Math.Clamp(info.Difficulty, 0, Constants.Difficulties.Length - 1);

		if (string.IsNullOrWhiteSpace(info.DifficultyName))
		{
			info.DifficultyName = Constants.Difficulties[info.Difficulty];
		}

		if (info.Mappers is null || info.Mappers.Length == 0)
		{
			info.Mappers = ["N/A"];
		}

		if (string.IsNullOrWhiteSpace(info.AudioExt))
		{
			info.AudioExt = "mp3";
		}
	}
}
