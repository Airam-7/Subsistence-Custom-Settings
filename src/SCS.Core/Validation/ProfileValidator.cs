using System.Collections.Immutable;

namespace SCS.Core;

public static class ProfileValidator
{
    public static ImmutableArray<ValidationIssue> Validate(SettingsCatalog catalog, ProfileRecord profile)
    {
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        if (profile.CatalogVersion != catalog.Version) issues.Add(new("CatalogVersion", null, "Migrate this profile to the current catalog before compiling."));
        if (profile.DisplayName.Length is < 1 or > 32) issues.Add(new("DisplayName", null, "Profile names must contain 1–32 characters."));
        foreach (var key in profile.Values.Keys)
            if (!catalog.ById.TryGetValue(key, out var s) || s.SupportStatus != SupportStatus.Supported)
                issues.Add(new("UnexpectedSetting", key, "The profile contains a setting outside the supported catalog."));
        foreach (var s in catalog.Supported)
        {
            if (!profile.Values.TryGetValue(s.Id, out var value)) issues.Add(new("MissingSetting", s.Id, "All supported settings must be present, including Vanilla values."));
            else if (ValueValidator.Validate(s, value) is { } issue) issues.Add(issue);
            else if (profile.IsReadOnly && value != s.DefaultValue) issues.Add(new("ReadOnly", s.Id, "Vanilla must contain the catalog defaults."));
        }
        return issues.ToImmutable();
    }
}
