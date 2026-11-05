namespace TrackMaster.Core;

/// <summary>
/// Fest eingebaute Grundmodelle (ohne Lizenz nutzbar). Maße im Vorbild in Metern; x = Länge (beim Gleisbezug entlang des Gleises),
/// y = Tiefe, z = Höhe. Beim Gleisbezug „neben dem Gleis“ zeigt die Seite −y zum Gleis.
/// </summary>
public static class BuiltinModels
{
    public static string CatTunnel => Lang.T("Tunnel und Brücken");
    public static string CatStation => Lang.T("Bahnhof");
    public static string CatRailway => Lang.T("Bahnanlagen");
    public static string CatHouses => Lang.T("Häuser");

    private static IReadOnlyList<ModelDefinition>? _all;
    private static Language _language;

    /// <summary>Alle Grundmodelle (Namen in der eingestellten Sprache).</summary>
    public static IReadOnlyList<ModelDefinition> All
    {
        get
        {
            if (_all is null || _language != Lang.Current) { _all = Build(); _language = Lang.Current; }
            return _all;
        }
    }

    private static ModelBody B(BodyShape s, double x, double y, double z, double w, double d, double h, ModelMaterial m, double rot = 0, string? color = null)
        => new() { Shape = s, X = x, Y = y, Z = z, W = w, D = d, H = h, Material = m, Rotation = rot, Color = color };

    /// <summary>Fenster gleichmäßig auf einer Wand: Wand von x0 bis x1 bei y (Außenseite), in Geschossen ab z.</summary>
    private static IEnumerable<ModelBody> WindowsX(double x0, double x1, double y, double z, int floors = 1, double floorH = 3, double spacing = 3, double w = 1.1, double h = 1.4)
    {
        var n = Math.Max(1, (int)((x1 - x0) / spacing));
        for (var f = 0; f < floors; f++)
            for (var i = 0; i < n; i++)
                yield return B(BodyShape.Fenster, x0 + (x1 - x0) * (i + 0.5) / n, y, z + f * floorH, w, 0.1, h, ModelMaterial.Glas);
    }

    private static IEnumerable<ModelBody> WindowsY(double y0, double y1, double x, double z, int floors = 1, double floorH = 3, double spacing = 3, double w = 1.1, double h = 1.4)
    {
        var n = Math.Max(1, (int)((y1 - y0) / spacing));
        for (var f = 0; f < floors; f++)
            for (var i = 0; i < n; i++)
                yield return B(BodyShape.Fenster, x, y0 + (y1 - y0) * (i + 0.5) / n, z + f * floorH, w, 0.1, h, ModelMaterial.Glas, 90);
    }

    /// <summary>Haus: Wände w × d × wallH, Fenster rundum, Tür vorne (+y), Dach darauf.</summary>
    private static List<ModelBody> House(double w, double d, double wallH, ModelMaterial wall, BodyShape roof, double roofH, ModelMaterial roofMat, int floors = 1, double x = 0, double y = 0, bool door = true)
    {
        var floorH = wallH / floors;
        var winZ = Math.Min(0.9, floorH * 0.3);
        var list = new List<ModelBody> { B(BodyShape.Quader, x, y, 0, w, d, wallH, wall) };
        list.AddRange(WindowsX(x - w / 2, x + w / 2, y - d / 2 - 0.05, winZ, floors, floorH));
        list.AddRange(WindowsX(x - w / 2, x + w / 2, y + d / 2 + 0.05, winZ, floors, floorH).Where((_, i) => !door || i != 0));
        list.AddRange(WindowsY(y - d / 2, y + d / 2, x - w / 2 - 0.05, winZ, floors, floorH));
        list.AddRange(WindowsY(y - d / 2, y + d / 2, x + w / 2 + 0.05, winZ, floors, floorH));
        if (door)
        {
            var n = Math.Max(1, (int)(w / 3));
            list.Add(B(BodyShape.Tuer, x - w / 2 + w * 0.5 / n, y + d / 2 + 0.05, 0, 1.2, 0.1, 2.2, ModelMaterial.Holz));
        }
        list.Add(B(roof, x, y, wallH, w + 0.6, d + 0.6, roofH, roofMat));
        return list;
    }

    private static ModelDefinition M(string id, string name, string category, ModelSnap snap, string description, IEnumerable<ModelBody> bodies, ModelMaterial? main = null)
        => new() { Id = ModelDefinition.BuiltinPrefix + id, Name = name, Category = category, Snap = snap, Description = description, Bodies = bodies.ToList(), MainMaterial = main };

