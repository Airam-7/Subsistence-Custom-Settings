using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace SCS.Core;

public static class SupportedCatalog
{
    public sealed record BaselineRow(string Class, string Property, string Original, int Paragraph)
    {
        public decimal Baseline { get; init; }
        public LootEntry[]? Loot { get; init; }
    }
    private static ImmutableArray<BaselineRow> Read()
    {
        var assembly = typeof(SupportedCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("catalog-baselines.json", StringComparison.Ordinal)))!;
        return JsonSerializer.Deserialize<BaselineRow[]>(stream)!.ToImmutableArray();
    }
    public static SettingsCatalog Create()
    {
        var rows = Read(); var definitions = new List<SettingDefinition>();
        var evidence = ImmutableArray.Create(new EvidenceRecord("Corrected catalogue 23/09/2026, sections 2 and 6", "Gameplay validation reported by the user; original class baselines preserved. Full ranges and multiplayer are not independently certified."));
        ConsoleTarget Target(BaselineRow row, Conversion conversion, string unit, bool integer = false, bool minimumOne = false)
        {
            if (row.Loot is not null) return new(row.Class, row.Property, null, Conversion.MultiplyBaseline, OutputKind.LootTable, null, null, null, Rounding.None, "items", LootItems: row.Loot.ToImmutableArray(), EmissionOrder: row.Paragraph);
            return new(row.Class, row.Property, SettingValue.Numeric(row.Baseline), conversion, integer ? OutputKind.Integer : OutputKind.Decimal,
                row.Baseline < 0 ? row.Baseline * 100 : 0, row.Baseline < 0 ? 0 : Math.Max(row.Baseline * 1000, 20000), integer ? 0 : 6,
                Rounding.NearestAwayFromZero, unit, EmissionOrder: row.Paragraph, MinimumAfterRounding: minimumOne ? 1 : null);
        }
        void Add(string id, string name, string category, string description, IEnumerable<BaselineRow> targets, Conversion conversion = Conversion.MultiplyBaseline,
            decimal min = 1, decimal max = 100, decimal step = 1, string unit = "", bool integer = false, bool minimumOne = false, InputDefinition? input = null, SettingValue? defaultValue = null)
        {
            var anchors = new decimal[] { .1m, .5m, 1, 2, 5, 10, 25, 50, 100 }.Where(a => a >= min && a <= max).ToImmutableArray();
            definitions.Add(new(id, name, description, [category], SupportStatus.Supported, input ?? new MultiplierInput(min, max, step, anchors),
                defaultValue ?? SettingValue.Numeric(1), new ConsolePropertyBackend(targets.Select(r => Target(r, conversion, unit, integer, minimumOne)).ToImmutableArray()), evidence));
        }
        IEnumerable<BaselineRow> Find(string cls, params string[] properties) => rows.Where(r => r.Class == cls && properties.Contains(r.Property));
        Add("WoodYield", "Wood yield", "Gathering", "Logs received per chopping event. Range ×1–×100; existing loaded logs may also change.", Find("ColdInventoryItem_Log", "Count"), unit: "logs / event", integer: true);
        var crops = new[] { "Berries", "Carrot", "Cotton", "Onion", "Potato", "Strawberry", "Wheat" }.Select(n => "ColdLootVegetation_" + n).ToHashSet();
        Add("VegetationYield", "Wild gatherables", "Gathering", "More items from wild pickups, excluding crops and berries. Preserves item selection and chances; stack limits still apply.", rows.Where(r => r.Class.StartsWith("ColdLootVegetation_") && !crops.Contains(r.Class)));
        Add("CropHarvestYield", "Crop harvest quantity", "Farming", "Scales the documented crop and seed harvest tables, including berries. Does not change growth, water or fertilizer.", rows.Where(r => crops.Contains(r.Class)));
        Add("CrateYield", "Loot crate quantity", "Gathering", "Quantity across each crate's slots. Limited to ×1–×5 while stack limits are validated; duplicate entries and chances are preserved.", rows.Where(r => r.Class.StartsWith("ColdLootContainer")), max: 5);
        Add("ButcherYield", "Butchering yield", "Animals", "Twelve documented species; corrected pork and fat quantities for boar. Range ×1–×5 to respect quantity limits.", rows.Where(r => r.Loot is not null && !r.Class.StartsWith("ColdLoot")), max: 5);
        Add("PassiveWildlife", "Passive unless provoked", "Animals", "Reduces sight and hearing together. Animals can still react to direct aggression. OFF restores each species' original perception.", rows.Where(r => r.Property is "SightRadius" or "HearingThreshold"), Conversion.PassiveWhenEnabled, unit: "perception units", integer: true, input: new ToggleInput(), defaultValue: SettingValue.Logical(false));
        Add("CraftSpeed", "Crafting speed", "Crafting", "Applies proportionally to inventory items and buildables. Whole seconds, minimum 1 s; faster multipliers saturate at that minimum.", rows.Where(r => r.Property == "CraftTime"), Conversion.DivideByMultiplier, unit: "s / craft", integer: true, minimumOne: true);
        Add("TrapAttemptSpeed", "Capture attempt speed", "Traps", "Shared timing for baited land and fish traps. Increases attempt frequency, not guaranteed catches.", rows.Where(r => r.Property.StartsWith("DelayUntilAttemptTrap_")), Conversion.DivideByMultiplier, unit: "s", integer: true, minimumOne: true);
        Add("TrapAttractionRadius", "Animal attraction radius", "Traps", "Search radius for small animals. Engine units, not metres. The tested upper bound is 20,000.", Find("ColdAccessModule_SmallAnimalTrap", "RadiusToAttractAnimals"), Conversion.Direct, unit: "engine units", integer: true, input: new NumberInput(6000, 20000, 1, "engine units"), defaultValue: SettingValue.Numeric(6000));
        Add("FishTrapCompetitionRadius", "Fish trap competition radius", "Traps", "Nearby traps with catches can penalize this trap. A smaller radius reduces that competition; it is not an attraction radius.", Find("ColdAccessModule_FishTrap", "RadiusToCheckForNearbyFishTraps"), Conversion.Direct, unit: "engine units", integer: true, input: new NumberInput(1, 1000, 1, "engine units"), defaultValue: SettingValue.Numeric(1000));
        Add(ReferenceCatalog.WeaponUpgradeSpeed, "Weapon upgrade speed", "Weapons Bench", "Complete upgrades faster without changing upgrade effects or material costs.", Find("ColdAccessModule_WeaponsBench", "UpgradeTime"), Conversion.DivideByMultiplier, .1m, 100, .1m, "s / upgrade");
        Add(ReferenceCatalog.CampfireFuelDuration, "Fuel duration", "Campfire", "Make the same amount of fuel last longer; cooking behavior stays unchanged.", Find("ColdAccessModule_Campfire", "FuelConsumptionPerSec"), Conversion.DivideByMultiplier, .1m, 100, .1m, "fuel / s");
        Add("BcuPower", "Passive power production", "BCU", "Preserves the master BCU conditions and the original 500 power production cap.", Find("ColdBuildable_BaseCommandUnit", "ProducePowerPerSec"), min: .1m, step: .1m, unit: "power / s");
        Add("BcuMass", "Passive mass production", "BCU", "Preserves the master BCU conditions and the original 100 mass production cap.", Find("ColdBuildable_BaseCommandUnit", "ProduceMassPerSec"), min: .1m, step: .1m, unit: "mass / s");
        Add("GeneratorPower", "Power output", "Power Generator", "Scales normal production while preserving the game's Overdrive relationship.", Find("ColdBuildable_PowerGenerator", "PowerOutputPerSec"), min: .1m, step: .1m, unit: "power / s");
        Add("GeneratorFuelDuration", "Fuel duration", "Power Generator", "A higher multiplier reduces fuel consumption. Overdrive behavior is preserved.", Find("ColdAccessModule_PowerGenerator", "FuelConsumptionPerSec"), Conversion.DivideByMultiplier, .1m, 100, .1m, "fuel / s");
        Add("MassProduction", "Mass production", "Mass Fabricator", "Scales normal and Overdrive output together, preserving their 1:2 ratio.", Find("ColdBuildable_MassFabricator", "MassGeneratedPerSec", "MassGeneratedPerSecOverdrive"), min: .1m, step: .1m, unit: "mass / s");
        Add("MassPowerConsumption", "Power consumption", "Mass Fabricator", "×0.1 uses 10% of Vanilla power; ×10 uses ten times as much. Preserves the normal/Overdrive ratio.", Find("ColdBuildable_MassFabricator", "PowerUsagePerSec", "PowerUsagePerSecOverdrive"), min: .1m, step: .1m, unit: "power / s");
        Add("RefiningSpeed", "Refining speed", "Refinery", "Faster refining cycles, in whole seconds. Minimum 1 s; effective speed tops out at ×50.", Find("ColdAccessModule_Refinery", "LaserCookInterval"), Conversion.DivideByMultiplier, 1, 50, 1, "s / cycle", true, true);
        Add("RefineryPower", "Power consumption per laser", "Refinery", "×0.1 uses 10% of Vanilla power. Existing efficiency upgrades are preserved.", Find("ColdBuildable_Refinery", "PowerDrainPerLaserPerSec"), min: .1m, step: .1m, unit: "power / laser / s");
        var catalog = new SettingsCatalog("2.0.1", definitions);
        if (catalog.Supported.Sum(s => ((ConsolePropertyBackend)s.Backend).Targets.Length) != rows.Length) throw new InvalidOperationException("The corrected baseline catalogue is not completely owned.");
        return catalog;
    }
}
