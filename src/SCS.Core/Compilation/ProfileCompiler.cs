using System.Collections.Immutable;

namespace SCS.Core;

public sealed record CompileResult(ImmutableArray<CompiledOutput> Outputs, ImmutableArray<ValidationIssue> Issues)
{
    public bool Success => Issues.IsEmpty;
    public string Text => Success ? string.Join("\r\n", Outputs.Select(o => o.Command)) + (Outputs.IsEmpty ? "" : "\r\n") : "";
}

public sealed class ProfileCompiler(SettingsCatalog catalog)
{
    public CompileResult Compile(ProfileRecord profile)
    {
        var issues = ProfileValidator.Validate(catalog, profile).ToBuilder();
        if (issues.Count != 0) return new([], issues.ToImmutable());
        var outputs = new List<CompiledOutput>();
        foreach (var s in catalog.Supported)
        {
            var backend = (ConsolePropertyBackend)s.Backend;
            foreach (var target in backend.Targets)
            {
                try { outputs.Add(ValueConverter.Convert(s.Id, target, profile.Values[s.Id])); }
                catch (Exception ex) when (ex is ArithmeticException or InvalidOperationException)
                { issues.Add(new("Conversion", s.Id, target.Identity + ": " + ex.Message)); }
            }
        }
        if (issues.Count != 0) return new([], issues.ToImmutable());
        return new(outputs.OrderBy(o => o.Target.EmissionOrder).ThenBy(o => o.Target.ClassName, StringComparer.Ordinal)
            .ThenBy(o => o.Target.PropertyName, StringComparer.Ordinal).ToImmutableArray(), []);
    }
}
