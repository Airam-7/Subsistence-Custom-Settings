using System.Collections.Immutable;

namespace SCS.Core;

public enum ProfileId { Profile1 = 1, Profile2, Profile3, Profile4, Vanilla }

public sealed class ProfileRecord
{
    public ProfileId Id { get; }
    public string DisplayName { get; }
    public string CatalogVersion { get; }
    public ImmutableDictionary<string, SettingValue> Values { get; }
    public bool IsReadOnly => Id == ProfileId.Vanilla;
    public string FileName => Id == ProfileId.Vanilla ? "SCS_Vanilla.txt" : $"SCS_Profile{(int)Id}.txt";

    private ProfileRecord(ProfileId id, string displayName, string catalogVersion, ImmutableDictionary<string, SettingValue> values)
    {
        if (!Enum.IsDefined(id)) throw new ArgumentOutOfRangeException(nameof(id));
        Id = id; DisplayName = displayName; CatalogVersion = catalogVersion; Values = values.WithComparers(StringComparer.Ordinal);
    }

    public static ProfileRecord CreateCustom(ProfileId id, string displayName, string version, IEnumerable<KeyValuePair<string, SettingValue>> values)
    {
        if (id == ProfileId.Vanilla) throw new InvalidOperationException("Vanilla is constructed exclusively from the catalog.");
        return new(id, displayName.Trim(), version, values.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public static ProfileRecord CreateDefault(ProfileId id, SettingsCatalog catalog) => id == ProfileId.Vanilla
        ? new(id, "Vanilla", catalog.Version, catalog.DefaultValues())
        : CreateCustom(id, id switch { ProfileId.Profile1 => "Casual", ProfileId.Profile2 => "Fast progression", ProfileId.Profile3 => "Testing", _ => "Profile 4" }, catalog.Version, catalog.DefaultValues());

    public ProfileRecord WithValues(ImmutableDictionary<string, SettingValue> values)
    {
        EnsureEditable(); return new(Id, DisplayName, CatalogVersion, values);
    }
    public ProfileRecord Rename(string name) { EnsureEditable(); return new(Id, name.Trim(), CatalogVersion, Values); }
    public void EnsureEditable() { if (IsReadOnly) throw new InvalidOperationException("Vanilla is read-only."); }
    public bool SameContent(ProfileRecord other) => Id == other.Id && DisplayName == other.DisplayName
        && CatalogVersion == other.CatalogVersion && Values.Count == other.Values.Count
        && Values.All(pair => other.Values.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
