using System.Text;

namespace SCS.Core;

public enum ExistingFileBehavior { Replace, Preserve }
public enum WriteStatus { Written, AlreadyExists, Failed }
public enum FileFailureKind { NotFound, NotWritable, PermissionDenied, WrongFolder, InvalidContent, IoError }
public sealed record FileFailure(FileFailureKind Kind, string Message);
public sealed record FileWriteResult(WriteStatus Status, string Path, FileFailure? Failure = null)
{
    public bool Success => Status != WriteStatus.Failed;
}

public interface IAtomicCommitter
{
    void Commit(string temporaryPath, string destinationPath, ExistingFileBehavior behavior);
}

public sealed class AtomicCommitter : IAtomicCommitter
{
    public void Commit(string temporaryPath, string destinationPath, ExistingFileBehavior behavior)
    {
        if (behavior == ExistingFileBehavior.Replace && File.Exists(destinationPath))
            File.Replace(temporaryPath, destinationPath, null, ignoreMetadataErrors: false);
        else File.Move(temporaryPath, destinationPath, overwrite: false);
    }
}

public sealed class AtomicFileWriter(IAtomicCommitter? committer = null)
{
    private readonly IAtomicCommitter committer = committer ?? new AtomicCommitter();

    public FileWriteResult WriteConsoleText(string destination, string text, ExistingFileBehavior behavior = ExistingFileBehavior.Replace)
    {
        if (text.Any(c => c > 127)) return new(WriteStatus.Failed, destination, new(FileFailureKind.InvalidContent, "Console files must contain only ASCII commands."));
        return Write(destination, Encoding.ASCII.GetBytes(text), behavior);
    }

    public FileWriteResult Write(string destination, ReadOnlySpan<byte> bytes, ExistingFileBehavior behavior = ExistingFileBehavior.Replace)
    {
        string? temporary = null;
        try
        {
            destination = Path.GetFullPath(destination);
            var parent = Path.GetDirectoryName(destination)!;
            if (!Directory.Exists(parent)) return new(WriteStatus.Failed, destination, new(FileFailureKind.NotFound, "The destination directory does not exist."));
            if (Directory.Exists(destination)) return new(WriteStatus.Failed, destination, new(FileFailureKind.NotWritable, "A directory occupies the destination filename."));
            if (File.Exists(destination) && behavior == ExistingFileBehavior.Preserve) return new(WriteStatus.AlreadyExists, destination);
            if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0)
                return new(WriteStatus.Failed, destination, new(FileFailureKind.NotWritable, "The destination file is read-only."));
            temporary = Path.Combine(parent, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            // Only a complete, flushed, closed sibling file reaches the commit operation.
            try { committer.Commit(temporary, destination, behavior); }
            catch (IOException) when (behavior == ExistingFileBehavior.Preserve && File.Exists(destination))
            { return new(WriteStatus.AlreadyExists, destination); }
            return new(WriteStatus.Written, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(WriteStatus.Failed, destination, Classify(ex)); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* An orphan temp cannot be executed by a profile hotkey. */ }
            }
        }
    }

    internal static FileFailure Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => new(FileFailureKind.PermissionDenied, "Access was denied. Additional permission may be required."),
        DirectoryNotFoundException or FileNotFoundException => new(FileFailureKind.NotFound, "The destination no longer exists."),
        ArgumentException or NotSupportedException => new(FileFailureKind.WrongFolder, "The selected path is invalid."),
        _ => new(FileFailureKind.IoError, ex.Message)
    };
}
