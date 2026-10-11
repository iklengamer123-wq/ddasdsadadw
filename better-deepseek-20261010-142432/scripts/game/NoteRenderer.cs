using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using CustomRhythia.Map;

namespace CustomRhythia.Game;

/// <summary>
/// Draws notes as hollow outlined squares.
///
/// Performance: the renderer only iterates the small slice of notes that can
/// actually be on-screen right now (a few dozen at most), instead of the
/// whole chart. For a 4-minute song with thousands of notes this cuts the
/// per-frame work by 10-50x and removes the gradual slowdown that came from
/// touching every note every frame.
/// </summary>
public partial class NoteRenderer : Node3D
{
    private const string OutlineShaderCode =
        "shader_type spatial;\n" +
        "render_mode unshaded, blend_mix, cull_disabled, depth_draw_opaque;\n" +
        "\n" +
        "uniform float thickness : hint_range(0.0, 0.5) = 0.07;\n" +
        "\n" +
        "void fragment() {\n" +
        "    vec2 u = UV;\n" +
        "    vec2 d = min(u, vec2(1.0) - u);\n" +
        "    float dist = min(d.x, d.y);\n" +
        "    float aa = max(fwidth(dist), 0.001);\n" +
        "    if (dist > thickness + aa) discard;\n" +
        "    float border = 1.0 - smoothstep(thickness, thickness + aa, dist);\n" +
        "    ALBEDO = COLOR.rgb;\n" +
        "    ALPHA = COLOR.a * border;\n" +
        "}\n";

    private MultiMeshInstance3D _mmi;
    private Runner _runner;
    private ShaderMaterial _material;

    private static readonly Color[] DefaultColorset = { new("ffffff"), new("4a9eff") };
    private Color[] _colorset = DefaultColorset;
    private string _colorsetLoadedFrom = null;

    private static readonly Color Transparent = new(1, 1, 1, 0);

    // Keeps the multimesh buffer from being reallocated on every tiny change.
    private const int InstanceGrowChunk = 64;

    public override void _Ready()
    {
        _material = new ShaderMaterial
        {
            Shader = new Shader { Code = OutlineShaderCode },
        };

        var quad = new QuadMesh { Size = Vector2.One };
        quad.Material = _material;

        _mmi = new MultiMeshInstance3D
        {
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                Mesh = quad,
                InstanceCount = 0,
                VisibleInstanceCount = 0,
            },
        };
        AddChild(_mmi);

