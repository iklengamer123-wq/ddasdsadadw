using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SoundSpace;

/// <summary>Result of parsing a source map file (SSPM / PHXM).</summary>
public class ParsedMap
{
    public string Artist = "";
    public string Title = "";
    public string[] Mappers = ["N/A"];
    public int Difficulty;
    public string DifficultyName = "N/A";
    public int Length;
    public NoteData[] Notes = [];
    public byte[] Cover;
    public byte[] Audio;
    public string AudioExt = "mp3";
}

/// <summary>
/// Port of Rhythia's MapParser for the two supported input formats:
///  - .sspm (Sound Space Plus, v1 and v2)  <- required
///  - .phxm (Phira map archive, zip)       <- the native Rhythia format
/// </summary>
public static class MapParser
{
    public static ParsedMap ParseFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');

        return ext switch
        {
            "sspm" => ParseSspm(File.ReadAllBytes(path)),
            "phxm" => ParsePhxm(path),
            _ => throw new Exception($"Unsupported map format: .{ext}"),
        };
    }

    #region SSPM

    private static ParsedMap ParseSspm(byte[] data)
    {
        var r = new Reader(data);

        if (r.GetString(4) != "SS+m")
        {
            throw new Exception("Invalid SSPM signature");
        }

        ushort version = r.U16();

        return version switch
        {
            1 => ParseSspmV1(r),
            2 => ParseSspmV2(r),
            _ => throw new Exception($"Unsupported SSPM version: {version}"),
        };
    }

    private static ParsedMap ParseSspmV1(Reader r)
    {
        r.Skip(2); // reserved
        r.GetLine(); // id
        (string artist, string title) = SplitName(r.GetLine());
        string[] mappers = r.GetLine().Split(['&', ','], StringSplitOptions.RemoveEmptyEntries);

        uint mapLength = r.U32();
        uint noteCount = r.U32();
        int difficulty = r.U8();

        bool hasCover = r.U8() == 2;
        byte[] cover = null;
        if (hasCover)
        {
            cover = r.Get((int)r.U64());
        }

        bool hasAudio = r.Bool();
        byte[] audio = null;
        if (hasAudio)
        {
            audio = r.Get((int)r.U64());
        }

        NoteData[] notes = ReadSspmNotes(r, noteCount, false);

        return Build(artist, title, mappers, difficulty, null, mapLength, notes, cover, audio);
    }

    private static ParsedMap ParseSspmV2(Reader r)
    {
        r.Skip(4); // reserved
        r.Skip(20); // hash

        uint mapLength = r.U32();
        uint noteCount = r.U32();

        r.Skip(4); // marker count

        int difficulty = r.U8();

        r.Skip(2); // map rating

        bool hasAudio = r.Bool();
        bool hasCover = r.Bool();

        r.Skip(1); // 1mod

        ulong customDataOffset = r.U64();
        r.U64(); // custom data length

        ulong audioOffset = r.U64();
        ulong audioLength = r.U64();

        ulong coverOffset = r.U64();
        ulong coverLength = r.U64();

        r.Skip(16); // marker definitions offset & length

        ulong markerOffset = r.U64();

        r.Skip(8); // marker byte length

        r.Skip(r.U16()); // map id

        (string artist, string title) = SplitName(r.GetString(r.U16()));

        r.Skip(r.U16()); // song name

        int mapperCount = r.U16();
        var mappers = new string[mapperCount];
        for (int i = 0; i < mapperCount; i++)
        {
            mappers[i] = r.GetString(r.U16());
        }

        string difficultyName = ReadDifficultyName(r, customDataOffset);

        byte[] audio = null;
        if (hasAudio)
        {
            r.Seek((int)audioOffset);
            audio = r.Get((int)audioLength);
        }

        byte[] cover = null;
        if (hasCover)
        {
            r.Seek((int)coverOffset);
            cover = r.Get((int)coverLength);
        }

        r.Seek((int)markerOffset);
        NoteData[] notes = ReadSspmNotes(r, noteCount, true);

        return Build(artist, title, mappers, difficulty, difficultyName, mapLength, notes, cover, audio);
    }

    private static NoteData[] ReadSspmNotes(Reader r, uint count, bool v2)
    {
        var notes = new NoteData[count];

        for (int i = 0; i < count; i++)
        {
            uint ms = r.U32();

            if (v2)
            {
                r.Skip(1); // marker type
            }

            bool quantum = r.Bool();

            float x,
                y;

            if (quantum)
            {
                x = r.F32();
                y = r.F32();
            }
            else
            {
                x = r.U8();
                y = r.U8();
            }

            // SSPM stores Y top-down; Rhythia converts with (-y + 1).
            notes[i] = new NoteData { Ms = (int)ms, X = x - 1f, Y = 1f - y };
        }

        Array.Sort(notes, static (a, b) => a.Ms.CompareTo(b.Ms));
        return notes;
    }

    private static string ReadDifficultyName(Reader r, ulong customDataOffset)
    {
        try
        {
            r.Seek((int)customDataOffset);
            r.Skip(2); // field count

            if (r.GetString(r.U16()) != "difficulty_name")
            {
                return null;
            }

            byte type = r.U8();
            int length = type switch
            {
                9 => r.U16(),
                11 => (int)r.U32(),
                _ => 0,
            };

            return length > 0 ? r.GetString(length) : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region PHXM

    private static ParsedMap ParsePhxm(string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);

        using JsonDocument doc = JsonDocument.Parse(ReadZipString(zip, "metadata.json"));
        JsonElement root = doc.RootElement;

        byte[] objects = ReadZipBytes(zip, "objects.phxmo");
        NoteData[] notes = DecodePhxmo(objects);

        bool hasAudio = GetBool(root, "HasAudio");
        bool hasCover = GetBool(root, "HasCover");
        string audioExt = GetString(root, "AudioExt");
        if (string.IsNullOrEmpty(audioExt))
        {
            audioExt = "mp3";
        }

        var parsed = new ParsedMap
        {
            Artist = GetString(root, "Artist"),
            Title = GetString(root, "Title"),
            Mappers = GetStringArray(root, "Mappers"),
            Difficulty = Math.Clamp(GetInt(root, "Difficulty"), 0, Constants.Difficulties.Length - 1),
            DifficultyName = GetString(root, "DifficultyName"),
            Length = GetInt(root, "Length"),
            Notes = notes,
            Audio = hasAudio ? ReadZipBytes(zip, "audio." + audioExt) : null,
            Cover = hasCover ? ReadZipBytes(zip, "cover.png") : null,
            AudioExt = audioExt,
        };

        if (string.IsNullOrEmpty(parsed.DifficultyName))
        {
            parsed.DifficultyName = Constants.Difficulties[parsed.Difficulty];
        }

        if (parsed.Mappers.Length == 0)
        {
            parsed.Mappers = ["N/A"];
        }

        if (parsed.Length <= 0 && notes.Length > 0)
        {
            parsed.Length = notes[^1].Ms;
        }

        return parsed;
    }

    private static NoteData[] DecodePhxmo(byte[] buffer)
    {
        var r = new Reader(buffer);
        r.U32(); // version

        uint count = r.U32();
        var notes = new NoteData[count];

        for (int i = 0; i < count; i++)
        {
            uint ms = r.U32();
            bool quantum = r.Bool();

            float x,
                y;

            if (quantum)
            {
                x = r.F32();
                y = r.F32();
            }
            else
            {
                // PHXMO stores both axes shifted by +1 (no Y flip).
                x = r.U8() - 1;
                y = r.U8() - 1;
            }

            notes[i] = new NoteData { Ms = (int)ms, X = x, Y = y };
        }

        return notes;
    }

    private static byte[] ReadZipBytes(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new Exception($"Missing archive entry: {name}");

        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string ReadZipString(ZipArchive zip, string name) => Encoding.UTF8.GetString(ReadZipBytes(zip, name));

    private static bool GetBool(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement e) && e.ValueKind == JsonValueKind.True;

    private static int GetInt(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement e) && e.TryGetInt32(out int v) ? v : 0;

    private static string GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out JsonElement e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";

    private static string[] GetStringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out JsonElement e) || e.ValueKind != JsonValueKind.Array)
        {
            return ["N/A"];
        }

        var list = new List<string>();
        foreach (JsonElement item in e.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString() ?? "");
            }
        }

        return list.Count > 0 ? list.ToArray() : ["N/A"];
    }

    #endregion

    #region Shared

    private static ParsedMap Build(
        string artist,
        string title,
        string[] mappers,
        int difficulty,
        string difficultyName,
        uint mapLength,
        NoteData[] notes,
        byte[] cover,
        byte[] audio
    )
    {
        int clampedDifficulty = Math.Clamp(difficulty, 0, Constants.Difficulties.Length - 1);

        return new ParsedMap
        {
            Artist = artist ?? "",
            Title = title ?? "",
            Mappers = mappers.Length > 0 ? mappers : ["N/A"],
            Difficulty = clampedDifficulty,
            DifficultyName = difficultyName ?? Constants.Difficulties[clampedDifficulty],
            Length = mapLength > 0 ? (int)mapLength : (notes.Length > 0 ? notes[^1].Ms : 0),
            Notes = notes,
            Cover = cover,
            Audio = audio,
            AudioExt = IsOgg(audio) ? "ogg" : "mp3",
        };
    }

    private static bool IsOgg(byte[] buffer) =>
        buffer != null && buffer.Length >= 4 && buffer[0] == 'O' && buffer[1] == 'g' && buffer[2] == 'g' && buffer[3] == 'S';

    private static (string Artist, string Title) SplitName(string line)
    {
        line ??= "";
        string[] parts = line.Split([" - "], 2, StringSplitOptions.None);

        if (parts.Length == 1)
        {
            return ("", parts[0].Trim());
        }

        return (parts[0].Trim(), parts[1].Trim());
    }

    /// <summary>Minimal little-endian binary reader (port of Rhythia's FileParser).</summary>
    private sealed class Reader(byte[] buffer)
    {
        private readonly byte[] _b = buffer;
        private int _p;

        public void Skip(int n) => _p += n;

        public void Seek(int p) => _p = p;

        public byte[] Get(int n)
        {
            var result = new byte[n];
            Array.Copy(_b, _p, result, 0, n);
            _p += n;
            return result;
        }

        public string GetString(int n)
        {
            string s = Encoding.UTF8.GetString(_b, _p, n);
            _p += n;
            return s;
        }

        public string GetLine()
        {
            int start = _p;
            while (_p < _b.Length && _b[_p] != (byte)'\n')
            {
                _p++;
            }

            int length = _p - start;
            if (_p < _b.Length)
            {
                _p++;
            }

            return Encoding.UTF8.GetString(_b, start, length).TrimEnd('\r');
        }

        public byte U8() => _b[_p++];

        public ushort U16()
        {
            ushort v = BitConverter.ToUInt16(_b, _p);
            _p += 2;
            return v;
        }

        public uint U32()
        {
            uint v = BitConverter.ToUInt32(_b, _p);
            _p += 4;
            return v;
        }

        public ulong U64()
        {
            ulong v = BitConverter.ToUInt64(_b, _p);
            _p += 8;
            return v;
        }

        public float F32()
        {
            float v = BitConverter.ToSingle(_b, _p);
            _p += 4;
            return v;
        }

        public bool Bool() => _b[_p++] != 0;
    }

    #endregion
}
