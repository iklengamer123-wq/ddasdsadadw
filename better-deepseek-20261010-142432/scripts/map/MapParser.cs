using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomRhythia.Util;
using Godot;

namespace CustomRhythia.Map;

/// <summary>
/// Decoders for the community map formats.
/// Currently supports .sspm v1/v2 and .phxm (folder).
/// </summary>
public static class MapParser
{
    public static bool IsValidExt(string ext) =>
        ext is "sspm" or "phxm" or "txt" or "rhm";

    /// <summary>
    /// Decodes a file path into a Map. Audio / cover buffers are attached separately.
    /// </summary>
    public static Map Decode(string path)
    {
        string ext = path.GetExtension().ToLowerInvariant();
        return ext switch
        {
            "sspm" => Sspm(path),
            _ => throw new NotSupportedException($"Unsupported map format: .{ext}"),
        };
    }

    // ---------------------------------------------------------------------
    // SSPM
    // ---------------------------------------------------------------------

    private static Map Sspm(string path)
    {
        var fp = new FileParser(path);
        string sig = fp.GetString(4);
        if (sig != "SS+m") throw new InvalidDataException("Not an SSPM file.");

        ushort version = fp.GetUInt16();
        return version switch
        {
            1 => SspmV1(fp),
            2 => SspmV2(fp),
            _ => throw new InvalidDataException($"Unsupported SSPM version: {version}"),
        };
    }