    private static List<ModelDefinition> Build()
    {
        var list = new List<ModelDefinition>();

        // ---------- Tunnel und Brücken (Gleis entlang x) ----------
        list.Add(M("tunnelportal-1", Lang.T("Tunnelportal eingleisig"), CatTunnel, ModelSnap.Tunnelportal,
            Lang.T("Natursteinportal für ein Gleis; rastet am Tunnelanfang ein."),
            [
                B(BodyShape.Bogenwand, 0, 0, 0, 8, 1.2, 8, ModelMaterial.Naturstein, 90).With(o => { o.OpeningW = 4.8; o.OpeningH = 6.4; }),
                B(BodyShape.Quader, 0, 0, 8, 1.6, 8.8, 0.5, ModelMaterial.Beton),
                B(BodyShape.Quader, -0.2, -3.6, 0, 1.6, 0.9, 8, ModelMaterial.Naturstein),
                B(BodyShape.Quader, -0.2, 3.6, 0, 1.6, 0.9, 8, ModelMaterial.Naturstein),
                B(BodyShape.Keil, -1.4, -4.5, 0, 1.0, 3.2, 6, ModelMaterial.Naturstein, 90),
                B(BodyShape.Keil, -1.4, 4.5, 0, 1.0, 3.2, 6, ModelMaterial.Naturstein, 90),
            ]));
        list.Add(M("tunnelportal-2", Lang.T("Tunnelportal zweigleisig"), CatTunnel, ModelSnap.Tunnelportal,
            Lang.T("Betonportal für zwei Gleise; rastet am Tunnelanfang ein."),
            [
                B(BodyShape.Bogenwand, 0, 0, 0, 13, 1.4, 9.5, ModelMaterial.Beton, 90).With(o => { o.OpeningW = 9.4; o.OpeningH = 7.4; }),
                B(BodyShape.Quader, 0, 0, 9.5, 1.8, 13.6, 0.6, ModelMaterial.Beton),
                B(BodyShape.Keil, -1.5, -7.1, 0, 1.2, 3.4, 7, ModelMaterial.Beton, 90),
                B(BodyShape.Keil, -1.5, 7.1, 0, 1.2, 3.4, 7, ModelMaterial.Beton, 90),
            ]));
        list.Add(M("kastenbruecke", Lang.T("Kastenbrücke"), CatTunnel, ModelSnap.Unter,
            Lang.T("Stahlbrücke mit Pfeilern; liegt unter dem Gleis, die Höhe richtet sich nach dem Gleis."),
            [
                B(BodyShape.Quader, 0, 0, 5.6, 20, 4.6, 1.4, ModelMaterial.Metall, 0, "#4E5A66"),
                B(BodyShape.Quader, 0, -2.25, 7.0, 20, 0.12, 1.0, ModelMaterial.Metall),
                B(BodyShape.Quader, 0, 2.25, 7.0, 20, 0.12, 1.0, ModelMaterial.Metall),
                B(BodyShape.Quader, -9.0, 0, 0, 1.6, 5.4, 5.6, ModelMaterial.Beton),
                B(BodyShape.Quader, 9.0, 0, 0, 1.6, 5.4, 5.6, ModelMaterial.Beton),
            ], ModelMaterial.Beton));
        list.Add(M("brueckenkopf", Lang.T("Brückenkopf"), CatTunnel, ModelSnap.Unter,
            Lang.T("Widerlager aus Naturstein mit Flügelmauern; liegt unter dem Gleisende einer Brücke."),
            [
                B(BodyShape.Quader, 0, 0, 0, 3, 6, 6.6, ModelMaterial.Naturstein),
                B(BodyShape.Quader, 0, 0, 6.6, 3.2, 6.2, 0.4, ModelMaterial.Beton),
                B(BodyShape.Keil, 2.4, -3.6, 0, 1.8, 3.6, 6.6, ModelMaterial.Naturstein, -90),
                B(BodyShape.Keil, 2.4, 3.6, 0, 1.8, 3.6, 6.6, ModelMaterial.Naturstein, -90),
            ]));
        list.Add(M("stuetzmauer", Lang.T("Stützmauer"), CatTunnel, ModelSnap.Neben,
            Lang.T("Natursteinmauer neben dem Gleis, folgt Bögen."),
            [
                B(BodyShape.Quader, 0, 0, 0, 30, 1.0, 3.2, ModelMaterial.Naturstein).With(o => o.Top = 0.85),
                B(BodyShape.Quader, 0, 0, 3.2, 30, 0.9, 0.3, ModelMaterial.Beton),
            ]));

        // ---------- Bahnhof ----------
        list.Add(M("hausbahnsteig", Lang.T("Hausbahnsteig"), CatStation, ModelSnap.Neben,
            Lang.T("Bahnsteig an einem Gleis; rastet im Kantenabstand ein und folgt Bögen."),
            [
                B(BodyShape.Quader, 0, 0, 0, 60, 4, 0.55, ModelMaterial.Pflaster),
                B(BodyShape.Quader, 0, -1.85, 0, 60, 0.3, 0.56, ModelMaterial.Kante),
            ]));
        list.Add(M("mittelbahnsteig", Lang.T("Mittelbahnsteig"), CatStation, ModelSnap.Neben,
            Lang.T("Bahnsteig zwischen zwei Gleisen; rastet an einem Gleis ein und folgt Bögen."),
            [
                B(BodyShape.Quader, 0, 0, 0, 60, 6, 0.55, ModelMaterial.Pflaster),
                B(BodyShape.Quader, 0, -2.85, 0, 60, 0.3, 0.56, ModelMaterial.Kante),
                B(BodyShape.Quader, 0, 2.85, 0, 60, 0.3, 0.56, ModelMaterial.Kante),
            ]));
        var roofPosts = Enumerable.Range(0, 6).Select(i => B(BodyShape.Quader, -17.5 + i * 7, 0.2, 0, 0.3, 0.3, 3.9, ModelMaterial.Metall, 0, "#2E5A3A"));
        list.Add(M("bahnsteigdach", Lang.T("Bahnsteigdach"), CatStation, ModelSnap.Neben,
            Lang.T("Überdachung auf Stützen für den Bahnsteig; rastet neben dem Gleis ein."),
            [
                .. roofPosts,
                B(BodyShape.Quader, 0, 0.2, 3.9, 37, 0.4, 0.4, ModelMaterial.Metall, 0, "#2E5A3A"),
                B(BodyShape.Satteldach, 0, 0.2, 4.3, 38, 4.6, 0.6, ModelMaterial.Blech),
            ]));
        list.Add(M("wartehaeuschen", Lang.T("Wartehäuschen"), CatStation, ModelSnap.Frei,
            Lang.T("Kleiner Unterstand mit Glaswänden."),
            [
                B(BodyShape.Quader, 0, -0.75, 0, 3.2, 0.12, 2.4, ModelMaterial.Holz),
                B(BodyShape.Quader, -1.55, 0, 0, 0.1, 1.5, 2.4, ModelMaterial.Glas),
                B(BodyShape.Quader, 1.55, 0, 0, 0.1, 1.5, 2.4, ModelMaterial.Glas),
                B(BodyShape.Pultdach, 0, 0, 2.4, 3.6, 2.0, 0.35, ModelMaterial.Blech),
            ], ModelMaterial.Holz));
        list.Add(M("bahnhof-klein", Lang.T("Bahnhof klein (Haltepunkt)"), CatStation, ModelSnap.Frei,
            Lang.T("Kleines Empfangsgebäude mit Satteldach."),
            House(12, 8, 4, ModelMaterial.Putz, BodyShape.Satteldach, 3.4, ModelMaterial.Dachziegel)));
        var mittel = House(20, 10, 7, ModelMaterial.Putz, BodyShape.Walmdach, 4.2, ModelMaterial.Schiefer, floors: 2);
        mittel.AddRange(House(8, 7, 4, ModelMaterial.Putz, BodyShape.Satteldach, 2.6, ModelMaterial.Schiefer, x: 14, y: 1.5));
        mittel.Add(B(BodyShape.Pultdach, 0, 5.6, 3.0, 12, 2.4, 0.5, ModelMaterial.Blech));
        list.Add(M("bahnhof-mittel", Lang.T("Bahnhof mittel (mit Anbau)"), CatStation, ModelSnap.Frei,
            Lang.T("Zweigeschossiges Empfangsgebäude mit Walmdach, Anbau und Vordach."), mittel));

        // ---------- Bahnanlagen ----------
        var stw = new List<ModelBody> { B(BodyShape.Quader, 0, 0, 0, 6, 5, 3.6, ModelMaterial.Ziegel) };
        stw.AddRange(WindowsX(-3, 3, 2.55, 1.0, spacing: 2.5));
        stw.Add(B(BodyShape.Tuer, -2, -2.55, 0, 1.0, 0.1, 2.2, ModelMaterial.Holz));
        stw.Add(B(BodyShape.Quader, 0, 0, 3.6, 7, 6, 2.6, ModelMaterial.Holz, 0, "#C8B48A"));
        stw.AddRange(WindowsX(-3.5, 3.5, -3.05, 4.1, spacing: 1.4, w: 1.2, h: 1.5));
        stw.AddRange(WindowsX(-3.5, 3.5, 3.05, 4.1, spacing: 1.4, w: 1.2, h: 1.5));
        stw.AddRange(WindowsY(-3, 3, -3.55, 4.1, spacing: 1.4, w: 1.2, h: 1.5));
        stw.AddRange(WindowsY(-3, 3, 3.55, 4.1, spacing: 1.4, w: 1.2, h: 1.5));
        stw.Add(B(BodyShape.Walmdach, 0, 0, 6.2, 8, 7, 2.6, ModelMaterial.Schiefer));
        list.Add(M("stellwerk", Lang.T("Stellwerk"), CatRailway, ModelSnap.Frei, Lang.T("Stellwerk mit verglaster Kanzel."), stw, ModelMaterial.Ziegel));

        var gs = new List<ModelBody>
        {
            B(BodyShape.Quader, 0, 0, 0, 20, 8, 5, ModelMaterial.Holz),
            B(BodyShape.Satteldach, 0, 0, 5, 21, 11, 2.4, ModelMaterial.Blech),
            B(BodyShape.Quader, 0, -5.5, 0, 20, 3, 1.1, ModelMaterial.Beton),
            B(BodyShape.Treppe, 11.2, -5.5, 0, 3, 2.4, 1.1, ModelMaterial.Beton, -90),
        };
        gs.AddRange(Enumerable.Range(0, 3).Select(i => B(BodyShape.Tor, -6 + i * 6, -4.05, 1.1, 3, 0.1, 3, ModelMaterial.Holz, 0, "#6A4A30")));
        gs.AddRange(Enumerable.Range(0, 3).Select(i => B(BodyShape.Tor, -6 + i * 6, 4.05, 0, 3, 0.1, 3.4, ModelMaterial.Holz, 0, "#6A4A30")));
        list.Add(M("gueterschuppen", Lang.T("Güterschuppen mit Rampe"), CatRailway, ModelSnap.Frei,
            Lang.T("Holzschuppen mit Ladetoren und Laderampe auf der Gleisseite (−y)."), gs, ModelMaterial.Holz));

        list.Add(M("lokschuppen-1", Lang.T("Lokschuppen einständig"), CatRailway, ModelSnap.Auf,
            Lang.T("Lokschuppen über einem Gleis; rastet auf dem Gleis ein, das Tor zeigt zum Gleisanfang."), Shed(1)));
        list.Add(M("lokschuppen-2", Lang.T("Lokschuppen zweiständig"), CatRailway, ModelSnap.Frei,
            Lang.T("Lokschuppen für zwei Gleise im Abstand von 5 m."), Shed(2)));

        list.Add(M("bahnuebergang", Lang.T("Bahnübergang"), CatRailway, ModelSnap.Quer,
            Lang.T("Straße quer über das Gleis mit Schranken; rastet auf dem Gleis ein."),
            [
                B(BodyShape.Quader, 0, 0, 0, 6, 16, 0.22, ModelMaterial.Asphalt),
                B(BodyShape.Quader, 0, 0, 0.22, 6, 1.3, 0.04, ModelMaterial.Beton),
                B(BodyShape.Quader, -3.4, -3.6, 0, 0.3, 0.3, 1.2, ModelMaterial.Metall, 0, "#F0F0F0"),
                B(BodyShape.Quader, 3.4, 3.6, 0, 0.3, 0.3, 1.2, ModelMaterial.Metall, 0, "#F0F0F0"),
                B(BodyShape.Quader, -3.4, -3.6, 1.2, 0.14, 0.14, 5.5, ModelMaterial.Metall, 0, "#D03030"),
                B(BodyShape.Quader, 3.4, 3.6, 1.2, 0.14, 0.14, 5.5, ModelMaterial.Metall, 0, "#D03030"),
                B(BodyShape.Quader, -3.4, 3.6, 0, 0.12, 0.12, 2.4, ModelMaterial.Metall, 0, "#F0F0F0"),
                B(BodyShape.Quader, 3.4, -3.6, 0, 0.12, 0.12, 2.4, ModelMaterial.Metall, 0, "#F0F0F0"),
            ], ModelMaterial.Asphalt));
        list.Add(M("prellbock", Lang.T("Prellbock mit Erdhügel"), CatRailway, ModelSnap.Gleisende,
            Lang.T("Betonprellbock mit Schotterhügel; rastet an einem offenen Gleisende ein."),
            [
                B(BodyShape.Quader, -1.3, 0, 0, 1.0, 3.4, 1.3, ModelMaterial.Beton),
                B(BodyShape.Quader, -1.85, 0, 0.75, 0.15, 2.4, 0.35, ModelMaterial.Holz, 0, "#C83C2C"),
                B(BodyShape.Keil, 0.6, 0, 0, 3.4, 2.8, 1.6, ModelMaterial.Schotter, -90),
            ], ModelMaterial.Beton));

        // ---------- Häuser ----------
        var wohn = House(10, 9, 5.6, ModelMaterial.Putz, BodyShape.Satteldach, 4.2, ModelMaterial.Dachziegel, floors: 2);
        wohn.Add(B(BodyShape.Quader, 2.5, -1.5, 5.6, 0.7, 0.7, 4.4, ModelMaterial.Ziegel));
        list.Add(M("wohnhaus", Lang.T("Wohnhaus"), CatHouses, ModelSnap.Frei, Lang.T("Zweigeschossiges Wohnhaus mit Satteldach und Schornstein."), wohn));
        var bauer = House(14, 10, 3.6, ModelMaterial.Putz, BodyShape.Satteldach, 5, ModelMaterial.Dachziegel);
        bauer.Add(B(BodyShape.Quader, -4, 1, 3.6, 0.7, 0.7, 5.6, ModelMaterial.Ziegel));
        list.Add(M("bauernhaus", Lang.T("Bauernhaus"), CatHouses, ModelSnap.Frei, Lang.T("Eingeschossiges Bauernhaus mit steilem Dach."), bauer));
        list.Add(M("scheune", Lang.T("Scheune"), CatHouses, ModelSnap.Frei, Lang.T("Holzscheune mit großem Tor."),
            [
                B(BodyShape.Quader, 0, 0, 0, 18, 12, 5, ModelMaterial.Holz, 0, "#7A5434"),
                B(BodyShape.Satteldach, 0, 0, 5, 19, 13, 5.5, ModelMaterial.Dachziegel),
                B(BodyShape.Tor, 0, 6.05, 0, 4.5, 0.1, 4.5, ModelMaterial.Holz, 0, "#5A3C24"),
                B(BodyShape.Tor, 0, -6.05, 0, 4.5, 0.1, 4.5, ModelMaterial.Holz, 0, "#5A3C24"),
            ], ModelMaterial.Holz));
        return list;
    }

