if (args.Length != 1) throw new ArgumentException("Fixture root required");
var report = EverestPreflight.ProbeChecks.Run(Path.GetFullPath(args[0]));
Console.WriteLine(report);
if (report.Split('\n').Any(line => line.StartsWith("FAIL: ", StringComparison.Ordinal)))
    return 1;
return 0;
