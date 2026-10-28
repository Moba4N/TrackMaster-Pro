namespace TrackMaster.Core;

/// <summary>
/// Vorgaben und Näherungen für die Fahrzeugprüfung in Bögen. Vorgabe ist ein Reisezugwagen mit 26,4 m Länge über Puffer
/// (Schmalspur 15 m), im Maßstab der Spur verkleinert; der Drehgestellabstand wird mit 70 % der Länge angenähert.
/// </summary>
public static class VehicleDefaults
{
    public const double BogieRatio = 0.7;
    /// <summary>Faustregel: Bogenradius mindestens so viel mal die Fahrzeuglänge.</summary>
    public const double RadiusFactor = 1.5;
    /// <summary>Empfohlene größte Steigung in Bögen (Prozent).</summary>
    public const double CurveGradePercent = 2.0;

    public static bool IsNarrow(Gauge gauge) => gauge.Id.EndsWith('m') || gauge.Id.EndsWith('e');

    public static double ScaleDivisor(Gauge gauge)
        => double.TryParse(gauge.Scale.Split(':').Last().Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) && d > 0 ? d : 87;

    public static double Length(Gauge gauge) => Math.Round((IsNarrow(gauge) ? 15000 : 26400) / ScaleDivisor(gauge));

    /// <summary>Ausschwenken im Bogen: Wagenmitte nach innen, Wagenenden nach außen (mm).</summary>
    public static (double Inner, double Outer) Swing(double radius, double length)
    {
        var half = BogieRatio * length / 2;
        if (radius <= half) return (radius, length);
        var axis = Math.Sqrt(radius * radius - half * half);
        return (radius - axis, Math.Sqrt(axis * axis + length * length / 4) - radius);
    }
}