    private static Map SspmV1(FileParser fp)
    {
        fp.Skip(2); // reserved
        string _id = fp.GetLine();

        string[] nameParts = fp.GetLine().Split(" - ", 2);
        string artist = "";
        string title;
        if (nameParts.Length == 1) title = nameParts[0].Trim();
        else { artist = nameParts[0].Trim(); title = nameParts[1].Trim(); }

        string[] mappers = fp.GetLine().Split(new[] { '&', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (mappers.Length == 0) mappers = new[] { "N/A" };

        uint mapLength = fp.GetUInt32();
        uint noteCount = fp.GetUInt32();

        int difficulty = fp.GetByte();

        byte[] coverBuffer = null;
        if (fp.GetByte() == 2)
        {
            int coverLength = (int)fp.GetUInt64();
            coverBuffer = fp.Get(coverLength);
        }

        byte[] audioBuffer = null;
        if (fp.GetBool())
        {
            int audioLength = (int)fp.GetUInt64();
            audioBuffer = fp.Get(audioLength);
        }

        var notes = new Note[noteCount];
        for (int i = 0; i < noteCount; i++)
        {
            int ms = (int)fp.GetUInt32();
            bool quantum = fp.GetBool();
            float x, y;
            if (quantum) { x = fp.GetFloat(); y = fp.GetFloat(); }
            else { x = fp.GetByte(); y = fp.GetByte(); }
            notes[i] = new Note(i, ms, x - 1f, -y + 1f);
        }

        SortNotes(notes);

        return new Map
        {
            Artist = artist,
            Title = title,
            Mappers = mappers,
            Difficulty = Math.Clamp(difficulty, 0, 5),
            Length = (int)mapLength,
            Notes = notes,
            AudioExt = DetectAudioExt(audioBuffer),
        }.WithBuffers(audioBuffer, coverBuffer);
    }

    private static Map SspmV2(FileParser fp)
    {
        fp.Skip(4);  // reserved
        fp.Skip(20); // hash

        uint mapLength = fp.GetUInt32();
        uint noteCount = fp.GetUInt32();
        fp.Skip(4); // marker count

        int difficulty = fp.GetByte();
        fp.Skip(2); // rating

        bool hasAudio = fp.GetBool();
        bool hasCover = fp.GetBool();
        fp.Skip(1); // 1mod

        ulong customDataOffset = fp.GetUInt64();
        ulong customDataLength = fp.GetUInt64();
        ulong audioOffset = fp.GetUInt64();
        ulong audioLength = fp.GetUInt64();
        ulong coverOffset = fp.GetUInt64();
        ulong coverLength = fp.GetUInt64();
        fp.Skip(16); // marker def offset + length
        ulong markerOffset = fp.GetUInt64();
        fp.Skip(8); // marker byte length

        int mapIdLen = fp.GetUInt16();
        fp.Skip(mapIdLen);
        int mapNameLen = fp.GetUInt16();
        string[] nameParts = fp.GetString(mapNameLen).Split(" - ", 2);
        string artist = "";
        string title;
        if (nameParts.Length == 1) title = nameParts[0].Trim();
        else { artist = nameParts[0].Trim(); title = nameParts[1].Trim(); }

        int songLen = fp.GetUInt16();
        fp.Skip(songLen);

        int mapperCount = fp.GetUInt16();
        var mappers = new List<string>();
        for (int i = 0; i < mapperCount; i++)
        {
            int mapperLen = fp.GetUInt16();
            mappers.Add(fp.GetString(mapperLen));
        }
        if (mappers.Count == 0) mappers.Add("N/A");

        string difficultyName = null;
        fp.Seek((int)customDataOffset);
        fp.Skip(2);
        if (fp.GetString(fp.GetUInt16()) == "difficulty_name")
        {
            int length = 0;
            byte typeTag = fp.GetByte();
            if (typeTag == 9) length = fp.GetUInt16();
            else if (typeTag == 11) length = (int)fp.GetUInt32();
            difficultyName = fp.GetString(length);
        }

        byte[] audioBuffer = null;
        if (hasAudio)
        {
            fp.Seek((int)audioOffset);
            audioBuffer = fp.Get((int)audioLength);
        }

        byte[] coverBuffer = null;
        if (hasCover)
        {
            fp.Seek((int)coverOffset);
            coverBuffer = fp.Get((int)coverLength);
        }

        fp.Seek((int)markerOffset);
        var notes = new Note[noteCount];
        for (int i = 0; i < noteCount; i++)
        {
            int ms = (int)fp.GetUInt32();
            fp.Skip(1); // marker type
            bool quantum = fp.GetBool();
            float x, y;
            if (quantum) { x = fp.GetFloat(); y = fp.GetFloat(); }
            else { x = fp.GetByte(); y = fp.GetByte(); }
            notes[i] = new Note(i, ms, x - 1f, -y + 1f);
        }

        SortNotes(notes);

        return new Map
        {
            Artist = artist,
            Title = title,
            Mappers = mappers.ToArray(),
            Difficulty = Math.Clamp(difficulty, 0, 5),
            DifficultyName = difficultyName ?? DefaultDiffName(difficulty),
            Length = (int)mapLength,
            Notes = notes,
            AudioExt = DetectAudioExt(audioBuffer),
        }.WithBuffers(audioBuffer, coverBuffer);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static void SortNotes(Note[] notes)
    {
        Array.Sort(notes, (a, b) => a.Millisecond.CompareTo(b.Millisecond));
        for (int i = 0; i < notes.Length; i++) notes[i].Index = i;
    }

    private static string[] DifficultyNames =
        { "N/A", "Easy", "Medium", "Hard", "Insane", "Illogical" };

    private static string DefaultDiffName(int diff) =>
        DifficultyNames[Math.Clamp(diff, 0, DifficultyNames.Length - 1)];

    public static string DetectAudioExt(byte[] buffer)
    {
        if (buffer == null || buffer.Length < 4) return "mp3";
        if (buffer[0] == 'O' && buffer[1] == 'g' && buffer[2] == 'g' && buffer[3] == 'S')
            return "ogg";
        return "mp3";
    }
}

/// <summary>
/// Helper extension so the parsers above can attach buffers without exposing them on Map.
/// </summary>
internal static class MapParserExtensions
{
    public static Map WithBuffers(this Map map, byte[] audio, byte[] cover)
    {
        // Stash buffers in a temporary side-channel that MapManager will persist after import.
        MapImportBuffer.Pending[map] = (audio, cover);
        return map;
    }
}

/// <summary>
/// Temporary holder for buffers being handed from the parser to the importer.
/// </summary>
public static class MapImportBuffer
{
    public static readonly System.Collections.Generic.Dictionary<Map, (byte[] Audio, byte[] Cover)> Pending = new();
}
