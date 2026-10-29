using TrackMaster.Licensing;
using TrackMaster.Licensing.Management;

// Lizenz-Werkzeug für den Hersteller von TrackPlan.
//   keygen  <ordner>                                   erzeugt trackmaster.private.pem und trackmaster.public.pem
//   issue   <private.pem> <ausgabe.lic> --name "Max Muster" [--email a@b.de] [--expires 2027-12-31] [--machine <id>]
//   verify  <datei.lic> [public.pem]
//   machine                                            zeigt die Rechnerkennung dieses PCs

return args.FirstOrDefault()?.ToLowerInvariant() switch
{
    "keygen" => KeyGen(args),
    "issue" => Issue(args),
    "verify" => Verify(args),
    "machine" => Machine(),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("""
        TrackPlan Lizenz-Werkzeug

          keygen  <ordner>
          issue   <private.pem> <ausgabe.lic> --name <Name> [--email <mail>] [--expires <JJJJ-MM-TT>] [--machine <id>]
          verify  <datei.lic> [public.pem]
          machine

        Jede Lizenz ist eine Pro-Lizenz.
        """);
    return 1;
}

static int KeyGen(string[] args)
{
    if (args.Length < 2) return Usage();
    try
    {
        var (privatePath, publicPath) = KeyTools.CreateKeyPair(args[1]);
        Console.WriteLine($"Privater Schlüssel: {privatePath} (geheim halten, nicht einchecken)");
        Console.WriteLine($"Öffentlicher Schlüssel: {publicPath} (in src/TrackMaster.Licensing/LicensePublicKey.cs eintragen)");
        return 0;
    }
    catch (IOException e)
    {
        Console.Error.WriteLine(e.Message);
        return 2;
    }
}

static int Issue(string[] args)
{
    if (args.Length < 3) return Usage();
    var options = ParseOptions(args.Skip(3).ToArray());
    if (!options.TryGetValue("name", out var name)) return Usage();
    DateTime? expires = null;
    if (options.TryGetValue("expires", out var exp))
    {
        if (!DateTime.TryParse(exp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
        {
            Console.Error.WriteLine($"Ungültiges Datum „{exp}“ (Format JJJJ-MM-TT).");
            return 2;
        }
        expires = d;
    }
    var request = new LicenseRequest { Licensee = name, Email = options.GetValueOrDefault("email"), ExpiresOn = expires, MachineId = options.GetValueOrDefault("machine") };
    try
    {
        var key = LoadedKey.FromFile(args[1]);
        if (!key.MatchesEmbedded)
            Console.Error.WriteLine("Achtung: Dieser Schlüssel passt nicht zum öffentlichen Schlüssel im Programm – TrackPlan erkennt die Lizenz so nicht an.");
        var (data, text) = LicenseIssuer.Issue(request, key.PrivateKeyPem, DateTimeOffset.Now);
        File.WriteAllText(args[2], text);
        Console.WriteLine($"Pro-Lizenz {data.LicenseId} für {data.Licensee} erstellt: {args[2]}");
        return 0;
    }
    catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException)
    {
        Console.Error.WriteLine(e.Message);
        return 2;
    }
}

static int Verify(string[] args)
{
    if (args.Length < 2) return Usage();
    var publicPem = args.Length > 2 ? File.ReadAllText(args[2]) : LicensePublicKey.Pem;
    var result = LicenseCodec.Verify(File.ReadAllText(args[1]), publicPem, DateTimeOffset.Now, MachineFingerprint.Current);
    Console.WriteLine($"Status: {result.StatusText}");
    if (result.License is { } l)
    {
        Console.WriteLine($"Lizenznehmer: {l.Licensee}");
        Console.WriteLine($"Pakete: {string.Join(", ", l.Features.Select(Features.DisplayName))}");
        Console.WriteLine($"Gültig bis: {(l.ExpiresAt?.ToString("dd.MM.yyyy") ?? "unbefristet")}");
        if (l.MachineId is not null) Console.WriteLine($"Rechner: {l.MachineId}");
    }
    return result.IsValid ? 0 : 3;
}

static int Machine()
{
    Console.WriteLine(MachineFingerprint.Current);
    return 0;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i + 1 < args.Length; i += 2)
        if (args[i].StartsWith("--", StringComparison.Ordinal))
            result[args[i][2..]] = args[i + 1];
    return result;
}
