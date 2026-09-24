using SCS.Core;

try
{
    var catalog = SupportedCatalog.Create();
    var command = args.FirstOrDefault() ?? "demo";
    if (command == "vanilla") { Console.Write(new ProfileCompiler(catalog).Compile(ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog)).Text); return 0; }
    if (command == "demo")
    {
        var session = new ProfileEditSession(catalog, ProfileRecord.CreateDefault(ProfileId.Profile1, catalog));
        session.SetRawInput(ReferenceCatalog.WeaponUpgradeSpeed, "4");
        session.SetRawInput(ReferenceCatalog.CampfireFuelDuration, "2");
        Console.Write(new ProfileCompiler(catalog).Compile(session.Draft).Text);
        return 0;
    }
    if (command == "sample")
    {
        Console.WriteLine(ProfileJson.Serialize(ProfileRecord.CreateDefault(ProfileId.Profile1, catalog)));
        return 0;
    }
    if (command is "compile" or "migrate" && args.Length == 2)
    {
        var profile = ProfileJson.Deserialize(File.ReadAllText(args[1]));
        if (command == "migrate")
        {
            var migration = ProfileMigrator.Migrate(profile, catalog);
            if (!migration.Success) { foreach (var issue in migration.Issues) Console.Error.WriteLine(issue.Code + ": " + issue.Message); return 1; }
            Console.Error.WriteLine($"Added: {string.Join(", ", migration.Added)}; removed: {string.Join(", ", migration.Removed)}. No file was changed.");
            Console.WriteLine(ProfileJson.Serialize(migration.Profile!));
        }
        else
        {
            var result = new ProfileCompiler(catalog).Compile(profile);
            if (!result.Success) { foreach (var issue in result.Issues) Console.Error.WriteLine(issue.Code + ": " + issue.Message); return 1; }
            Console.Write(result.Text);
        }
        return 0;
    }
    Console.Error.WriteLine("SCS.Cli demo | vanilla | sample | compile <profile.json> | migrate <profile.json>");
    Console.Error.WriteLine("These commands only write to standard output. They never modify the game installation.");
    return 2;
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or System.Text.Json.JsonException or OverflowException)
{
    Console.Error.WriteLine(ex.Message); return 1;
}
