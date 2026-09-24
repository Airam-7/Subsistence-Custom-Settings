using System.Collections.Immutable;

namespace SCS.Core;

public enum InstallationStatus { Unconfigured, Selected, Checking, Ready, Invalid }
public sealed record InstallationVerification(InstallationStatus Status, string? BinariesPath, FileFailure? Failure = null)
{
    public bool Ready => Status == InstallationStatus.Ready;
}
public sealed record InstallationResult(ImmutableArray<FileWriteResult> Files, ImmutableArray<ValidationIssue> Issues)
{
    public bool Success => Issues.IsEmpty && Files.All(f => f.Success);
}

public sealed class InstallationService(AtomicFileWriter? writer = null)
{
    private readonly AtomicFileWriter writer = writer ?? new();

    public InstallationVerification Verify(string? binariesPath)
    {
        if (string.IsNullOrWhiteSpace(binariesPath)) return new(InstallationStatus.Unconfigured, null);
        string? probe = null;
        try
        {
            if (!Path.IsPathFullyQualified(binariesPath)) return Invalid(binariesPath, FileFailureKind.WrongFolder, "Choose an absolute Binaries path.");
            binariesPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binariesPath));
            if (!string.Equals(Path.GetFileName(binariesPath), "Binaries", StringComparison.OrdinalIgnoreCase))
                return Invalid(binariesPath, FileFailureKind.WrongFolder, "Select the game's Binaries directory.");
            // Unlike Directory.Exists, GetAttributes preserves access-denied errors.
            if ((File.GetAttributes(binariesPath) & FileAttributes.Directory) == 0)
                return Invalid(binariesPath, FileFailureKind.WrongFolder, "The selected path is not a directory.");
            probe = Path.Combine(binariesPath, ".scs-write-check-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.WriteByte(0); stream.Flush(flushToDisk: true); }
            File.Delete(probe); probe = null;
            return new(InstallationStatus.Ready, binariesPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(InstallationStatus.Invalid, binariesPath, AtomicFileWriter.Classify(ex)); }
        finally
        {
            if (probe is not null) try { File.Delete(probe); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static InstallationVerification Invalid(string path, FileFailureKind kind, string message) =>
        new(InstallationStatus.Invalid, path, new(kind, message));

    public InstallationResult InstallMissing(SettingsCatalog catalog, IEnumerable<ProfileRecord> savedCustomProfiles, string binariesPath)
    {
        var verification = Verify(binariesPath);
        if (!verification.Ready) return new([], [new("Installation", null, verification.Failure?.Message ?? "Choose an installation first.")]);
        var custom = savedCustomProfiles.ToArray();
        if (custom.Length != 4 || custom.Any(p => p.IsReadOnly) || custom.Select(p => p.Id).Distinct().Count() != 4)
            return new([], [new("Profiles", null, "Provide the four distinct saved custom profiles.")]);
        var profiles = custom.Append(ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog)).OrderBy(p => p.Id).ToArray();
        var compiler = new ProfileCompiler(catalog);
        var compiled = profiles.Select(compiler.Compile).ToArray();
        var issues = compiled.SelectMany(c => c.Issues).ToImmutableArray();
        if (!issues.IsEmpty) return new([], issues);
        var files = profiles.Select((p, i) => writer.WriteConsoleText(Path.Combine(verification.BinariesPath!, p.FileName),
            compiled[i].Text, ExistingFileBehavior.Preserve)).ToImmutableArray();
        return new(files, []);
    }
}
