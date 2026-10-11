using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace CustomRhythia.Map;

/// <summary>
/// Owns the list of imported maps and handles import + delete.
/// Fixed ordering: favorites first, then newest-first by AddedTime.
/// No user-facing sort options.
/// </summary>
public static class MapManager
{
    public static List<Map> Maps { get; private set; } = new();
    public static event Action MapsChanged;

    private static readonly string[] ValidExtensions = { "sspm", "phxm" };

    public static void Initialize()
    {
        Directory.CreateDirectory(Constants.MapsFolder);
        Directory.CreateDirectory(Constants.PbsFolder);
        Reload();
    }

    public static void Reload()
    {
        Maps.Clear();
        if (Directory.Exists(Constants.MapsFolder))
        {
            foreach (string dir in Directory.GetDirectories(Constants.MapsFolder))
            {
                try
                {
                    var map = Map.LoadFromFolder(dir);
                    if (map.Notes.Length > 0) Maps.Add(map);
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"Failed to load map {dir}: {ex.Message}");
                }
            }
        }

        SortFixed();
        MapsChanged?.Invoke();
    }

    /// <summary>
    /// Favorites first, then newest-first by AddedTime. Ties broken by name.
    /// </summary>
    public static void SortFixed()
    {
        Maps.Sort((a, b) =>
        {
            if (a.Favorite != b.Favorite) return a.Favorite ? -1 : 1;

            int cmp = b.AddedTime.CompareTo(a.AddedTime); // newest first
            if (cmp != 0) return cmp;

            return string.Compare(a.PrettyTitle, b.PrettyTitle, StringComparison.OrdinalIgnoreCase);
        });
    }

    public static async Task<int> ImportAsync(string[] files)
    {
        if (files == null || files.Length == 0) return 0;
        int imported = 0;

        await Task.Run(() =>
        {
            foreach (string file in files)
            {
                try
                {
                    string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    if (!ValidExtensions.Contains(ext)) continue;

                    Map map = MapParser.Decode(file);
                    Persist(map, file);
                    imported++;
                }
                catch (Exception ex)
                {
                    GD.PushError($"Import failed for {file}: {ex.Message}");
                }
            }
        });

        Reload();
        return imported;
    }

    /// <summary>
    /// Removes the map folder from disk and drops it from the in-memory list.
    /// </summary>
    public static void Delete(Map map)
    {
        if (map == null) return;

        try
        {
            if (Directory.Exists(map.FolderPath))
            {
                Directory.Delete(map.FolderPath, true);
            }
        }
        catch (Exception ex)
        {
            GD.PushError($"Failed to delete map folder: {ex.Message}");
            return;
        }

        try
        {
            string pbPath = $"{Constants.PbsFolder}/{map.Name}.json";
            if (File.Exists(pbPath)) File.Delete(pbPath);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Failed to delete leaderboard: {ex.Message}");
        }

        Maps.RemoveAll(m => m.FolderPath == map.FolderPath);
        SortFixed();
        MapsChanged?.Invoke();
    }

    /// <summary>
    /// Toggles Favorite and persists it. Re-sorts and notifies listeners.
    /// </summary>
    public static void ToggleFavorite(Map map)
    {
        if (map == null) return;
        map.Favorite = !map.Favorite;
        try { map.Save(); } catch (Exception ex) { GD.PushError(ex.Message); }

        SortFixed();
        MapsChanged?.Invoke();
    }

    private static void Persist(Map map, string sourceFile)
    {
        string safeName = SanitizeFileName(string.IsNullOrEmpty(map.Title) ? "map" : map.Title);
        string folder = $"{Constants.MapsFolder}/{safeName}";
        int suffix = 1;
        while (Directory.Exists(folder))
        {
            folder = $"{Constants.MapsFolder}/{safeName}_{suffix++}";
        }

        Directory.CreateDirectory(folder);
        map.FolderPath = folder;
        map.Name = Path.GetFileName(folder);
        map.AddedTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (MapImportBuffer.Pending.TryGetValue(map, out var buffers))
        {
            if (buffers.Audio != null && buffers.Audio.Length > 0)
            {
                File.WriteAllBytes($"{folder}/audio.{map.AudioExt}", buffers.Audio);
            }
            if (buffers.Cover != null && buffers.Cover.Length > 0)
            {
                File.WriteAllBytes($"{folder}/cover.png", buffers.Cover);
            }
            MapImportBuffer.Pending.Remove(map);
        }

        map.Save();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 64 ? name[..64] : name;
    }
}
