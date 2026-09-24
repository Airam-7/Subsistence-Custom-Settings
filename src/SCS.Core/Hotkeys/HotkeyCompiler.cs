using System.Collections.Immutable;

namespace SCS.Core;

public sealed record HotkeyAssignment(ProfileId ProfileId, string KeyId, bool Control = false, bool Shift = false, bool Alt = false)
{
    public string Label => (Control ? "Ctrl + " : "") + (Shift ? "Shift + " : "") + (Alt ? "Alt + " : "") + KeyId;
}
public sealed record HotkeyDefinition(string KeyId, string Label, string ConsoleToken, bool PotentialConflict, string? Warning = null);
public sealed record HotkeyCompileResult(string Text, ImmutableArray<ValidationIssue> Issues, ImmutableArray<string> Warnings)
{
    public bool Success => Issues.IsEmpty;
}

public sealed class HotkeyCompiler
{
    public ImmutableDictionary<string, HotkeyDefinition> Keys { get; }
    public static ImmutableArray<HotkeyAssignment> Defaults => [new(ProfileId.Profile1, "F6"), new(ProfileId.Profile2, "F7"), new(ProfileId.Profile3, "F8"), new(ProfileId.Profile4, "F9"), new(ProfileId.Vanilla, "F10")];

    public HotkeyCompiler(IEnumerable<HotkeyDefinition>? keys = null)
    {
        keys ??= Enumerable.Range(1, 12).Select(i => new HotkeyDefinition("F" + i, "F" + i, "F" + i, i < 6 || i > 10,
            i < 6 || i > 10 ? "This key may conflict with another game or overlay binding." : null));
        var list = keys.ToArray();
        if (list.Any(k => string.IsNullOrWhiteSpace(k.KeyId) || !CatalogValidator.Identifier().IsMatch(k.ConsoleToken))
            || list.Select(k => k.KeyId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
            throw new ArgumentException("Hotkeys require unique IDs and safe console tokens.");
        Keys = list.ToImmutableDictionary(k => k.KeyId, StringComparer.OrdinalIgnoreCase);
    }

    public HotkeyCompileResult Compile(IEnumerable<HotkeyAssignment> assignments)
    {
        var list = assignments.ToArray(); var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        var warnings = ImmutableArray.CreateBuilder<string>();
        if (list.Length != 5 || list.Any(a => !Enum.IsDefined(a.ProfileId)) || list.Select(a => a.ProfileId).Distinct().Count() != 5)
            issues.Add(new("ProfileAssignments", null, "Each of the five profiles needs exactly one hotkey."));
        var assigned = new Dictionary<string, ProfileId>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in list.OrderBy(a => a.ProfileId))
        {
            if (assignment.Control || assignment.Shift || assignment.Alt) issues.Add(new("LegacyModifiers", null, "Modifier combinations require INI installation, not SetBind export."));
            if (!Keys.TryGetValue(assignment.KeyId, out var key)) { issues.Add(new("UnknownKey", null, "This key is not in the allowed key catalog.")); continue; }
            if (assigned.TryGetValue(key.ConsoleToken, out var previous))
                issues.Add(new("DuplicateHotkey", null, $"{key.Label} is already assigned to {ProfileLabel(previous)}."));
            else assigned.Add(key.ConsoleToken, assignment.ProfileId);
            if (key.PotentialConflict) warnings.Add($"{key.Label}: {key.Warning ?? "This key may conflict with another game or overlay binding."}");
        }
        if (issues.Count != 0) return new("", issues.ToImmutable(), warnings.ToImmutable());
        var lines = list.OrderBy(a => a.ProfileId).Select(a => $"setbind {Keys[a.KeyId].ConsoleToken} \"exec {FileName(a.ProfileId)}\"");
        return new(string.Join("\r\n", lines) + "\r\n", [], warnings.ToImmutable());
    }

    private static string ProfileLabel(ProfileId id) => id == ProfileId.Vanilla ? "Vanilla" : $"Profile {(int)id}";
    private static string FileName(ProfileId id) => id == ProfileId.Vanilla ? "SCS_Vanilla.txt" : $"SCS_Profile{(int)id}.txt";
}

public sealed record HotkeySaveResult(HotkeyCompileResult Compilation, FileWriteResult? File)
{
    public bool Success => Compilation.Success && File is { Status: WriteStatus.Written };
}

public sealed class HotkeyWriter(HotkeyCompiler compiler, AtomicFileWriter? writer = null)
{
    private readonly AtomicFileWriter writer = writer ?? new();
    public HotkeySaveResult Save(IEnumerable<HotkeyAssignment> assignments, string binariesPath)
    {
        var compiled = compiler.Compile(assignments);
        if (!compiled.Success) return new(compiled, null);
        var verified = new InstallationService().Verify(binariesPath);
        if (!verified.Ready) return new(compiled, new(WriteStatus.Failed, binariesPath, verified.Failure ?? new(FileFailureKind.NotFound, "Choose an installation first.")));
        return new(compiled, writer.WriteConsoleText(Path.Combine(verified.BinariesPath!, "SCS_Bindings.txt"), compiled.Text));
    }
}
