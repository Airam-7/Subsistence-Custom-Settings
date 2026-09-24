using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace SCS.Core;

public static partial class CatalogValidator
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    internal static partial Regex Identifier();

    public static ImmutableArray<ValidationIssue> Validate(string version, ImmutableArray<SettingDefinition> definitions)
    {
        var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        void Add(string code, string? id, string message) => issues.Add(new(code, id, message));
        if (string.IsNullOrWhiteSpace(version)) Add("CatalogVersion", null, "The catalog needs a version.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in definitions)
        {
            var start = issues.Count;
            if (string.IsNullOrWhiteSpace(s.Id) || !Identifier().IsMatch(s.Id) || !ids.Add(s.Id)) Add("SettingId", s.Id, "Setting IDs must be valid and unique.");
            if (string.IsNullOrWhiteSpace(s.Name) || s.CategoryPath.IsDefaultOrEmpty || s.CategoryPath.Any(string.IsNullOrWhiteSpace)) Add("Presentation", s.Id, "A name and category path are required.");
            if (!Enum.IsDefined(s.SupportStatus)) Add("Status", s.Id, "Unknown support status.");
            if (s.SupportStatus != SupportStatus.Supported && string.IsNullOrWhiteSpace(s.UnavailableReason)) Add("Reason", s.Id, "Unavailable settings require a reason.");
            switch (s.Input)
            {
                case MultiplierInput m:
                    if (m.Min <= 0 || m.Max < m.Min || m.Step <= 0 || m.Anchors.IsDefaultOrEmpty || !m.Anchors.Contains(1m)
                        || m.Anchors.Any(a => a < m.Min || a > m.Max) || !m.Anchors.SequenceEqual(m.Anchors.Distinct().Order()))
                        Add("InputRange", s.Id, "Multiplier bounds, step and anchors are invalid.");
                    if (s.DefaultValue != SettingValue.Numeric(1)) Add("Default", s.Id, "Multiplier default must be ×1.");
                    break;
                case ChoiceInput c:
                    if (c.Options.IsDefaultOrEmpty || c.Options.Distinct().Count() != c.Options.Length) Add("Choices", s.Id, "Choice values must be nonempty and unique.");
                    break;
                case ToggleInput: break;
                case NumberInput n:
                    if (n.Min < 0 || n.Max < n.Min || n.Step <= 0) Add("NumberRange", s.Id, "Invalid numeric bounds.");
                    break;
                default: Add("Input", s.Id, "Unknown input type."); break;
            }
            if (s.SupportStatus == SupportStatus.Supported)
            {
                if (s.Backend is not ConsolePropertyBackend) Add("Backend", s.Id, "Supported v1 settings require a console backend.");
                if (s.DefaultValue is not { IsValid: true }) Add("Default", s.Id, "Supported settings require a valid default.");
                else if (issues.Count == start && ValueValidator.Validate(s, s.DefaultValue.Value) is { } error) issues.Add(error);
                if (s.Evidence.IsDefaultOrEmpty || s.Evidence.Any(e => string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.Finding))) Add("Evidence", s.Id, "Supported settings need recorded evidence.");
            }
            if (s.Backend is not ConsolePropertyBackend console) continue;
            if (console.Targets.IsDefaultOrEmpty) { Add("Targets", s.Id, "A console backend needs targets."); continue; }
            foreach (var target in console.Targets)
            {
                if (string.IsNullOrWhiteSpace(target.ClassName) || string.IsNullOrWhiteSpace(target.PropertyName)
                    || !Identifier().IsMatch(target.ClassName) || !Identifier().IsMatch(target.PropertyName)) Add("ConsoleIdentifier", s.Id, "Console identifiers contain invalid characters.");
                if (!targets.Add(target.Identity)) Add("DuplicateTarget", s.Id, "Duplicate console target: " + target.Identity);
                if (!Enum.IsDefined(target.Conversion) || !Enum.IsDefined(target.OutputKind) || !Enum.IsDefined(target.Rounding)) Add("OutputDefinition", s.Id, "Unknown output conversion or format.");
                if (s.SupportStatus != SupportStatus.Supported) continue;
                if (target.OutputKind == OutputKind.LootTable)
                {
                    if (target.LootItems.IsDefaultOrEmpty || target.LootItems.Any(e => !System.Text.RegularExpressions.Regex.IsMatch(e.ItemClass, @"^ColdGame\.[A-Za-z_][A-Za-z0-9_]*$") || e.Chance < 0 || e.Chance > 100 || e.CountMin < 0 || e.CountMax > 255 || e.CountMin > e.CountMax)) Add("LootBaseline", s.Id, "Invalid original loot table.");
                    if (target.Conversion != Conversion.MultiplyBaseline) Add("LootConversion", s.Id, "Loot tables scale original quantities only.");
                    continue;
                }
                if (target.VanillaValue is not { IsValid: true }) Add("Baseline", s.Id, "Supported targets need a known baseline.");
                if (target.OutputKind != OutputKind.Boolean && (!target.OutputMin.HasValue || !target.OutputMax.HasValue || target.OutputMin > target.OutputMax)) Add("OutputBounds", s.Id, "Numeric output requires valid bounds.");
                if (target.OutputKind == OutputKind.Decimal && target.DecimalPlaces is not (>= 0 and <= 28)) Add("OutputPrecision", s.Id, "Decimal precision must be 0–28.");
                if (issues.Count != start) continue;
                try
                {
                    var value = ValueConverter.Convert(s.Id, target, s.DefaultValue!.Value);
                    if (value.Value != target.VanillaValue) Add("VanillaMismatch", s.Id, "The default must reproduce the exact Vanilla baseline.");
                }
                catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException)
                { Add("DefaultConversion", s.Id, ex.Message); }
            }
        }
        return issues.ToImmutable();
    }
}
