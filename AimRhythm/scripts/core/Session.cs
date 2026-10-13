namespace SoundSpace;

/// <summary>Data passed from the menu into the gameplay scene.</summary>
public static class Session
{
	public static MapInfo SelectedMap;

	/// <summary>Playback speed (Rhythia "rate").</summary>
	public static double Speed = 1.0;

	/// <summary>Millisecond the map should start from (0 = from the beginning).</summary>
	public static double StartFrom = 0;
}
