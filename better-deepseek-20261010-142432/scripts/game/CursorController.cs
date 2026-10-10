using Godot;

namespace CustomRhythia.Game;

/// <summary>
/// Renders the cursor mesh and exposes the current cursor position.
/// </summary>
public partial class CursorController : Node3D
{
    public double X { get; private set; }
    public double Y { get; private set; }

    private MeshInstance3D _mesh;
    private double _rotationDegPerSec;

    public override void _Ready()
    {
        var settings = SettingsManager.Current;
        float size = (float)(Constants.CURSOR_SIZE * settings.CursorScale);

        var quad = new QuadMesh { Size = Vector2.One * size };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1, 1, 1, 0.85f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            BillboardKeepScale = true,
            DisableReceiveShadows = true,
        };
        quad.Material = mat;

        _mesh = new MeshInstance3D { Mesh = quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_mesh);

        _rotationDegPerSec = settings.CursorRotation;
    }

    public void SetPosition(double x, double y)
    {
        X = Mathf.Clamp((float)x, -Constants.BOUNDS.X, Constants.BOUNDS.X);
        Y = Mathf.Clamp((float)y, -Constants.BOUNDS.Y, Constants.BOUNDS.Y);
        Position = new Vector3((float)X, (float)Y, 0);
    }

    public override void _Process(double delta)
    {
        if (_rotationDegPerSec != 0)
        {
            _mesh.RotateZ((float)(_rotationDegPerSec * Mathf.Pi / 180.0 * delta));
        }
    }
}
