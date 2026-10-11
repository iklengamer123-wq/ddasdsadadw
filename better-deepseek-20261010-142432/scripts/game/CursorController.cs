using System;
using System.IO;
using Godot;
using CustomRhythia.Util;

namespace CustomRhythia.Game;

/// <summary>
/// Renders the in-game cursor. Default is a plain white circle drawn by a
/// fragment shader; if Settings.CursorFile points to an image in the
/// cursors folder, that image is used instead. Size and opacity come from
/// settings and update live.
/// </summary>
public partial class CursorController : Node3D
{
    private const string CursorShaderCode =
        "shader_type spatial;\n" +
        "render_mode unshaded, blend_mix, cull_disabled, depth_draw_opaque;\n" +
        "\n" +
        "uniform vec4 tint : source_color = vec4(1.0);\n" +
        "uniform float opacity : hint_range(0.0, 1.0) = 1.0;\n" +
        "uniform float is_custom = 0.0;\n" +
        "uniform sampler2D tex : source_color, filter_linear_mipmap;\n" +
        "\n" +
        "void fragment() {\n" +
        "    vec3 col;\n" +
        "    float alpha;\n" +
        "    if (is_custom > 0.5) {\n" +
        "        vec4 t = texture(tex, UV);\n" +
        "        col = t.rgb * tint.rgb;\n" +
        "        alpha = t.a * tint.a;\n" +
        "    } else {\n" +
        "        vec2 c = UV * 2.0 - 1.0;\n" +
        "        float d = length(c);\n" +
        "        float aa = max(fwidth(d), 0.001);\n" +
        "        alpha = 1.0 - smoothstep(1.0 - aa, 1.0, d);\n" +
        "        col = tint.rgb;\n" +
        "    }\n" +
        "    alpha *= opacity;\n" +
        "    if (alpha < 0.02) discard;\n" +
        "    ALBEDO = col;\n" +
        "    ALPHA = alpha;\n" +
        "}\n";

    public double X { get; private set; }
    public double Y { get; private set; }

    private MeshInstance3D _mesh;
    private ShaderMaterial _material;
    private QuadMesh _quad;

    private double _appliedScale = -1;
    private double _appliedOpacity = -1;
    private string _appliedCursorFile = null;

    public override void _Ready()
    {
        _material = new ShaderMaterial
        {
            Shader = new Shader { Code = CursorShaderCode },
        };
        _material.SetShaderParameter("tint", new Color(1, 1, 1, 1));
        _material.SetShaderParameter("opacity", 1.0f);
        _material.SetShaderParameter("is_custom", 0.0f);

        _quad = new QuadMesh { Size = Vector2.One * (float)Constants.CURSOR_SIZE };
        _quad.Material = _material;

        _mesh = new MeshInstance3D
        {
            Mesh = _quad,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_mesh);

        ApplySettings(force: true);
    }

    public void SetPosition(double x, double y)
    {
        X = Mathf.Clamp((float)x, -Constants.BOUNDS.X, Constants.BOUNDS.X);
        Y = Mathf.Clamp((float)y, -Constants.BOUNDS.Y, Constants.BOUNDS.Y);
        Position = new Vector3((float)X, (float)Y, 0);
    }

    public override void _Process(double delta)
    {
        ApplySettings(force: false);

        double spin = SettingsManager.Current.CursorRotation;
        if (Math.Abs(spin) > 0.001)
        {
            _mesh.RotateZ((float)(spin * Mathf.Pi / 180.0 * delta));
        }
    }

    /// <summary>
    /// Reads the current settings and updates the material + mesh only when
    /// something actually changed (cheap enough to run every frame).
    /// </summary>
    private void ApplySettings(bool force)
    {
        var s = SettingsManager.Current;

        if (force || Math.Abs(s.CursorScale - _appliedScale) > 0.0001)
        {
            _appliedScale = s.CursorScale;
            float baseSize = (float)(Constants.CURSOR_SIZE * s.CursorScale);
            _quad.Size = new Vector2(baseSize, baseSize);
            _mesh.Mesh = null; // force refresh on some GPU drivers
            _mesh.Mesh = _quad;
        }

        if (force || Math.Abs(s.CursorOpacity - _appliedOpacity) > 0.0001)
        {
            _appliedOpacity = s.CursorOpacity;
            _material.SetShaderParameter("opacity", (float)s.CursorOpacity);
        }

        string file = s.CursorFile ?? "";
        if (force || file != _appliedCursorFile)
        {
            _appliedCursorFile = file;

            if (string.IsNullOrEmpty(file))
            {
                _material.SetShaderParameter("is_custom", 0.0f);
            }
            else
            {
                string path = $"{Constants.CursorsFolder}/{file}";
                Image img = Images.LoadFromFile(path);
                if (img == null)
                {
                    _material.SetShaderParameter("is_custom", 0.0f);
                }
                else
                {
                    img.GenerateMipmaps();
                    var tex = ImageTexture.CreateFromImage(img);
                    _material.SetShaderParameter("tex", tex);
                    _material.SetShaderParameter("is_custom", 1.0f);
                }
            }
        }
    }
}