        ReloadColorsetIfNeeded();
    }

    public void Bind(Runner runner) => _runner = runner;

    public override void _Process(double delta)
    {
        if (_runner?.Attempt == null) return;
        var attempt = _runner.Attempt;
        var settings = SettingsManager.Current;

        ReloadColorsetIfNeeded();

        _material.SetShaderParameter("thickness",
            Mathf.Clamp((float)settings.NoteOutlineThickness, 0.02f, 0.3f));

        float ar = (float)settings.ApproachRate;
        float ad = (float)settings.ApproachDistance;
        float at = (float)settings.ApproachTime;
        if (at <= 0.0001f) at = 0.5f;

        float noteSize = (float)settings.NoteSize;
        float fadeIn = (float)settings.FadeIn / 100f;
        float fadeOut = (float)settings.FadeOut / 100f;
        float opacityMul = Mathf.Clamp((float)settings.NoteOpacity, 0f, 1f);
        bool pushback = settings.Pushback;
        float hitWindowDepth = pushback ? (float)(Constants.HIT_WINDOW * ar / 1000.0) : 0f;
        double hitWindowMs = Constants.HIT_WINDOW * attempt.Speed;
        double approachMs = at * 1000.0 * attempt.Speed;

        var notes = attempt.Map.Notes;
        if (notes == null || notes.Length == 0)
        {
            _mmi.Multimesh.VisibleInstanceCount = 0;
            return;
        }

        // ---- Windowed range ----
        // Above this bound everything is resolved and off-screen.
        double lowerMs = attempt.Progress - hitWindowMs - 250.0;
        // Below this bound notes aren't visible yet.
        double upperMs = attempt.Progress + approachMs + 50.0;

        int firstIdx = LowerBound(notes, lowerMs);
        int endIdx = LowerBound(notes, upperMs); // exclusive

        int visibleCount = endIdx - firstIdx;
        if (visibleCount < 0) visibleCount = 0;

        // Grow the multimesh buffer only when needed, in chunks.
        if (_mmi.Multimesh.InstanceCount < visibleCount)
        {
            int target = ((visibleCount / InstanceGrowChunk) + 1) * InstanceGrowChunk;
            _mmi.Multimesh.InstanceCount = target;
        }
        _mmi.Multimesh.VisibleInstanceCount = visibleCount;

        // ---- Draw only the visible slice ----
        for (int j = 0; j < visibleCount; j++)
        {
            var note = notes[firstIdx + j];

            if (note.LastResult != HitResult.None)
            {
                _mmi.Multimesh.SetInstanceColor(j, Transparent);
                // Transform isn't visible so we can skip setting it.
                continue;
            }

            double depth = (note.Millisecond - attempt.Progress)
                / (1000.0 * at) * ad / attempt.Speed;

            if (depth < -(hitWindowDepth) - 0.001 || depth > ad + 0.001)
            {
                _mmi.Multimesh.SetInstanceColor(j, Transparent);
                continue;
            }

            float progress = 1f - Mathf.Max(0f, (float)((depth + hitWindowDepth) / (ad + hitWindowDepth)));
            float opacity = 1f;
            if (fadeIn > 0) opacity = Mathf.Min(1f, progress / fadeIn);
            if (fadeOut > 0) opacity -= 1f - Mathf.Min(1f, (1f - progress) / fadeOut);

            float zJitter = (note.Index % 32) * 0.0005f;

            var transform = new Transform3D(
                new Basis(Vector3.Right * noteSize, Vector3.Up * noteSize, Vector3.Back * noteSize),
                new Vector3(note.X, note.Y, -(float)depth + zJitter));

            var color = _colorset[note.Index % _colorset.Length];
            color.A = Mathf.Clamp(opacity * opacityMul, 0, 1);

            _mmi.Multimesh.SetInstanceTransform(j, transform);
            _mmi.Multimesh.SetInstanceColor(j, color);
        }
    }

    /// <summary>
    /// Binary search: index of the first note whose Millisecond >= target.
    /// Notes are guaranteed sorted ascending by time.
    /// </summary>
    private static int LowerBound(Note[] notes, double target)
    {
        int lo = 0;
        int hi = notes.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (notes[mid].Millisecond < target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private void ReloadColorsetIfNeeded()
    {
        string name = SettingsManager.Current.NoteColorset ?? "";
        if (name == _colorsetLoadedFrom) return;
        _colorsetLoadedFrom = name;

        if (string.IsNullOrEmpty(name) || name == "Default")
        {
            _colorset = DefaultColorset;
            return;
        }

        string path = $"{Constants.ColorsetsFolder}/{name}.txt";
        if (!File.Exists(path))
        {
            _colorset = DefaultColorset;
            return;
        }

        try
        {
            string txt = File.ReadAllText(path);
            string[] parts = txt.Split(
                new[] { ',', '\n', '\r', ' ', '\t', ';' },
                StringSplitOptions.RemoveEmptyEntries);

            var list = new List<Color>();
            foreach (string raw in parts)
            {
                string hex = raw.Trim().TrimStart('#');
                if (hex.Length == 6
                    && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
                {
                    byte r = (byte)((rgb >> 16) & 0xFF);
                    byte g = (byte)((rgb >> 8) & 0xFF);
                    byte b = (byte)(rgb & 0xFF);
                    list.Add(new Color(r / 255f, g / 255f, b / 255f));
                }
            }

            _colorset = list.Count > 0 ? list.ToArray() : DefaultColorset;
        }
        catch
        {
            _colorset = DefaultColorset;
        }
    }
}
