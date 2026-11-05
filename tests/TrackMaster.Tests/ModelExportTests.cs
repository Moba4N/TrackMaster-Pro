using TrackMaster.Core;

namespace TrackMaster.Tests;

[Collection("Sprache")]
public class ModelExportTests
{
    private static readonly ModelDefinition Box = new()
    {
        Id = "eigen.kiste.1", Name = "Kiste Ä",
        Bodies = [new ModelBody { Shape = BodyShape.Quader, W = 8.7, D = 4.35, H = 2, Y = 1, Material = ModelMaterial.Ziegel }],
    };

    [Fact]
    public void Stl_hat_Dreiecke_im_Massstab_und_nach_aussen_zeigende_Normalen()
    {
        var stl = ModelExport.ToStl(Box, 1000 / 87.0);
        var count = BitConverter.ToUInt32(stl, 80);
        Assert.Equal(84 + (int)count * 50, stl.Length);
        Assert.True(count >= 10);
        var xs = new List<float>(); var ys = new List<float>(); var zs = new List<float>();
        var outward = 0;
        for (var i = 0; i < count; i++)
        {
            var o = 84 + i * 50;
            float F(int k) => BitConverter.ToSingle(stl, o + k * 4);
            for (var v = 0; v < 3; v++) { xs.Add(F(3 + v * 3)); ys.Add(F(4 + v * 3)); zs.Add(F(5 + v * 3)); }
            // Normale zeigt vom Mittelpunkt der Kiste weg (Mitte bei y = −11,49 mm nach dem Spiegeln, z = 11,49 mm)
            double cx = (F(3) + F(6) + F(9)) / 3, cy = (F(4) + F(7) + F(10)) / 3 + 1000 / 87.0, cz = (F(5) + F(8) + F(11)) / 3 - 1000 / 87.0;
            if (F(0) * cx + F(1) * cy + F(2) * cz > 0) outward++;
        }
        Assert.Equal(100, xs.Max() - xs.Min(), 1);
        Assert.Equal(50, ys.Max() - ys.Min(), 1);
        Assert.Equal(23, zs.Max() - zs.Min(), 1);
        Assert.Equal(0, zs.Min(), 3);
        Assert.Equal(-1000 / 87.0, (ys.Max() + ys.Min()) / 2, 2);
        Assert.Equal((int)count, outward);
    }

    [Fact]
    public void Obj_mit_Materialien_fuer_alle_Grundmodelle()
    {
        foreach (var m in BuiltinModels.All)
        {
            var obj = ModelExport.ToObj(m, 1000, "m.mtl");
            var mtl = ModelExport.ToMtl(m);
            var lines = obj.Split('\n');
            var v = lines.Count(l => l.StartsWith("v "));
            var f = lines.Count(l => l.StartsWith("f "));
            Assert.Equal(v, f * 3);
            Assert.All(lines.Where(l => l.StartsWith("usemtl ")), l => Assert.Contains("newmtl " + l[7..], mtl));
            Assert.DoesNotContain(",", string.Join("\n", lines.Where(l => l.StartsWith("v "))));
        }
        Assert.Contains("o Kiste_Ae", ModelExport.ToObj(Box, 1, "x.mtl"));
    }

    [Fact]
    public void Zeichnen_fuer_Ansichten_sortiert_und_Schraegansicht_passt_zu_den_Rissen()
    {
        var m = BuiltinModels.All.First(x => x.Id == "tm.bahnhof-mittel");
        var tris = ModelDrawing.Draw(m, ModelDrawing.Ortho(EditorView.Draufsicht), [0], new Rgb(0, 255, 0));
        Assert.NotEmpty(tris);
        Assert.True(tris.Zip(tris.Skip(1)).All(p => p.First.Depth <= p.Second.Depth));
        Assert.Contains(tris, t => t.Body == 0);
        // Schrägansicht ohne Drehung und Neigung = Vorderansicht, mit 90° Neigung = Draufsicht
        static (double, double, double, double) B(List<DrawnTriangle> t) => ModelDrawing.Bounds(t);
        var front = B(ModelDrawing.Draw(m, ModelDrawing.Ortho(EditorView.Vorderansicht)));
        var o0 = B(ModelDrawing.Draw(m, ModelDrawing.Orbit(0, 0)));
        var top = B(ModelDrawing.Draw(m, ModelDrawing.Ortho(EditorView.Draufsicht)));
        var o90 = B(ModelDrawing.Draw(m, ModelDrawing.Orbit(0, 90)));
        Assert.Equal(front.Item1, o0.Item1, 6); Assert.Equal(front.Item2, o0.Item2, 6); Assert.Equal(front.Item4, o0.Item4, 6);
        Assert.Equal(top.Item1, o90.Item1, 6); Assert.Equal(top.Item2, o90.Item2, 6); Assert.Equal(top.Item3, o90.Item3, 6);
    }
}
