using System.Globalization;
using System.Text.RegularExpressions;

namespace SCS.Core;

public static partial class ValueValidator
{
    [GeneratedRegex(@"^(?:\d+(?:[.,]\d*)?|[.,]\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericText();

    public static ValidationIssue? Validate(SettingDefinition definition, SettingValue value)
    {
        ValidationIssue Issue(string code, string text) => new(code, definition.Id, text);
        if (!value.IsValid) return Issue("InvalidType", "A setting must contain exactly one number or boolean.");
        switch (definition.Input)
        {
            case ToggleInput:
                return value.Boolean.HasValue ? null : Issue("InvalidType", "A boolean is required.");
            case NumberInput n:
                if (value.Number is not { } actual || actual < n.Min || actual > n.Max || (actual - n.Min) % n.Step != 0)
                    return Issue("OutOfRange", FormattableString.Invariant($"Enter {n.Min}–{n.Max} {n.Unit}, in steps of {n.Step}."));
                return null;
            case ChoiceInput choice:
                return value.Number.HasValue && choice.Options.Contains(value.Number.Value)
                    ? null : Issue("InvalidChoice", "Choose one of the allowed values.");
            case MultiplierInput multiplier:
                if (!value.Number.HasValue) return Issue("InvalidType", "A number is required.");
                var number = value.Number.Value;
                if (number < multiplier.Min || number > multiplier.Max)
                    return Issue("OutOfRange", FormattableString.Invariant($"Enter a value from {multiplier.Min} to {multiplier.Max}."));
                if (multiplier.Step <= 0 || (number - multiplier.Min) % multiplier.Step != 0)
                    return Issue("InvalidStep", FormattableString.Invariant($"Use steps of {multiplier.Step}."));
                return null;
            default:
                return Issue("InvalidInput", "Unknown input definition.");
        }
    }

    public static bool TryParse(SettingDefinition definition, string raw, out SettingValue value, out ValidationIssue? issue)
    {
        value = default;
        var text = raw.Trim();
        if (definition.Input is ToggleInput)
        {
            if (bool.TryParse(text, out var boolean)) value = SettingValue.Logical(boolean);
        }
        else if (NumericText().IsMatch(text) && decimal.TryParse(text.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            value = SettingValue.Numeric(number);
        issue = value.IsValid ? Validate(definition, value)
            : new ValidationIssue("InvalidText", definition.Id, definition.Input is ToggleInput ? "Enter True or False." : "Enter a number without grouping separators or an exponent.");
        return issue is null;
    }
}
