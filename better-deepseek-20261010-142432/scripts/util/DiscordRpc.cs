using System;
using System.Collections.Generic;
using System.Globalization;
using DiscordRPC;
using DiscordRPC.Logging;
using Godot;

namespace CustomRhythia.Util;

/// <summary>
/// Discord Rich Presence wrapper.
///   - idle (any menu): "В главном меню" with no second line.
///   - playing:         "{map title}"      / "{speed}x" only.
/// The idle state clears Timestamps so Discord doesn't show a running timer.
/// </summary>
public static class DiscordRpc
{
    private const string AppId = "1463211937050660904";
    private const string LargeImageKey = "short";
    private const string LargeImageText = "CustomRhythia";

    public static DiscordRpcClient Client { get; private set; }
    public static bool Enabled { get; private set; }

    private static DateTime _playingStartUtc = DateTime.UtcNow;

    public static void Initialize()
    {
        if (Client != null) return;

        try
        {
            Client = new DiscordRpcClient(AppId) { Logger = new GodotLogger(LogLevel.Warning) };
            Client.Initialize();
            Enabled = true;
            GD.Print("[DiscordRPC] Initialized");
            SetIdle();
        }
        catch (Exception ex)
        {
            GD.PushWarning("Discord RPC init failed: " + ex.Message);
            Client = null;
            Enabled = false;
        }
    }

    public static void Shutdown()
    {
        try { Client?.ClearPresence(); } catch { }
        try { Client?.Dispose(); } catch { }
        Client = null;
        Enabled = false;
    }

    /// <summary>
    /// Called from main menu, settings, map select, and results.
    /// Only the top line — no subtitle, no timer.
    /// </summary>
    public static void SetIdle()
    {
        if (!Enabled || Client == null) return;

        try
        {
            var presence = new RichPresence
            {
                Details = "В главном меню",
                State = null,
                Timestamps = null,
                Assets = new Assets
                {
                    LargeImageKey = LargeImageKey,
                    LargeImageText = LargeImageText,
                },
            };
            Client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            GD.PushWarning("Discord RPC update failed: " + ex.Message);
        }
    }

    /// <summary>
    /// Called when the player starts a map. Top line is the map title,
    /// second line is only the speed modifier (e.g. "1x").
    /// Timer starts counting since this call.
    /// </summary>
    public static void SetPlaying(string mapTitle, double speed)
    {
        if (!Enabled || Client == null) return;

        _playingStartUtc = DateTime.UtcNow;

        try
        {
            var presence = new RichPresence
            {
                Details = Truncate(mapTitle, 128),
                State = speed.ToString("0.##", CultureInfo.InvariantCulture) + "x",
                Timestamps = new Timestamps(_playingStartUtc),
                Assets = new Assets
                {
                    LargeImageKey = LargeImageKey,
                    LargeImageText = LargeImageText,
                },
            };
            Client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            GD.PushWarning("Discord RPC update failed: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026";
    }

    /// <summary>
    /// Routes DiscordRichPresence log output into Godot's logger. The
    /// interface declares `params object[]` for each method.
    /// </summary>
    private sealed class GodotLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Warning;

        public GodotLogger() { }
        public GodotLogger(LogLevel level) { Level = level; }

        public void Trace(string message, params object[] args) { }
        public void Info(string message, params object[] args) { }

        public void Warning(string message, params object[] args)
        {
            GD.PushWarning("[DiscordRPC] " + Format(message, args));
        }

        public void Error(string message, params object[] args)
        {
            GD.PushError("[DiscordRPC] " + Format(message, args));
        }

        private static string Format(string message, object[] args)
        {
            if (args == null || args.Length == 0) return message ?? "";
            try { return string.Format(message ?? "", args); }
            catch { return message ?? ""; }
        }
    }
}
