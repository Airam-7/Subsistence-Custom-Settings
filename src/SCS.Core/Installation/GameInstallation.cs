using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SCS.Core;

public sealed record GamePaths(string Root, string Binaries, string InputIni)
{
    public static GamePaths FromBinaries(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose the Subsistence installation folder.");
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Path.GetFileName(path).Equals("Binaries", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Select Subsistence or its Binaries folder.");
        var root = Path.GetDirectoryName(path)!;
        return new(root, path, Path.Combine(root, "UDKGame", "Config", "UDKInput.ini"));
    }
    public bool HasGameExecutable => new[] { Path.Combine(Binaries, "Win64", "Subsistence.exe"), Path.Combine(Binaries, "Win32", "Subsistence.exe"), Path.Combine(Binaries, "Subsistence.exe") }.Any(File.Exists);
}
public sealed record GameVerification(GamePaths? Paths, bool BinariesFound, bool IniFound, bool BinariesWritable, bool IniWritable, string Message)
{
    public bool ProfilesReady => Paths is not null && BinariesFound && BinariesWritable;
    public bool Ready => ProfilesReady && IniFound && IniWritable;
}
public static class GameInstallation
{
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static GameVerification Verify(string binaries)
    {
        GamePaths? paths = null; var bf = false; var inf = false; var bw = false; var iw = false;
        try
        {
            paths = GamePaths.FromBinaries(binaries); bf = Directory.Exists(paths.Binaries) && paths.HasGameExecutable; inf = File.Exists(paths.InputIni);
            if (!bf) return new(paths, bf, inf, false, false, "Subsistence.exe and its Binaries folder must belong to this installation.");
            ProbeReplacement(paths.Binaries); bw = true;
            if (!inf) return new(paths, bf, inf, bw, false, "UDKInput.ini was not found in the same installation.");
            _ = new InputIniDocument(File.ReadAllBytes(paths.InputIni));
            if ((File.GetAttributes(paths.InputIni) & FileAttributes.ReadOnly) != 0) throw new IOException("UDKInput.ini is read-only.");
            using (var readWrite = new FileStream(paths.InputIni, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) { }
            ProbeReplacement(Path.GetDirectoryName(paths.InputIni)!); iw = true;
            return new(paths, bf, inf, bw, iw, "Both locations verified.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or NotSupportedException)
        { return new(paths, bf, inf, bw, iw, ex.Message); }
    }
    private static void ProbeReplacement(string folder)
    {
        var path = Path.Combine(folder, ".scs-permission-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(path, [1]); var result = new AtomicFileWriter().Write(path, new byte[] { 2 }); if (!result.Success) throw new IOException(result.Failure!.Message); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public sealed record ProfileManifestEntry(string FileName, string Hash, bool Created);
public sealed record InstallResult(bool Success, string Message);
public sealed class ManagedProfiles
{
    private readonly AtomicFileWriter writer = new();
    private static string ManifestPath(GamePaths paths) => Path.Combine(paths.Binaries, "SCS_Manifest.json");
    public static string[] Names => ["SCS_Profile1.txt", "SCS_Profile2.txt", "SCS_Profile3.txt", "SCS_Profile4.txt", "SCS_Vanilla.txt"];
    private Dictionary<string, ProfileManifestEntry> Read(GamePaths p)
    {
        var file = ManifestPath(p);
        return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, ProfileManifestEntry>>(File.ReadAllText(file)) ?? throw new FormatException("Invalid SCS profile manifest.") : [];
    }
    private void WriteManifest(GamePaths p, Dictionary<string, ProfileManifestEntry> entries) => WriteRequired(ManifestPath(p), JsonSerializer.SerializeToUtf8Bytes(entries));
    private void WriteRequired(string path, byte[] bytes) { var result = writer.Write(path, bytes); if (!result.Success) throw new IOException(result.Failure!.Message); }

    public ProfileSaveResult Save(ProfileEditSession session, string binaries)
    {
        var verify = GameInstallation.Verify(binaries);
        if (!verify.ProfilesReady) return new(null, [new("Installation", null, verify.Message)]);
        if (!session.ValidationErrors.IsEmpty || session.Draft.IsReadOnly) return new ProfileWriter().Save(session, binaries);
        var paths = verify.Paths!; var manifest = Read(paths); var target = Path.Combine(binaries, session.Draft.FileName);
        if (File.Exists(target))
        {
            var backup = target + ".scs-save-" + Guid.NewGuid().ToString("N") + ".bak";
            WriteRequired(backup, File.ReadAllBytes(target));
        }
        var saved = new ProfileWriter().Save(session, binaries);
        if (saved.Success)
        {
            try { manifest[session.Saved.FileName] = new(session.Saved.FileName, GameInstallation.Hash(File.ReadAllBytes(target)), true); WriteManifest(paths, manifest); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { return saved with { Warning = "Profile saved, but its removal manifest could not be updated: " + ex.Message }; }
        }
        return saved;
    }
    public InstallResult InstallProfiles(SettingsCatalog catalog, IEnumerable<ProfileRecord> profiles, string binaries)
    {
        var verify = GameInstallation.Verify(binaries); if (!verify.ProfilesReady) return new(false, verify.Message);
        var records = profiles.Where(p => !p.IsReadOnly).ToArray();
        if (records.Length != 4 || records.Select(p => p.Id).Distinct().Count() != 4) return new(false, "Four custom profiles are required.");
        var outputs = records.Select(p => (Profile:p, Result:new ProfileCompiler(catalog).Compile(p))).ToArray();
        if (outputs.Any(o => !o.Result.Success)) return new(false, "Invalid profile values.");
        var manifest = Read(verify.Paths!);
        foreach (var output in outputs)
        {
            var bytes = Encoding.ASCII.GetBytes(output.Result.Text);
            var result = writer.Write(Path.Combine(binaries, output.Profile.FileName), bytes, ExistingFileBehavior.Preserve);
            if (!result.Success) return new(false, result.Failure!.Message);
            if (result.Status == WriteStatus.Written) { manifest[output.Profile.FileName] = new(output.Profile.FileName, GameInstallation.Hash(bytes), true); WriteManifest(verify.Paths!, manifest); }
        }
        MaintainVanilla(catalog, binaries);
        return new(true, "Profile files ready. Vanilla is current; custom files preserved. Hotkeys were not changed.");
    }
    public void MaintainVanilla(SettingsCatalog catalog, string binaries)
    {
        var paths = GamePaths.FromBinaries(binaries); var manifest = Read(paths);
        VanillaMaintenance.Ensure(catalog, binaries);
        manifest["SCS_Vanilla.txt"] = new("SCS_Vanilla.txt", GameInstallation.Hash(File.ReadAllBytes(Path.Combine(binaries,"SCS_Vanilla.txt"))), true);
        WriteManifest(paths, manifest);
    }
    public InstallResult Install(SettingsCatalog catalog, IEnumerable<ProfileRecord> profiles, string binaries, IReadOnlyCollection<HotkeyAssignment> keys, IGameRunningProbe? probe = null)
    {
        var verify = GameInstallation.Verify(binaries); if (!verify.Ready) return new(false, verify.Message);
        var paths = verify.Paths!;
        var currentIni = new InputIniDocument(File.ReadAllBytes(paths.InputIni)); var errors = IniHotkeys.Validate(keys, currentIni);
        if (!errors.IsEmpty) return new(false, string.Join("\n", errors.Select(e => e.Message)));
        var records = profiles.Where(p => !p.IsReadOnly).Append(ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog)).ToArray();
        if (records.Length != 5 || records.Select(p => p.Id).Distinct().Count() != 5) return new(false, "Five unique profile slots are required.");
        var outputs = records.Select(p => (Profile: p, Result: new ProfileCompiler(catalog).Compile(p))).ToArray();
        if (outputs.Any(o => !o.Result.Success)) return new(false, "Profiles are invalid; nothing was installed.");
        var manifest = Read(paths); var created = new Dictionary<string, string>();
        var journal = Path.Combine(paths.Binaries, "SCS_Install-" + Guid.NewGuid().ToString("N") + ".json");
        void Journal(string state) => WriteRequired(journal, JsonSerializer.SerializeToUtf8Bytes(new { state, root = paths.Root, created, iniBefore = GameInstallation.Hash(currentIni.OriginalBytes) }));
        Journal("Prepared");
        try
        {
            foreach (var output in outputs)
            {
                var name = output.Profile.FileName; var bytes = Encoding.ASCII.GetBytes(output.Result.Text);
                var result = writer.Write(Path.Combine(binaries, name), bytes, ExistingFileBehavior.Preserve);
                if (!result.Success) throw new IOException(result.Failure!.Message);
                if (result.Status == WriteStatus.Written) { created[name] = GameInstallation.Hash(bytes); Journal("Profiles partially created"); }
            }
            MaintainVanilla(catalog, binaries);
            manifest = Read(paths);
            var hotkey = new IniHotkeyService(probe).Install(paths.InputIni, keys);
            if (!hotkey.Success) throw new IOException(hotkey.Message);
            // Profiles are committed before bindings. A manifest failure leaves a recoverable journal, not deleted bound profiles.
            Journal("Profiles and hotkeys committed");
            foreach (var (name, hash) in created) manifest[name] = new(name, hash, true);
            WriteManifest(paths, manifest); Journal("Complete");
            return new(true, $"Profile files: {created.Count} created, {5-created.Count} preserved. Hotkeys installed; pending in-game use.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            var installed = new InputIniDocument(File.ReadAllBytes(paths.InputIni)).Bindings.Count(b => b.IsScs && !b.Uncertain) == 5;
            var blocked = new List<string>();
            if (!installed) foreach (var (name, hash) in created)
            {
                var path = Path.Combine(binaries, name);
                if (File.Exists(path) && GameInstallation.Hash(File.ReadAllBytes(path)) == hash) File.Delete(path); else blocked.Add(name);
            }
            Journal(installed ? "Committed files retained; recovery required" : blocked.Count == 0 ? "Rolled back created profiles" : "Recovery blocked by external changes");
            return new(false, ex.Message + (installed ? " Profiles and hotkeys may be committed; review the installation journal." : " Newly created profiles were rolled back where unchanged.") + (blocked.Count > 0 ? " External changes preserved: " + string.Join(", ", blocked) : ""));
        }
    }
    public InstallResult Remove(string binaries, IEnumerable<string> selected)
    {
        var paths = GamePaths.FromBinaries(binaries); var entries = Read(paths); var names = selected.Distinct().ToArray();
        if (new InputIniDocument(File.ReadAllBytes(paths.InputIni)).Bindings.Any(b => b.IsScs)) return new(false, "Remove SCS hotkeys before removing profile files.");
        foreach (var name in names)
        {
            if (!Names.Contains(name) || !entries.TryGetValue(name, out var entry)) return new(false, "This profile is not recorded in the installation manifest: " + name);
            var path = Path.Combine(binaries, name);
            if (File.Exists(path) && GameInstallation.Hash(File.ReadAllBytes(path)) != entry.Hash) return new(false, "Externally modified profile preserved: " + name);
        }
        foreach (var name in names)
        {
            var path = Path.Combine(binaries, name);
            if (File.Exists(path))
            {
                WriteRequired(path + ".scs-removed-" + Guid.NewGuid().ToString("N") + ".bak", File.ReadAllBytes(path));
                if (GameInstallation.Hash(File.ReadAllBytes(path)) != entries[name].Hash)
                    return new(false, "Externally modified profile preserved: " + name);
                File.Delete(path);
            }
            entries.Remove(name);
        }
        WriteManifest(paths, entries); return new(true, "Selected profile files removed. Backups were preserved.");
    }
}
