using System.Windows.Media;
using System.Windows.Media.Media3D;
using TrackMaster.Core;

namespace TrackMaster.App;

/// <summary>
/// WPF-Darstellung des gemeinsamen 3D-Modells (<see cref="Scene3DModel"/>):
/// je Farbe ein Netz, Wasser und Schienen glänzen, Fenster leuchten nachts, Signallampen immer.
/// </summary>
public sealed class Scene3D
{
    /// <summary>Fensterlicht der Gebäude; nachts gelb, tagsüber schwarz (leuchtet nicht).</summary>
    public SolidColorBrush WindowGlow { get; } = new(Colors.Black);

    public Model3DGroup Model { get; } = new();

    public Scene3D(LayoutPlan plan) => Fill(Model, new Scene3DModel(plan).Meshes);

    /// <summary>Ersetzt den Inhalt von <paramref name="target"/> durch die Netze (auch für den fahrenden Zug).</summary>
    public void Fill(Model3DGroup target, IReadOnlyDictionary<(Rgb Color, SurfaceKind Kind), SceneMesh> meshes)
    {
        target.Children.Clear();
        foreach (var ((rgb, kind), mesh) in meshes)
        {
            if (mesh.IsEmpty) continue;
            var color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(kind == SurfaceKind.Lamp ? Brushes.Black : new SolidColorBrush(color)));
            switch (kind)
            {
                case SurfaceKind.Water: material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xC0, 0xD8, 0xF0)), 40)); break;
                case SurfaceKind.Rail: material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)), 20)); break;
                case SurfaceKind.Window: material.Children.Add(new EmissiveMaterial(WindowGlow)); break;
                case SurfaceKind.Lamp: material.Children.Add(new EmissiveMaterial(new SolidColorBrush(color))); break;
            }
            target.Children.Add(new GeometryModel3D(ToMesh(mesh), material) { BackMaterial = material });
        }
    }

    private static MeshGeometry3D ToMesh(SceneMesh mesh)
    {
        var points = new Point3DCollection(mesh.Points.Count);
        var indices = new Int32Collection(mesh.Points.Count);
        foreach (var p in mesh.Points)
        {
            indices.Add(points.Count);
            points.Add(new Point3D(p.X, p.Y, p.Z));
        }
        var m = new MeshGeometry3D { Positions = points, TriangleIndices = indices };
        m.Freeze();
        return m;
    }
}
