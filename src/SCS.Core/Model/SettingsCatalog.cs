using System.Collections.Frozen;
using System.Collections.Immutable;

namespace SCS.Core;

public sealed class SettingsCatalog
{
    public string Version { get; }
    public ImmutableArray<SettingDefinition> Definitions { get; }
    public ImmutableArray<SettingDefinition> Supported { get; }
    public FrozenDictionary<string, SettingDefinition> ById { get; }

    public SettingsCatalog(string version, IEnumerable<SettingDefinition> definitions)
    {
        Version = version;
        Definitions = definitions.ToImmutableArray();
        var issues = CatalogValidator.Validate(version, Definitions);
        if (!issues.IsEmpty) throw new CatalogException(issues);
        Supported = Definitions.Where(s => s.SupportStatus == SupportStatus.Supported)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToImmutableArray();
        ById = Definitions.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);
    }

    public ImmutableDictionary<string, SettingValue> DefaultValues() => Supported
        .ToImmutableDictionary(s => s.Id, s => s.DefaultValue!.Value, StringComparer.Ordinal);

    public IEnumerable<SettingDefinition> Visible(bool developerMode) => Definitions.Where(s =>
        s.SupportStatus == SupportStatus.Supported || (developerMode && s.SupportStatus == SupportStatus.Candidate));
}
