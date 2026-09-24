using System.Collections.Immutable;

namespace SCS.Core;

public enum HotkeySetupState { NotGenerated, Current, Stale, WriteFailed }

public sealed class HotkeyEditSession(HotkeyCompiler compiler)
{
    public ImmutableArray<HotkeyAssignment> SavedAssignments { get; private set; } = HotkeyCompiler.Defaults;
    public ImmutableArray<HotkeyAssignment> DraftAssignments { get; private set; } = HotkeyCompiler.Defaults;
    public HotkeySetupState SetupFileState { get; private set; } = HotkeySetupState.NotGenerated;
    public string? GeneratedBinariesPath { get; private set; }
    public bool IsDirty => !DraftAssignments.SequenceEqual(SavedAssignments);
    public HotkeyCompileResult Preview => compiler.Compile(DraftAssignments);

    public static HotkeyEditSession Restore(HotkeyCompiler compiler, IEnumerable<HotkeyAssignment> assignments, string? generatedPath)
    {
        var saved = assignments.OrderBy(a => a.ProfileId).ToImmutableArray();
        if (!compiler.Compile(saved).Success) throw new ArgumentException("Invalid saved hotkey assignments.");
        return new(compiler)
        {
            SavedAssignments = saved, DraftAssignments = saved,
            GeneratedBinariesPath = generatedPath,
            // Persisted metadata cannot prove that the setup file is still current on disk.
            SetupFileState = generatedPath is null ? HotkeySetupState.NotGenerated : HotkeySetupState.Stale
        };
    }

    public void SetKey(ProfileId id, string keyId)
    {
        if (!Enum.IsDefined(id)) throw new ArgumentOutOfRangeException(nameof(id));
        DraftAssignments = DraftAssignments.Select(a => a.ProfileId == id ? new HotkeyAssignment(id, keyId) : a).ToImmutableArray();
    }

    public void InstallationChanged(string newBinariesPath)
    {
        if (GeneratedBinariesPath is not null && !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(newBinariesPath)), GeneratedBinariesPath, StringComparison.OrdinalIgnoreCase))
            SetupFileState = HotkeySetupState.Stale;
    }

    public HotkeySaveResult Generate(string binariesPath, AtomicFileWriter? writer = null)
    {
        var result = new HotkeyWriter(compiler, writer).Save(DraftAssignments, binariesPath);
        if (result.Success)
        {
            SavedAssignments = DraftAssignments;
            GeneratedBinariesPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(binariesPath));
            SetupFileState = HotkeySetupState.Current;
        }
        else if (result.File is { Success: false }) SetupFileState = HotkeySetupState.WriteFailed;
        return result;
    }
}
