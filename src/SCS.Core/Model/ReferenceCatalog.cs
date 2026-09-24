using System.Collections.Immutable;

namespace SCS.Core;

public static class ReferenceCatalog
{
    public const string WeaponUpgradeSpeed = "WeaponUpgradeSpeed";
    public const string CampfireFuelDuration = "CampfireFuelDuration";

    public static SettingsCatalog Create()
    {
        var speedInput = new MultiplierInput(.1m, 100m, .1m, [.1m, .5m, 1m, 2m, 5m, 10m, 25m, 50m, 100m]);
        return new("1.0.0", [
            new(WeaponUpgradeSpeed, "Weapon upgrade speed", "Complete weapon upgrades faster.", ["Buildables", "WeaponsBench"],
                SupportStatus.Supported, speedInput, SettingValue.Numeric(1),
                new ConsolePropertyBackend([new("ColdAccessModule_WeaponsBench", "UpgradeTime", SettingValue.Numeric(20),
                    Conversion.DivideByMultiplier, OutputKind.Decimal, .2m, 200m, 6, Rounding.NearestAwayFromZero, "s / upgrade",
                    OutputDescription: "Time required to complete one weapon upgrade.")]),
                [new("Research evidence — master document §12.13", "UpgradeTime = 20; consumed by the upgrade timer."),
                 new("Runtime validation — reported in master document §§25.1–25.2 (version 21/09/2026)", "GetAll resolved the class/property and baseline; Set changed the value during an active session and GetAll verified it.")]),
            new(CampfireFuelDuration, "Fuel duration", "Make the same amount of fuel last longer.", ["Buildables", "Campfire"],
                SupportStatus.Supported, speedInput, SettingValue.Numeric(1),
                new ConsolePropertyBackend([new("ColdAccessModule_Campfire", "FuelConsumptionPerSec", SettingValue.Numeric(.14m),
                    Conversion.DivideByMultiplier, OutputKind.Decimal, .0014m, 1.4m, 6, Rounding.NearestAwayFromZero, "fuel / s",
                    OutputDescription: "Fuel consumed per second; a larger duration multiplier reduces this value.")]),
                [new("Research evidence — master document §12.14", "FuelConsumptionPerSec = 0.14; fuel decreases by this value multiplied by BurnInterval."),
                 new("Runtime validation — reported in master document §26.3 (version 21/09/2026)", "Sequential Set commands in an Exec file changed FuelConsumptionPerSec; final value 0.14 was verified.")]),
            Candidate("RefinerySpeed", "Refining speed", "Refinery", new MultiplierInput(1, 100, .1m, [1, 2, 5, 10, 25, 50, 100]),
                SettingValue.Numeric(1), "ColdAccessModule_Refinery", "LaserCookInterval", SettingValue.Numeric(50), Conversion.DivideByMultiplier, "s / cycle"),
            Candidate("RefineryYield", "Production yield", "Refinery", new MultiplierInput(1, 100, 1, [1, 2, 5, 10, 25, 50, 100]),
                SettingValue.Numeric(1), "ColdAccessModule_Refinery", "YeildCountPerCook", null, Conversion.MultiplyBaseline, "items / cycle"),
            Candidate("RefineryLasers", "Active lasers", "Refinery", new ChoiceInput([1, 2, 3]), null,
                "ColdAccessModule_Refinery", "LaserCount", null, Conversion.Direct, "lasers"),
            Candidate("CampfireCapacity", "Fuel capacity", "Campfire", new MultiplierInput(1, 100, 1, [1, 2, 5, 10, 25, 50, 100]),
                SettingValue.Numeric(1), "ColdAccessModule_Campfire", "MaxFuelAmount", SettingValue.Numeric(100), Conversion.MultiplyBaseline, "fuel capacity")
        ]);
    }

    private static SettingDefinition Candidate(string id, string name, string section, InputDefinition input,
        SettingValue? defaultValue, string className, string propertyName, SettingValue? baseline, Conversion conversion, string unit) =>
        new(id, name, "Developer inspection only.", ["Buildables", section], SupportStatus.Candidate, input, defaultValue,
            new ConsolePropertyBackend([new(className, propertyName, baseline, conversion, OutputKind.Decimal, null, null, null, Rounding.None, unit)]),
            [new("Research evidence — master document §§12.11–12.14, §33", "Candidate property identified; no promotion to Supported.")],
            baseline.HasValue ? "Console mapping not validated." : "Vanilla baseline and console mapping not validated.");
}
