using System.Collections.Immutable;
using System.Globalization;

namespace SCS.Core;

public enum SupportStatus { Supported, Candidate, HookRequired, Unsupported }
public enum Conversion { MultiplyBaseline, DivideByMultiplier, Direct, PassiveWhenEnabled }
public enum OutputKind { Decimal, Integer, Boolean, LootTable }
public enum Rounding { None, Floor, Ceiling, NearestAwayFromZero }

public readonly record struct SettingValue(decimal? Number, bool? Boolean)
{
    public bool IsValid => Number.HasValue ^ Boolean.HasValue;
    public static SettingValue Numeric(decimal value) => new(value, null);
    public static SettingValue Logical(bool value) => new(null, value);
    public string Format() => Number?.ToString("0.############################", CultureInfo.InvariantCulture)
        ?? (Boolean.HasValue ? (Boolean.Value ? "True" : "False") : "");
}

public abstract record InputDefinition;
public sealed record MultiplierInput(decimal Min, decimal Max, decimal Step, ImmutableArray<decimal> Anchors) : InputDefinition;
public sealed record ChoiceInput(ImmutableArray<decimal> Options) : InputDefinition;
public sealed record ToggleInput : InputDefinition;
public sealed record NumberInput(decimal Min, decimal Max, decimal Step, string Unit) : InputDefinition;
public abstract record BackendDefinition;
public sealed record NoBackend : BackendDefinition;
public sealed record ConsolePropertyBackend(ImmutableArray<ConsoleTarget> Targets) : BackendDefinition;
public sealed record LootEntry(string ItemClass, int Chance, int CountMin, int CountMax);

public sealed record ConsoleTarget(
    string ClassName, string PropertyName, SettingValue? VanillaValue,
    Conversion Conversion, OutputKind OutputKind,
    decimal? OutputMin, decimal? OutputMax, int? DecimalPlaces,
    Rounding Rounding, string OutputUnit,
    string? OutputLabel = null, string? OutputDescription = null,
    ImmutableArray<LootEntry> LootItems = default, int EmissionOrder = 0, decimal? MinimumAfterRounding = null)
{
    public string Identity => ClassName + "." + PropertyName;
}

public sealed record EvidenceRecord(string Source, string Finding, string? GameBuild = null);
public sealed record SettingDefinition(
    string Id, string Name, string Description, ImmutableArray<string> CategoryPath,
    SupportStatus SupportStatus, InputDefinition Input, SettingValue? DefaultValue,
    BackendDefinition Backend, ImmutableArray<EvidenceRecord> Evidence,
    string? UnavailableReason = null);

public sealed record ValidationIssue(string Code, string? SettingId, string Message);

public sealed class CatalogException(ImmutableArray<ValidationIssue> issues)
    : ArgumentException(string.Join(Environment.NewLine, issues.Select(i => i.Message)))
{
    public ImmutableArray<ValidationIssue> Issues { get; } = issues;
}
