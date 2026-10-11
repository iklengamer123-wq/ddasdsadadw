using System;
using System.IO;
using System.Text.Json;
using CustomRhythia.Util;

namespace CustomRhythia.Map;

/// <summary>
/// Parsed map data. Notes are always sorted by time and re-indexed.
/// </summary>
public class Map
{
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public string DifficultyName { get; set; } = "N/A";
    public int Difficulty { get; set; } = 0;
    public string[] Mappers { get; set; } = { "N/A" };
    public int Length { get; set; } = 0;
    public string AudioExt { get; set; } = "mp3";
    public bool Favorite { get; set; } = false;
    public int PlayCount { get; set; } = 0;

    /// <summary>
    /// Unix seconds at which this map was imported into the library.
    /// 0 means "no recorded import time" (older maps) — these are treated
    /// as the oldest and appear at the bottom of the fixed sort order.
    /// </summary>
    public long AddedTime { get; set; } = 0;

    public Note[] Notes { get; set; } = Array.Empty<Note>();

    public string FolderPath { get; set; } = "";

    public string PrettyTitle => string.IsNullOrEmpty(Artist) ? Title : $"{Artist} - {Title}";
    public string PrettyMappers => string.Join(", ", Mappers);

    public string AudioPath => $"{FolderPath}/audio.{AudioExt}";
    public string CoverPath => $"{FolderPath}/cover.png";
    public string MetadataPath => $"{FolderPath}/metadata.json";
    public string ObjectsPath => $"{FolderPath}/objects.phxmo";

    public bool HasAudio => File.Exists(AudioPath);
    public bool HasCover => File.Exists(CoverPath);

    public static Note[] ReadObjects(string path)
    {
        if (!File.Exists(path)) return Array.Empty<Note>();
        var fp = new FileParser(path);
        return DecodePHXMO(fp);
    }

    public static Note[] DecodePHXMO(FileParser fp)
    {
        _ = fp.GetUInt32();
        uint count = fp.GetUInt32();
        var notes = new Note[count];
        for (int i = 0; i < count; i++)
        {
            int ms = (int)fp.GetUInt32();
            bool quantum = fp.GetBool();
            float x, y;
            if (quantum)
            {
                x = fp.GetFloat();
                y = fp.GetFloat();
            }
            else
            {
                x = fp.GetByte() - 1;
                y = fp.GetByte() - 1;
            }
            notes[i] = new Note(i, ms, x, y);
        }
        return notes;
    }

    public void Save()
    {
        Directory.CreateDirectory(FolderPath);

        var meta = new
        {
            Name,
            Artist,
            Title,
            DifficultyName,
            Difficulty,
            Mappers,
            Length,
            AudioExt,
            Favorite,
            PlayCount,
            AddedTime,
        };
        File.WriteAllText(MetadataPath, JsonSerializer.Serialize(meta,
            new JsonSerializerOptions { WriteIndented = true }));

        using (var stream = File.Create(ObjectsPath))
        using (var bw = new BinaryWriter(stream))
        {
            bw.Write((uint)12);
            bw.Write((uint)Notes.Length);
            foreach (var note in Notes)
            {
                bool quantum = (int)note.X != note.X || (int)note.Y != note.Y
                    || note.X < -1 || note.X > 1
                    || note.Y < -1 || note.Y > 1;
                bw.Write((uint)note.Millisecond);
                bw.Write(quantum);
                if (quantum)
                {
                    bw.Write(note.X);
                    bw.Write(note.Y);
                }
                else
                {
                    bw.Write((byte)(note.X + 1));
                    bw.Write((byte)(note.Y + 1));
                }
            }
            bw.Write(0);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            bw.Write(0); bw.Write(0); bw.Write(0);
        }
    }

    public static Map LoadFromFolder(string folder)
    {
        var map = new Map { FolderPath = folder, Name = Path.GetFileName(folder) };

        if (File.Exists($"{folder}/metadata.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText($"{folder}/metadata.json"));
                var root = doc.RootElement;

                map.Name = root.TryGetProperty("Name", out var n) ? n.GetString() : map.Name;
                map.Artist = root.TryGetProperty("Artist", out var ar) ? ar.GetString() : "";
                map.Title = root.TryGetProperty("Title", out var t) ? t.GetString() : "";
                map.DifficultyName = root.TryGetProperty("DifficultyName", out var dn) ? dn.GetString() : "N/A";
                map.Difficulty = root.TryGetProperty("Difficulty", out var d) ? d.GetInt32() : 0;
                map.Length = root.TryGetProperty("Length", out var l) ? l.GetInt32() : 0;
                map.AudioExt = root.TryGetProperty("AudioExt", out var ae) ? ae.GetString() : "mp3";
                map.Favorite = root.TryGetProperty("Favorite", out var f) && f.GetBoolean();
                map.PlayCount = root.TryGetProperty("PlayCount", out var pc) ? pc.GetInt32() : 0;

                if (root.TryGetProperty("AddedTime", out var at) && at.ValueKind == JsonValueKind.Number)
                {
                    map.AddedTime = at.GetInt64();
                }

                if (root.TryGetProperty("Mappers", out var m) && m.ValueKind == JsonValueKind.Array)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var item in m.EnumerateArray()) list.Add(item.GetString());
                    map.Mappers = list.ToArray();
                }
            }
            catch (Exception ex)
            {
                Godot.GD.PushError($"Failed to read {folder}/metadata.json: {ex.Message}");
            }
        }

        map.Notes = ReadObjects(map.ObjectsPath);
        return map;
    }
}
