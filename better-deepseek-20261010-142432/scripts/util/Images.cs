using System.IO;
using Godot;

namespace CustomRhythia.Util;

/// <summary>
/// Safe image loading that sniffs the magic bytes instead of trusting the
/// extension. Returns null on failure and never spams Godot's error log.
/// </summary>
public static class Images
{
    public static Image LoadFromFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        byte[] buffer;
        try
        {
            buffer = File.ReadAllBytes(path);
        }
        catch (System.Exception ex)
        {
            GD.PushWarning("Could not read image file: " + ex.Message);
            return null;
        }

        return LoadFromBuffer(buffer, path);
    }

    public static Image LoadFromBuffer(byte[] buffer, string debugPath = null)
    {
        if (buffer == null || buffer.Length < 8) return null;

        bool isPng = buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47;
        bool isJpg = buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF;
        bool isBmp = buffer[0] == 0x42 && buffer[1] == 0x4D;
        bool isWebp = buffer.Length >= 12
            && buffer[0] == 0x52 && buffer[1] == 0x49 && buffer[2] == 0x46 && buffer[3] == 0x46
            && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50;

        Image img = new Image();
        Error err = Error.Failed;

        if (isPng) err = img.LoadPngFromBuffer(buffer);
        else if (isJpg) err = img.LoadJpgFromBuffer(buffer);
        else if (isWebp) err = img.LoadWebpFromBuffer(buffer);
        else if (isBmp) err = img.LoadBmpFromBuffer(buffer);

        if (err == Error.Ok) return img;

        if (!string.IsNullOrEmpty(debugPath))
        {
            GD.PushWarning("Cover is not a supported image, ignoring: " + debugPath);
        }
        return null;
    }
}
