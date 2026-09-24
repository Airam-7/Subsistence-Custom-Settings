using System.Collections.Immutable;

namespace SCS.Core;

public sealed record MigrationResult(ProfileRecord? Profile, ImmutableArray<string> Added, ImmutableArray<string> Removed,
    ImmutableArray<ValidationIssue> Issues, bool RequiresSave)
{
    public bool Success => Profile is not null && Issues.IsEmpty;
}

public static class ProfileMigrator
{
    public static MigrationResult Migrate(ProfileRecord old, SettingsCatalog target)
    {
        if (!Version.TryParse(old.CatalogVersion, out var sourceVersion) || !Version.TryParse(target.Version, out var targetVersion))
            return new(null, [], [], [new("MigrationVersion", null, "Migration requires recognized numeric catalog versions.")], false);
        if (sourceVersion > targetVersion)
            return new(null, [], [], [new("MigrationDowngrade", null, "Automatic catalog downgrades are not supported.")], false);
        if (old.IsReadOnly)
            return new(ProfileRecord.CreateDefault(ProfileId.Vanilla, target), [], [], [], old.CatalogVersion != target.Version);
        var desired = target.DefaultValues();
        var added = desired.Keys.Except(old.Values.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var removed = old.Values.Keys.Except(desired.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        var values = desired.ToBuilder();
        foreach (var pair in old.Values.Where(p => desired.ContainsKey(p.Key))) values[pair.Key] = pair.Value;
        if (old.CatalogVersion == "2.0.0" && target.Version == "2.0.1" && values.TryGetValue("RefiningSpeed", out var refining)
            && refining.Number is > 50 and <= 100 && refining.Number == decimal.Truncate(refining.Number.Value))
            values["RefiningSpeed"] = SettingValue.Numeric(50); // Both versions generate exactly 1 second.
        var migrated = ProfileRecord.CreateCustom(old.Id, old.DisplayName, target.Version, values);
        var issues = new ProfileCompiler(target).Compile(migrated).Issues;
        if (!issues.IsEmpty) return new(null, added, removed, issues, false);
        // A migration is a proposal in memory. The original record/file is not changed.
        return new(migrated, added, removed, [], !migrated.SameContent(old));
    }
}
