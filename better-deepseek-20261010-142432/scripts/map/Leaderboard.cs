using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomRhythia.Map;

public class ScoreEntry
{
    public bool Passed { get; set; }
    public string Grade { get; set; } = "F";
    public uint Score { get; set; }
    public double Accuracy { get; set; }
    public uint Misses { get; set; }
    public uint MaxCombo { get; set; }
    public uint Pauses { get; set; }
    public uint Hits { get; set; }
    public uint Sum { get; set; }
    public double Progress { get; set; }
    public double MapLength { get; set; }
    public double Speed { get; set; } = 1.0;
    public double Time { get; set; }
}

/// <summary>
/// Per-speed record: best attempt and last attempt at this exact speed.
/// </summary>
public class SpeedRecord
{
    public ScoreEntry Best { get; set; }
    public ScoreEntry Last { get; set; }
}

/// <summary>
/// Per-map local score list, keyed by speed.
/// </summary>
public class Leaderboard
{
    public Dictionary<string, SpeedRecord> Speeds { get; set; } = new();

    public static string SpeedKey(double speed) => speed.ToString("0.##");

    public static Leaderboard Load(string mapName)
    {
        string path = $"{Constants.PbsFolder}/{mapName}.json";
        var lb = new Leaderboard();
        if (File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<Leaderboard>(File.ReadAllText(path));
                if (loaded != null)
                {
                    lb = loaded;
                    if (lb.Speeds == null) lb.Speeds = new();
                }
            }
            catch { /* corrupted - start fresh */ }
        }
        return lb;
    }

    public SpeedRecord For(double speed)
    {
        string key = SpeedKey(speed);
        if (!Speeds.TryGetValue(key, out var rec))
        {
            rec = new SpeedRecord();
            Speeds[key] = rec;
        }
        return rec;
    }

    public void Add(ScoreEntry score)
    {
        var rec = For(score.Speed);
        rec.Last = score;

        if (rec.Best == null || score.Score > rec.Best.Score)
        {
            rec.Best = score;
        }
    }

    public void Save(string mapName)
    {
        Directory.CreateDirectory(Constants.PbsFolder);
        string path = $"{Constants.PbsFolder}/{mapName}.json";
        File.WriteAllText(path, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- Grade ----

    /// <summary>
    /// Grade thresholds follow the reference the user provided:
    ///   SS = 100%
    ///   S  = 98%
    ///   A  = 94%
    ///   B  = 90%
    ///   C  = 85%
    ///   D  = 80%
    ///   F  = failed or below 80%
    /// Uses a tiny epsilon so 99.999% from float rounding still counts as SS.
    /// </summary>
    public static string ComputeGrade(ScoreEntry e)
    {
        if (!e.Passed) return "F";
        return GradeFromAccuracy(e.Accuracy, true);
    }

    public static string GradeFromAccuracy(double accuracy, bool passed)
    {
        if (!passed) return "F";
        if (accuracy >= 100.0 - 0.001) return "SS";
        if (accuracy >= 98.0) return "S";
        if (accuracy >= 94.0) return "A";
        if (accuracy >= 90.0) return "B";
        if (accuracy >= 85.0) return "C";
        if (accuracy >= 80.0) return "D";
        return "F";
    }
}
