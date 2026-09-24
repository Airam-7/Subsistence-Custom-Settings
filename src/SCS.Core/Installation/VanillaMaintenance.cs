using System.Text;
using System.Text.Json;

namespace SCS.Core;

public static class VanillaMaintenance
{
    public sealed record Stamp(string CatalogVersion, string Hash);
    public static bool Current(SettingsCatalog catalog, string binaries)
    {
        var path = Path.Combine(binaries, "SCS_Vanilla.txt");
        return File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(Bytes(catalog));
    }
    private static byte[] Bytes(SettingsCatalog catalog) => Encoding.ASCII.GetBytes(new ProfileCompiler(catalog).Compile(ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog)).Text);
    public static void Ensure(SettingsCatalog catalog, string binaries, IAtomicCommitter? committer = null)
    {
        var path = Path.Combine(binaries, "SCS_Vanilla.txt"); var expected = Bytes(catalog);
        var original = File.Exists(path) ? File.ReadAllBytes(path) : null;
        void Write(string file, byte[] bytes, AtomicFileWriter? writer = null)
        { var result = (writer ?? new()).Write(file, bytes); if (!result.Success) throw new IOException(result.Failure!.Message); }
        if (original is null || !original.SequenceEqual(expected))
        {
            if (original is not null) Write(path + ".scs-upgrade-" + Guid.NewGuid().ToString("N") + ".bak", original);
            Write(path, expected, new AtomicFileWriter(new Guarded(original, committer ?? new AtomicCommitter())));
            if (!File.ReadAllBytes(path).SequenceEqual(expected)) throw new IOException("Vanilla changed after updating. Reload the installation.");
        }
        var stamp = Path.Combine(binaries, "SCS_Vanilla.catalog.json");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Stamp(catalog.Version, GameInstallation.Hash(expected)));
        if (!File.Exists(stamp) || !File.ReadAllBytes(stamp).SequenceEqual(bytes)) Write(stamp, bytes);
    }
    private sealed class Guarded(byte[]? original, IAtomicCommitter inner) : IAtomicCommitter
    {
        public void Commit(string temporaryPath, string destinationPath, ExistingFileBehavior behavior)
        {
            if (original is null ? File.Exists(destinationPath) : !File.Exists(destinationPath) || !File.ReadAllBytes(destinationPath).SequenceEqual(original))
                throw new IOException("Vanilla changed during the update. External changes were preserved.");
            inner.Commit(temporaryPath, destinationPath, behavior);
        }
    }
}
