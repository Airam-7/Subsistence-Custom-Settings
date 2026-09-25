using System.Collections.Immutable;

namespace SCS.Core;

public sealed class IniHotkeyEditSession
{
    public ImmutableArray<HotkeyAssignment> SavedAssignments { get; private set; } = Unassigned;
    public ImmutableArray<HotkeyAssignment> DraftAssignments { get; private set; } = Unassigned;
    public static ImmutableArray<HotkeyAssignment> Unassigned => Enum.GetValues<ProfileId>().Select(id => new HotkeyAssignment(id, "")).ToImmutableArray();
    public void ClearSavedAssignments() { SavedAssignments = Unassigned; DraftAssignments = Unassigned; GeneratedBinariesPath = null; }
    public string? GeneratedBinariesPath { get; private set; }
    public bool IsDirty => !SavedAssignments.SequenceEqual(DraftAssignments);
    public void SetKey(ProfileId id, string key) => DraftAssignments = DraftAssignments.Select(a => a.ProfileId == id ? new HotkeyAssignment(id, key) : a).ToImmutableArray();
    public void MarkPreferencesSaved() { SavedAssignments = DraftAssignments; }
    public void MarkInstalled(string binaries) { SavedAssignments = DraftAssignments; GeneratedBinariesPath = binaries; }
    public static IniHotkeyEditSession Restore(IEnumerable<HotkeyAssignment> keys, string? location)
    {
        // Recover unusable legacy assignments without preventing the workspace from opening.
        var source = keys.ToArray(); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var array = Enum.GetValues<ProfileId>().Select(id => {
            var matches = source.Where(k => k.ProfileId == id).ToArray();
            var key = matches.Length == 1 ? matches[0] : new(id, "");
            return !key.Control && !key.Shift && !key.Alt && IniHotkeys.AllowedKeys.Contains(key.KeyId) && used.Add(key.KeyId) ? key : new(id, "");
        }).ToImmutableArray();
        return new() { SavedAssignments = array, DraftAssignments = array, GeneratedBinariesPath = location };
    }
    public bool Installed(InputIniDocument document)
    {
        if (!IniHotkeys.Validate(SavedAssignments, document).IsEmpty) return false;
        return document.HasCompleteLayout && SavedAssignments.All(a => a.KeyId.Equals(document.InstalledKey(a.ProfileId), StringComparison.OrdinalIgnoreCase));
    }
}
