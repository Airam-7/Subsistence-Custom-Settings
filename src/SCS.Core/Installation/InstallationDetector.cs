using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SCS.Core;

public sealed record DetectedInstallation(string InstallationRoot, string BinariesPath, string Source);

public static partial class InstallationDetector
{
    [GeneratedRegex("\"path\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPaths();

    // Returns candidates for selection/verification; it never starts or inspects the game process.
    public static ImmutableArray<DetectedInstallation> Detect(IEnumerable<string>? steamRoots = null)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in steamRoots ?? DefaultSteamRoots())
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) continue;
            libraries.Add(root);
            var file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            try
            {
                if (!File.Exists(file)) continue;
                foreach (Match match in LibraryPaths().Matches(File.ReadAllText(file)))
                {
                    var library = match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                    if (Path.IsPathFullyQualified(library)) libraries.Add(library);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Unreadable libraries do not prevent manual Browse. */ }
        }
        return libraries.Select(root => Path.Combine(root, "steamapps", "common", "Subsistence"))
            .Where(root => Directory.Exists(Path.Combine(root, "Binaries")))
            .Select(root => new DetectedInstallation(Path.GetFullPath(root), Path.Combine(Path.GetFullPath(root), "Binaries"), "Steam library"))
            .DistinctBy(x => x.BinariesPath, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.BinariesPath, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }

    private static IEnumerable<string> DefaultSteamRoots()
    {
        var roots = new List<string>();
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programs)) roots.Add(Path.Combine(programs, "Steam"));
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string root) roots.Add(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return roots;
    }
}
