using CustomRhythia.Map;

namespace CustomRhythia.Game;

/// <summary>
/// State for one gameplay attempt at a map.
/// </summary>
public class Attempt
{
    public Map.Map Map;
    public double Speed = 1.0;
    public double StartFrom = 0;

    public double Progress;
    public double Length;
    public bool Playing = true;
    public bool Alive = true;
    public bool Qualifies = true;
    public bool Stopped = false;

    // Health: 5 HP max, hit = +1, miss = -1, fail at 0.
    public const double MaxHealth = 5;
    public double Health = MaxHealth;

    public uint Hits = 0;
    public uint Misses = 0;
    public uint Sum = 0;
    public uint Score = 0;
    public uint Combo = 0;
    public uint MaxCombo = 0;
    public uint Pauses = 0;
    public uint ComboMultiplier = 1;
    public uint ComboMultiplierProgress = 0;
    public uint ComboMultiplierIncrement = 40;

    public double Accuracy => Sum == 0 ? 100.0 : (double)Hits / Sum * 100.0;

    public bool FullClear => Alive && Progress >= Map.Length - 100;

    public Attempt(Map.Map map, double speed, double startFrom)
    {
        Map = map;
        Speed = speed;
        StartFrom = startFrom;
        Length = System.Math.Max(map.Length + 2000, 1000);

        // 1 second lead-in before the chosen start position.
        Progress = startFrom - 1000;

        ComboMultiplierIncrement = (uint)System.Math.Max(4, map.Notes.Length / 200);
    }
}
