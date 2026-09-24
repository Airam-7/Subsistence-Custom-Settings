using System.Globalization;

namespace SCS.Core;

public sealed record CompiledOutput(string SettingId, ConsoleTarget Target, SettingValue Value, string FormattedValue)
{
    public string Command => $"set {Target.ClassName} {Target.PropertyName} {FormattedValue}";
    public string Label => Target.OutputLabel ?? "Profile output";
    public string Unit => Target.OutputUnit;
    public string? Description => Target.OutputDescription;
    public bool Saturated { get; init; }
}

public static class ValueConverter
{
    public static CompiledOutput Convert(string settingId, ConsoleTarget target, SettingValue input)
    {
        if (target.OutputKind == OutputKind.LootTable)
        {
            if (input.Number is not { } multiplier || multiplier < 1 || decimal.Truncate(multiplier) != multiplier || target.LootItems.IsDefaultOrEmpty)
                throw new InvalidOperationException("Loot quantities require a positive integer multiplier and original table.");
            var entries = target.LootItems.Select(item =>
            {
                var min = checked(item.CountMin * multiplier); var max = checked(item.CountMax * multiplier);
                if (min < 0 || max > 255 || min > max) throw new InvalidOperationException("Loot quantities exceed the byte limit (255). Reduce the multiplier.");
                return $"(ItemClass=Class'{item.ItemClass}',Chance={item.Chance},countMin={min.ToString(CultureInfo.InvariantCulture)},countMax={max.ToString(CultureInfo.InvariantCulture)})";
            });
            return new(settingId, target, input, "(" + string.Join(",", entries) + ")");
        }
        if (!input.IsValid || target.VanillaValue is not { IsValid: true })
            throw new InvalidOperationException("Input or Vanilla baseline is missing.");
        if (target.OutputKind == OutputKind.Boolean)
        {
            if (target.Conversion != Conversion.Direct || !input.Boolean.HasValue)
                throw new InvalidOperationException("Boolean output requires a direct boolean input.");
            return new(settingId, target, input, input.Format());
        }
        if ((!input.Number.HasValue && target.Conversion != Conversion.PassiveWhenEnabled) || !target.VanillaValue.Value.Number.HasValue)
            throw new InvalidOperationException("Numeric input and baseline are required.");
        var baseline = target.VanillaValue.Value.Number.Value;
        var value = target.Conversion switch
        {
            Conversion.PassiveWhenEnabled when input.Boolean.HasValue => input.Boolean.Value ? 1 : baseline,
            Conversion.MultiplyBaseline when input.Number.HasValue => checked(baseline * input.Number.Value),
            Conversion.DivideByMultiplier when input.Number.HasValue && input.Number.Value != 0 => baseline / input.Number.Value,
            Conversion.Direct when input.Number.HasValue => input.Number.Value,
            _ => throw new InvalidOperationException("Invalid conversion or division by zero.")
        };
        var places = target.OutputKind == OutputKind.Integer ? 0 : target.DecimalPlaces!.Value;
        var rounded = decimal.Round(value, places, target.Rounding switch
        {
            Rounding.Floor => MidpointRounding.ToNegativeInfinity,
            Rounding.Ceiling => MidpointRounding.ToPositiveInfinity,
            Rounding.NearestAwayFromZero => MidpointRounding.AwayFromZero,
            _ => MidpointRounding.ToZero
        });
        if (target.Rounding == Rounding.None && rounded != value)
            throw new InvalidOperationException("The output cannot be represented without rounding.");
        var saturated = target.MinimumAfterRounding.HasValue && rounded < target.MinimumAfterRounding.Value;
        if (saturated) rounded = target.MinimumAfterRounding!.Value;
        if (rounded < target.OutputMin || rounded > target.OutputMax)
            throw new InvalidOperationException("The generated output is outside its declared limits.");
        var formatted = rounded.ToString("0" + (places > 0 ? "." + new string('#', places) : ""), CultureInfo.InvariantCulture);
        return new(settingId, target, SettingValue.Numeric(rounded), formatted) { Saturated = saturated };
    }
}
