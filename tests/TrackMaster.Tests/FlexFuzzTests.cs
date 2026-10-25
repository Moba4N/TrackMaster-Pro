using TrackMaster.Core;
using Xunit;

namespace TrackMaster.Tests;

public class FlexFuzzTests
{
    [Fact]
    public void Flexgleis_biegen_ueberall_ohne_Ausnahme()
    {
        var rnd = new Random(3);
        foreach (var gauge in new[] { "N", "H0" })
        {
            var s = new PlanSession();
            s.NewPlan(new BoardSetup(gauge, BoardShape.Rechteck, 3000, 2000, 600, 0));
            var first = s.PlaceFlex(s.CurrentFlex, new Pose(1000, 1000, 0), [Segment.Line(300)], null, null)!;
            var other = s.PlaceFlex(s.CurrentFlex, new Pose(1500, 700, 90), [Segment.Line(300)], null, null)!;
            for (var i = 0; i < 3000; i++)
            {
                double x = 1000 + rnd.NextDouble() * 1600 - 800, y = 1000 + rnd.NextDouble() * 1600 - 800;
                if (i % 7 == 0) { x = 1300; y = 1000; }          // genau auf der Geraden voraus
                if (i % 11 == 0) { x = 700; y = 1000; }          // genau dahinter
                if (i % 13 == 0) { x = 1000 + rnd.NextDouble() * 4; y = 1000; }
                if (i % 17 == 0) { x = 1000 - rnd.NextDouble() * 600; y = 1000 + rnd.NextDouble() * 0.01; } // knapp hinter dem Ende
                var target = i % 3 == 0 ? s.FlexTarget(x, y, 400, first.Id) : null;
                if (s.BeginReshape(first.Id, 1))
                {
                    var segs = s.Reshape(x, y, target);
                    s.Reshape(x + 0.3, y - 0.2, null);
                    s.EndReshape(segs);
                }
                var draft = s.BeginFlexDraw(x, y, i % 2 == 0 ? s.Connections.Open.FirstOrDefault() : null);
                draft.Update(x + rnd.NextDouble() * 200 - 100, y + rnd.NextDouble() * 200 - 100, i % 4 == 0 ? s.FlexTarget(x, y, 800) : null);
                Assert.True(draft.Segments.Count == 0 || FlexTrack.IsValid(draft.Segments));
                Assert.True(FlexTrack.Length(draft.Segments) <= FlexTrack.MaxLength);
                foreach (var l in draft.Legs) { _ = l.End; _ = l.Offset(5); }
                _ = FlexTrack.Describe(draft.Segments);
                Check(s.Plan);
            }
            s.ConnectEnds(s.CurrentFlex);
            Check(s.Plan);
        }
    }

    private static void Check(LayoutPlan plan)
    {
        foreach (var p in plan.Pieces)
        {
            Assert.True(p.Def.TrackLength <= FlexTrack.MaxLength + 1, p.Key);
            foreach (var l in p.WorldLegs())
            {
                var e = l.End;
                Assert.True(double.IsFinite(e.X) && double.IsFinite(e.Y), p.Key);
                var o = l.Offset(4);
                Assert.True(double.IsFinite(o.Item1) && double.IsFinite(o.Item5), p.Key);
            }
            _ = p.WorldConnectors();
            _ = p.ConnectorHeights();
        }
        _ = plan.Statistics();
        _ = BillOfMaterials.Build(plan);
        _ = PlanCheck.Run(plan);
        _ = Electrics.Analyze(plan);
        _ = SvgExport.Build(plan);
        _ = PlanSerializer.FromJson(PlanSerializer.ToJson(plan), out _);
    }
}
