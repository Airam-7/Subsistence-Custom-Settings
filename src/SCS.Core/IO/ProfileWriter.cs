using System.Collections.Immutable;

namespace SCS.Core;

public sealed record ProfileSaveResult(FileWriteResult? File, ImmutableArray<ValidationIssue> Issues, string? Warning = null)
{
    public bool Success => Issues.IsEmpty && File is { Status: WriteStatus.Written };
}

public sealed class ProfileWriter(AtomicFileWriter? writer = null, InstallationService? installation = null)
{
    private readonly AtomicFileWriter writer = writer ?? new();
    private readonly InstallationService installation = installation ?? new();

    public ProfileSaveResult Save(ProfileEditSession session, string binariesPath)
    {
        if (session.Draft.IsReadOnly) return new(null, [new("ReadOnly", null, "Vanilla cannot be saved through an editing session.")]);
        if (session.State == SaveState.Saving) return new(null, [new("Busy", null, "A save is already in progress.")]);
        var issues = session.ValidationErrors;
        if (!issues.IsEmpty) return new(null, issues);
        var compiled = new ProfileCompiler(session.Catalog).Compile(session.Draft);
        if (!compiled.Success) return new(null, compiled.Issues);
        session.BeginSave();
        var verified = installation.Verify(binariesPath);
        if (!verified.Ready)
        {
            var message = verified.Failure?.Message ?? "Choose an installation first.";
            session.SaveFailed(message); return new(null, [new("Installation", null, message)]);
        }
        var result = writer.WriteConsoleText(Path.Combine(verified.BinariesPath!, session.Draft.FileName), compiled.Text);
        if (result.Success)
        {
            try { if (!System.IO.File.ReadAllBytes(result.Path).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(compiled.Text))) throw new IOException("Profile changed after saving. Retry the save."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { session.SaveFailed(ex.Message); return new(result, [new("Readback", null, ex.Message)]); }
            session.SaveSucceeded();
        }
        else session.SaveFailed(result.Failure!.Message);
        return new(result, []);
    }
}
