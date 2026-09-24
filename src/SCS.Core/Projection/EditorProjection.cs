using System.Collections.Immutable;

namespace SCS.Core;

public sealed record ApplicationPreferences(bool DeveloperMode = false);
public sealed record DeveloperTargetView(string ClassName, string PropertyName, string KnownBaseline,
    string ConversionDescription, string? GeneratedCommand);
public sealed record DeveloperSettingView(SupportStatus Status, ImmutableArray<DeveloperTargetView> Targets,
    ImmutableArray<EvidenceRecord> Evidence, string? ExclusionReason);
public sealed record SettingEditorView(string Id, string Name, string Description, ImmutableArray<string> CategoryPath,
    InputDefinition? Input, string DisplayValue, bool IsReadOnly, string VanillaLabel,
    ImmutableArray<CompiledOutput> Outputs, ImmutableArray<ValidationIssue> ValidationErrors, DeveloperSettingView? DeveloperDetails);
public sealed record EditorProjectionResult(CompileResult Compilation, ImmutableArray<SettingEditorView> Settings);

public static class EditorProjection
{
    public static EditorProjectionResult Build(ProfileEditSession session, ApplicationPreferences preferences)
    {
        // Invalid raw input must not expose the last valid draft as a successful compilation.
        var compilation = session.ValidationErrors.IsEmpty
            ? new ProfileCompiler(session.Catalog).Compile(session.Draft)
            : new CompileResult([], session.ValidationErrors);
        var errors = compilation.Issues;
        var views = ImmutableArray.CreateBuilder<SettingEditorView>();
        foreach (var s in session.Catalog.Visible(preferences.DeveloperMode))
        {
            var supported = s.SupportStatus == SupportStatus.Supported;
            var settingErrors = errors.Where(e => e.SettingId == s.Id || e.SettingId is null).ToImmutableArray();
            // Reuse the compiler's result objects. The view performs no conversion or numeric formatting.
            var outputs = supported && settingErrors.IsEmpty ? compilation.Outputs.Where(o => o.SettingId == s.Id).ToImmutableArray() : [];
            var display = supported ? session.RawInputs.GetValueOrDefault(s.Id, session.Draft.Values.GetValueOrDefault(s.Id).Format()) : "";
            DeveloperSettingView? developer = null;
            if (preferences.DeveloperMode)
            {
                var targets = s.Backend is ConsolePropertyBackend backend ? backend.Targets.Select(t => new DeveloperTargetView(
                    t.ClassName, t.PropertyName, t.VanillaValue?.Format() ?? "Unknown", DescribeConversion(t),
                    outputs.FirstOrDefault(o => o.Target.Identity == t.Identity)?.Command)).ToImmutableArray() : [];
                developer = new(s.SupportStatus, targets, s.Evidence, s.UnavailableReason);
            }
            views.Add(new(s.Id, s.Name, s.Description, s.CategoryPath, supported ? s.Input : null, display,
                !supported || session.Draft.IsReadOnly, s.Input is MultiplierInput ? "Vanilla: ×1" : "Vanilla: " + (s.DefaultValue?.Format() ?? "Unknown"),
                outputs, settingErrors, developer));
        }
        return new(compilation, views.ToImmutable());
    }

    private static string DescribeConversion(ConsoleTarget target) => target.Conversion switch
    {
        Conversion.Direct => "value",
        Conversion.MultiplyBaseline => (target.VanillaValue?.Format() ?? "Unknown baseline") + " × multiplier",
        Conversion.DivideByMultiplier => (target.VanillaValue?.Format() ?? "Unknown baseline") + " / multiplier",
        _ => "Unknown"
    };
}
