using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;
using SCS.Core;

namespace SCS.Desktop;

public sealed record WorkspaceData(int SchemaVersion, bool DeveloperMode, string BinariesPath,
    ProfileId SelectedProfile, JsonElement[] Profiles, HotkeyAssignment[] Hotkeys, string? GeneratedBindingsPath);

public sealed class WorkspaceStore(string folder)
{
    public string Folder { get; } = folder;
    public string FilePath => Path.Combine(Folder, "workspace.json");
    public WorkspaceData? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var state = JsonSerializer.Deserialize<WorkspaceData>(File.ReadAllText(FilePath)) ?? throw new FormatException("Empty workspace.");
        if (state.SchemaVersion != 1 || state.Profiles is null || state.Hotkeys is null || state.BinariesPath is null || !Enum.IsDefined(state.SelectedProfile))
            throw new FormatException("Unsupported or incomplete workspace.");
        // Invalid or incomplete legacy hotkeys are recovered as unassigned by the edit session.
        return state;
    }

    public void Save(bool developer, string binaries, ProfileId selected, IEnumerable<ProfileRecord> profiles,
        ImmutableArray<HotkeyAssignment> keys, string? generatedPath)
    {
        Directory.CreateDirectory(Folder);
        var data = new WorkspaceData(1, developer, binaries, selected,
            profiles.Where(p => !p.IsReadOnly).OrderBy(p => p.Id).Select(p => JsonSerializer.Deserialize<JsonElement>(ProfileJson.Serialize(p))).ToArray(),
            keys.ToArray(), generatedPath);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        var result = new AtomicFileWriter().Write(FilePath, bytes);
        if (!result.Success) throw new IOException(result.Failure!.Message);
        if (!File.ReadAllBytes(FilePath).SequenceEqual(bytes)) throw new IOException("Workspace changed after saving. Retry the save.");
    }
}
