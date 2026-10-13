using System;
using System.IO;
using System.Text.Json.Serialization;
using Godot;

namespace SoundSpace;

/// <summary>
/// A single hit object. X/Y are grid coordinates in [-1..1] (Y is "up").
/// Matches Rhythia's Note (x - 1, -y + 1 for SSPM; both axes - 1 for PHXMO).
/// </summary>
public struct NoteData
{
	public int Ms;
	public float X;
	public float Y;
}

/// <summary>
/// Metadata + access to a single map stored in %APPDATA%\SoundSpace\maps\&lt;Name&gt;\
/// Folder layout: metadata.json, notes.bin, cover.png, audio.mp3|ogg
/// </summary>
public class MapInfo
{
	public string Name { get; set; } = "";
	public string Artist { get; set; } = "";
	public string Title { get; set; } = "";
	public string[] Mappers { get; set; } = ["N/A"];
	public int Difficulty { get; set; }
	public string DifficultyName { get; set; } = "N/A";
	public int Length { get; set; }
	public string AudioExt { get; set; } = "mp3";
	public bool HasAudio { get; set; }
	public bool HasCover { get; set; }
	public bool Favorite { get; set; }

	[JsonIgnore] public NoteData[] Notes { get; set; } = [];
	[JsonIgnore] private Texture2D _cover;
	[JsonIgnore] private AudioStream _audio;

	[JsonIgnore] public string Folder => Path.Combine(Constants.MapsFolder, Name);
	[JsonIgnore] public string MetadataPath => Path.Combine(Folder, "metadata.json");
	[JsonIgnore] public string NotesPath => Path.Combine(Folder, "notes.bin");
	[JsonIgnore] public string CoverPath => Path.Combine(Folder, "cover.png");
	[JsonIgnore] public string AudioPath => Path.Combine(Folder, "audio." + AudioExt);

	[JsonIgnore]
	public string PrettyTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";

	public NoteData[] LoadNotes()
	{
		if (Notes.Length > 0)
		{
			return Notes;
		}

		try
		{
			if (!File.Exists(NotesPath))
			{
				return Notes = [];
			}

			using var fs = File.OpenRead(NotesPath);
			using var br = new BinaryReader(fs);

			int count = br.ReadInt32();
			var arr = new NoteData[count];

			for (int i = 0; i < count; i++)
			{
				arr[i] = new NoteData { Ms = br.ReadInt32(), X = br.ReadSingle(), Y = br.ReadSingle() };
			}

			return Notes = arr;
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to read notes for {Name}: {e}");
			return Notes = [];
		}
	}

	public Image LoadCoverImage()
	{
		if (!HasCover || !File.Exists(CoverPath))
		{
			return null;
		}

		try
		{
			return DecodeImage(File.ReadAllBytes(CoverPath));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>Decodes an image buffer, detecting the format by content (PNG / JPEG / BMP / WEBP).</summary>
	public static Image DecodeImage(byte[] buffer)
	{
		if (buffer == null || buffer.Length < 4)
		{
			return null;
		}

		var image = new Image();

		// Checked by magic bytes first, so the engine is never asked to parse the wrong format.
		if (IsPng(buffer) && image.LoadPngFromBuffer(buffer) == Error.Ok)
		{
			return image;
		}

		if (IsJpeg(buffer) && image.LoadJpgFromBuffer(buffer) == Error.Ok)
		{
			return image;
		}

		if (IsBmp(buffer) && image.LoadBmpFromBuffer(buffer) == Error.Ok)
		{
			return image;
		}

		if (IsWebp(buffer) && image.LoadWebpFromBuffer(buffer) == Error.Ok)
		{
			return image;
		}

		return null;
	}

	private static bool IsPng(byte[] b) => b.Length > 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

	private static bool IsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

	private static bool IsBmp(byte[] b) => b.Length > 2 && b[0] == 0x42 && b[1] == 0x4D;

	private static bool IsWebp(byte[] b) =>
		b.Length > 12
		&& b[0] == 0x52
		&& b[1] == 0x49
		&& b[2] == 0x46
		&& b[3] == 0x46
		&& b[8] == 0x57
		&& b[9] == 0x45
		&& b[10] == 0x42
		&& b[11] == 0x50;

	public Texture2D LoadCover()
	{
		if (_cover != null)
		{
			return _cover;
		}

		Image img = LoadCoverImage();
		if (img == null)
		{
			return null;
		}

		_cover = ImageTexture.CreateFromImage(img);
		return _cover;
	}

	public AudioStream LoadAudio()
	{
		if (_audio != null)
		{
			return _audio;
		}

		if (!HasAudio || !File.Exists(AudioPath))
		{
			return null;
		}

		try
		{
			byte[] buffer = File.ReadAllBytes(AudioPath);
			bool ogg = buffer.Length > 4 && buffer[0] == 'O' && buffer[1] == 'g' && buffer[2] == 'g' && buffer[3] == 'S';

			_audio = ogg ? AudioStreamOggVorbis.LoadFromBuffer(buffer) : new AudioStreamMP3 { Data = buffer };
			return _audio;
		}
		catch (Exception e)
		{
			GD.PrintErr($"[SoundSpace] Failed to load audio for {Name}: {e}");
			return null;
		}
	}
}
