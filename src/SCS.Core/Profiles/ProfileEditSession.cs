using System.Collections.Immutable;

namespace SCS.Core;

public enum SaveState { Clean, Dirty, Saving, SaveFailed }

public sealed class ProfileEditSession(SettingsCatalog catalog, ProfileRecord saved)
{
    private ImmutableDictionary<string, string> rawInputs = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, ValidationIssue> inputErrors = ImmutableDictionary<string, ValidationIssue>.Empty;
    public SettingsCatalog Catalog { get; } = catalog;
    public ProfileRecord Saved { get; private set; } = saved;
    public ProfileRecord Draft { get; private set; } = saved;
    public ImmutableDictionary<string, string> RawInputs => rawInputs;
    public ImmutableArray<ValidationIssue> ValidationErrors => ProfileValidator.Validate(Catalog, Draft).AddRange(inputErrors.Values);
    public bool IsDirty => !Draft.SameContent(Saved) || !inputErrors.IsEmpty;
    public SaveState State { get; private set; } = SaveState.Clean;
    public string? LastSaveError { get; private set; }

    private void EnsureEditable()
    {
        Draft.EnsureEditable();
        if (State == SaveState.Saving) throw new InvalidOperationException("Cannot edit while a profile is being saved.");
    }

    private SettingDefinition EditableSetting(string id)
    {
        EnsureEditable();
        if (!Catalog.ById.TryGetValue(id, out var s) || s.SupportStatus != SupportStatus.Supported)
            throw new InvalidOperationException("Only supported settings can be edited.");
        return s;
    }

    private void Changed() { State = IsDirty ? SaveState.Dirty : SaveState.Clean; LastSaveError = null; }

    public void SetRawInput(string settingId, string raw)
    {
        var s = EditableSetting(settingId);
        rawInputs = rawInputs.SetItem(settingId, raw);
        if (ValueValidator.TryParse(s, raw, out var value, out var issue))
        { Draft = Draft.WithValues(Draft.Values.SetItem(settingId, value)); inputErrors = inputErrors.Remove(settingId); }
        else inputErrors = inputErrors.SetItem(settingId, issue!);
        Changed();
    }

    public void Rename(string name) { EnsureEditable(); Draft = Draft.Rename(name); Changed(); }

    public void ResetSetting(string id)
    {
        var s = EditableSetting(id);
        Draft = Draft.WithValues(Draft.Values.SetItem(id, s.DefaultValue!.Value));
        rawInputs = rawInputs.Remove(id); inputErrors = inputErrors.Remove(id); Changed();
    }

    public void ResetCategory(params string[] path)
    {
        EnsureEditable();
        if (path.Length == 0) throw new ArgumentException("A category path is required; use ResetProfile for all settings.");
        foreach (var s in Catalog.Supported.Where(s => s.CategoryPath.Length >= path.Length && s.CategoryPath.Take(path.Length).SequenceEqual(path))) ResetSetting(s.Id);
    }

    public void ResetProfile(bool confirmed)
    {
        EnsureEditable();
        if (!confirmed) return;
        Draft = Draft.WithValues(Catalog.DefaultValues()); rawInputs = rawInputs.Clear(); inputErrors = inputErrors.Clear(); Changed();
    }

    internal void BeginSave() { EnsureEditable(); State = SaveState.Saving; LastSaveError = null; }
    internal void SaveSucceeded() { Saved = Draft; rawInputs = rawInputs.Clear(); inputErrors = inputErrors.Clear(); State = SaveState.Clean; LastSaveError = null; }
    internal void SaveFailed(string error) { State = SaveState.SaveFailed; LastSaveError = error; }
}

public sealed class ProfileWorkspace
{
    public ImmutableDictionary<ProfileId, ProfileEditSession> Sessions { get; }
    public ProfileId SelectedProfileId { get; private set; } = ProfileId.Profile1;
    public ProfileEditSession Current => Sessions[SelectedProfileId];
    public ProfileWorkspace(SettingsCatalog catalog) => Sessions = Enum.GetValues<ProfileId>()
        .ToImmutableDictionary(id => id, id => new ProfileEditSession(catalog, ProfileRecord.CreateDefault(id, catalog)));
    public void Select(ProfileId id) { if (!Sessions.ContainsKey(id)) throw new ArgumentOutOfRangeException(nameof(id)); SelectedProfileId = id; }
}