    /// <summary>Lokschuppen für ein oder zwei Gleise entlang x; Tore (offen) auf der Seite −x.</summary>
    private static List<ModelBody> Shed(int stands)
    {
        const double l = 25, wall = 0.5, h = 6;
        var d = stands == 1 ? 7.0 : 12.0;
        var list = new List<ModelBody>
        {
            B(BodyShape.Quader, 0, -d / 2 + wall / 2, 0, l, wall, h, ModelMaterial.Ziegel),
            B(BodyShape.Quader, 0, d / 2 - wall / 2, 0, l, wall, h, ModelMaterial.Ziegel),
            B(BodyShape.Quader, l / 2 - wall / 2, 0, 0, wall, d - 2 * wall, h, ModelMaterial.Ziegel),
            B(BodyShape.Satteldach, 0, 0, h, l + 0.6, d + 0.8, stands == 1 ? 2.4 : 3.4, ModelMaterial.Schiefer),
        };
        list.AddRange(WindowsX(-l / 2 + 2, l / 2 - 2, -d / 2 - 0.05, 2.4, spacing: 4, w: 1.6, h: 2.2));
        list.AddRange(WindowsX(-l / 2 + 2, l / 2 - 2, d / 2 + 0.05, 2.4, spacing: 4, w: 1.6, h: 2.2));
        if (stands == 1)
            list.Add(B(BodyShape.Bogenwand, -l / 2 + wall / 2, 0, 0, d, wall, h, ModelMaterial.Ziegel, 90).With(o => { o.OpeningW = 4.6; o.OpeningH = 5.4; }));
        else
        {
            list.Add(B(BodyShape.Bogenwand, -l / 2 + wall / 2, -d / 4, 0, d / 2, wall, h, ModelMaterial.Ziegel, 90).With(o => { o.OpeningW = 4.4; o.OpeningH = 5.4; }));
            list.Add(B(BodyShape.Bogenwand, -l / 2 + wall / 2, d / 4, 0, d / 2, wall, h, ModelMaterial.Ziegel, 90).With(o => { o.OpeningW = 4.4; o.OpeningH = 5.4; }));
        }
        return list;
    }

    private static ModelBody With(this ModelBody b, Action<ModelBody> set) { set(b); return b; }
}
