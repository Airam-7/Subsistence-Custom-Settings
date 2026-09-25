using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace SCS.Core;

public sealed record InputBinding(string Section, string Name, string Command, bool Control, bool Shift, bool Alt,
    bool IgnoreControl, bool IgnoreShift, bool IgnoreAlt, bool Uncertain, int Start, int Length, string Operator = "")
{
    public bool IsScs => Regex.IsMatch(Command.Trim(), @"^exec\s+SCS_(?:Profile[1-4]|Vanilla)\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public bool Matches(HotkeyAssignment key) => Name.Equals(key.KeyId, StringComparison.OrdinalIgnoreCase)
        && (Uncertain || ((!Control || key.Control) && (!Shift || key.Shift) && (!Alt || key.Alt)
            && (!IgnoreControl || !key.Control) && (!IgnoreShift || !key.Shift) && (!IgnoreAlt || !key.Alt)));
    public string FriendlyCommand => Command switch { "GBA_QuickSave" => "Quick Save", "GBA_QuickLoad" => "Quick Load", "NextViewMode" => "Next View Mode", _ => "existing game binding" };
}

public sealed class InputIniDocument
{
    public const string ChildSection = "ColdGame.ColdPlayerInput";
    public bool HasCompleteLayout => Bindings.Count(b => b.IsScs) == 10 && Enum.GetValues<ProfileId>().All(id => InstalledKey(id) is not null);
    public bool NeedsRepair => Bindings.Any(b => b.IsScs) && !HasCompleteLayout;
    public const string LegacySection = "Engine.PlayerInput";
    public const string Begin = "; >>> SCS HOTKEYS >>>";
    public const string End = "; <<< SCS HOTKEYS <<<";
    private const string BeginNoEol = Begin + " [original file had no final newline]";
    public byte[] OriginalBytes { get; }
    public string Text { get; }
    public string NewLine { get; }
    public ImmutableArray<InputBinding> Bindings { get; }
    private readonly Encoding encoding;
    private readonly byte[] bom;
    private readonly List<(int Start, int Length, string Section, string Body)> lines = [];
    private readonly Dictionary<string, int> sectionEnds;

    public InputIniDocument(byte[] bytes)
    {
        OriginalBytes = bytes.ToArray();
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { encoding = new UTF8Encoding(false, true); bom = [0xEF, 0xBB, 0xBF]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) { encoding = new UnicodeEncoding(false, false, true); bom = [0xFF, 0xFE]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) { encoding = new UnicodeEncoding(true, false, true); bom = [0xFE, 0xFF]; }
        else { bom = []; try { _ = new UTF8Encoding(false, true).GetString(bytes); encoding = new UTF8Encoding(false, true); } catch (DecoderFallbackException) { encoding = Encoding.Latin1; } }
        Text = encoding.GetString(bytes, bom.Length, bytes.Length - bom.Length);
        if (Text.Contains('\0')) throw new FormatException("Unsupported INI encoding.");
        NewLine = Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : Text.Contains('\n') ? "\n" : "\r\n";
        var section = ""; var counts = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var ends = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var bindings = ImmutableArray.CreateBuilder<InputBinding>();
        foreach (Match match in Regex.Matches(Text, @"[^\r\n]*(?:\r\n|\n|\r|$)"))
        {
            if (match.Length == 0) continue;
            var body = match.Value.TrimEnd('\r', '\n'); var trimmed = body.Trim();
            var header = Regex.Match(trimmed, @"^\[([^\]]+)\]\s*(?:[;#].*)?$");
            if (header.Success)
            {
                if (Relevant(section)) ends[section] = match.Index;
                section = header.Groups[1].Value;
                if (Relevant(section)) counts[section] = counts.GetValueOrDefault(section) + 1;
            }
            lines.Add((match.Index, match.Length, section, body));
            if (!Relevant(section) || !Regex.IsMatch(trimmed, @"^[+.]?Bindings\s*=", RegexOptions.IgnoreCase)) continue;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var uncertain = !Regex.IsMatch(trimmed, @"^[+.]?Bindings\s*=\(.*\)\s*$", RegexOptions.IgnoreCase);
            foreach (Match field in Regex.Matches(trimmed, "([A-Za-z][A-Za-z0-9_]*)\\s*=\\s*(?:\"((?:[^\"\\\\]|\\\\.)*)\"|([^,()\\s]+))"))
            {
                var name = field.Groups[1].Value;
                if (name.Equals("Bindings", StringComparison.OrdinalIgnoreCase)) continue;
                if (!fields.TryAdd(name, field.Groups[2].Success ? field.Groups[2].Value : field.Groups[3].Value)) uncertain = true;
            }
            if (!fields.TryGetValue("Name", out var key)) throw new FormatException("An input binding has no readable key name; conflict detection is unavailable.");
            bool Flag(string name) { if (!fields.TryGetValue(name, out var value)) return false; if (bool.TryParse(value, out var flag)) return flag; uncertain = true; return false; }
            var ctrl = Flag("Control"); var shift = Flag("Shift"); var alt = Flag("Alt");
            var ic = Flag("bIgnoreCtrl"); var ish = Flag("bIgnoreShift"); var ia = Flag("bIgnoreAlt");
            var command = fields.GetValueOrDefault("Command", "Unknown command");
            if (command == "Unknown command") uncertain = true;
            var bindingOperator = trimmed[0] is '+' or '.' ? trimmed[..1] : "";
            bindings.Add(new(section, key, command, ctrl, shift, alt, ic, ish, ia, uncertain, match.Index, match.Length, bindingOperator));
        }
        if (Relevant(section)) ends[section] = Text.Length;
        if (counts.Values.Any(count => count != 1)) throw new FormatException("Duplicate input sections; existing INI was preserved.");
        Bindings = bindings.ToImmutable();
        if (ends.Count == 0) throw new FormatException("Input sections are missing. Existing INI was preserved.");
        sectionEnds = ends;
    }

    // A profile is installed only when both copies agree, including the array operator.
    public string? InstalledKey(ProfileId id)
    {
        var command = "exec " + (id == ProfileId.Vanilla ? "SCS_Vanilla.txt" : $"SCS_Profile{(int)id}.txt");
        var matches = Bindings.Where(b => b.IsScs && Regex.IsMatch(b.Command.Trim(), "^exec\\s+" + Regex.Escape(command[5..]) + "$", RegexOptions.IgnoreCase)).ToArray();
        if (matches.Length != 2 || matches.Any(b => b.Uncertain || b.Control || b.Shift || b.Alt || b.IgnoreControl || b.IgnoreShift || b.IgnoreAlt)) return null;
        if (matches.Count(b => b.Section.Equals(LegacySection, StringComparison.OrdinalIgnoreCase) && b.Operator == "") != 1
            || matches.Count(b => b.Section.Equals(ChildSection, StringComparison.OrdinalIgnoreCase) && b.Operator == ".") != 1) return null;
        var key = matches[0].Name;
        if (!matches[1].Name.Equals(key, StringComparison.OrdinalIgnoreCase)
            || !IniHotkeys.AllowedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
            || Bindings.Count(b => b.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) != 2) return null;
        return key;
    }

    private static bool Relevant(string section) => section.Equals(ChildSection, StringComparison.OrdinalIgnoreCase) || section.Equals(LegacySection, StringComparison.OrdinalIgnoreCase);
    // Simple SCS bindings have no modifier exclusions. Any existing chord on that key can overlap.
    public ImmutableArray<InputBinding> Conflicts(HotkeyAssignment assignment) => Bindings.Where(b => (!b.IsScs || b.Uncertain) && b.Name.Equals(assignment.KeyId, StringComparison.OrdinalIgnoreCase)).ToImmutableArray();
    public byte[] Encode(string text) => bom.Concat(encoding.GetBytes(text)).ToArray();
    public byte[] RemoveScs()
    {
        var ranges = Bindings.Where(b => b.IsScs && !b.Uncertain).Select(b => (b.Start, b.Length)).ToList();
        ranges.AddRange(lines.Where(l => Relevant(l.Section) && (l.Body.Trim() == Begin || l.Body.Trim() == End || l.Body.Trim() == BeginNoEol)).Select(l =>
            l.Body.Trim() == BeginNoEol && Text.TrimEnd('\r','\n').EndsWith(End, StringComparison.Ordinal) && l.Start >= NewLine.Length
                ? (l.Start - NewLine.Length, l.Length + NewLine.Length) : (l.Start, l.Length)));
        var result = new StringBuilder(Text);
        foreach (var range in ranges.Distinct().OrderByDescending(r => r.Start)) result.Remove(range.Start, range.Length);
        return Encode(result.ToString());
    }
    public byte[] Install(IEnumerable<HotkeyAssignment> assignments)
    {
        var clean = new InputIniDocument(RemoveScs());
        var keys = assignments.ToArray();
        if (!IniHotkeys.Validate(keys, clean).IsEmpty) throw new ArgumentException("Choose five valid, available hotkeys.");
        if (!clean.sectionEnds.ContainsKey(LegacySection) || !clean.sectionEnds.ContainsKey(ChildSection))
            throw new FormatException("Both Engine.PlayerInput and ColdGame.ColdPlayerInput are required. Existing INI was preserved.");
        var result = new StringBuilder(clean.Text);
        // Descending offsets preserve insertion positions even when the sections are reversed.
        foreach (var section in clean.sectionEnds.OrderByDescending(s => s.Value))
        {
            var position = section.Value;
            var prefix = position > 0 && clean.Text[position - 1] is not '\n' and not '\r' ? clean.NewLine : "";
            var append = section.Key.Equals(ChildSection, StringComparison.OrdinalIgnoreCase) ? "." : "";
            var block = prefix + (prefix.Length > 0 ? BeginNoEol : Begin) + clean.NewLine
                + string.Join(clean.NewLine, keys.OrderBy(a => a.ProfileId).Select(a => append + IniHotkeys.BindingLine(a))) + clean.NewLine + End + clean.NewLine;
            result.Insert(position, block);
        }
        return clean.Encode(result.ToString());
    }
}

public static class IniHotkeys
{
    public static ImmutableArray<HotkeyAssignment> Defaults => [new(ProfileId.Profile1, "F10"), new(ProfileId.Profile2, "F11"), new(ProfileId.Profile3, "F12"), new(ProfileId.Profile4, "PageUp"), new(ProfileId.Vanilla, "PageDown")];
    public static string[] AllowedKeys => Enumerable.Range(1, 12).Select(k => "F" + k).Concat(new[] { "Insert", "Home", "PageUp", "PageDown", "ScrollLock", "Pause", "NumPadZero", "NumPadOne", "NumPadTwo", "NumPadThree", "NumPadFour", "NumPadFive", "NumPadSix", "NumPadSeven", "NumPadEight", "NumPadNine" }).ToArray();
    public static IEnumerable<HotkeyAssignment> Choices(ProfileId id)
    {
        foreach (var key in AllowedKeys) yield return new(id, key);
    }
    public static ImmutableArray<ValidationIssue> Validate(IEnumerable<HotkeyAssignment> assignments, InputIniDocument? document = null, bool allowUnassigned = false)
    {
        var list = assignments.ToArray(); var issues = ImmutableArray.CreateBuilder<ValidationIssue>();
        if (list.Length != 5 || list.Any(a => !Enum.IsDefined(a.ProfileId)) || list.Select(a => a.ProfileId).Distinct().Count() != 5) issues.Add(new("Profiles", null, "Assign one hotkey to each of the five profiles."));
        foreach (var key in list)
        {
            if (string.IsNullOrEmpty(key.KeyId) && !key.Control && !key.Shift && !key.Alt) { if (!allowUnassigned) issues.Add(new("Unassigned", null, $"Choose a hotkey for {key.ProfileId}.")); continue; }
            if (!AllowedKeys.Contains(key.KeyId) || key.Control || key.Shift || key.Alt) issues.Add(new("Key", null, "Choose an allowed simple key. Modifier combinations are not supported in v1."));
            var conflicts = document?.Conflicts(key) ?? [];
            if (!conflicts.IsEmpty) issues.Add(new("ExistingBinding", null, key.Label + " — Unavailable: " + string.Join("; ", conflicts.Select(c => c.FriendlyCommand).Distinct())));
        }
        foreach (var group in list.Where(k => !string.IsNullOrEmpty(k.KeyId)).GroupBy(k => k.Label).Where(g => g.Count() > 1)) issues.Add(new("DuplicateHotkey", null, group.Key + " is assigned to more than one SCS profile."));
        return issues.ToImmutable();
    }
    public static string BindingLine(HotkeyAssignment a)
    {
        var file = a.ProfileId == ProfileId.Vanilla ? "SCS_Vanilla.txt" : $"SCS_Profile{(int)a.ProfileId}.txt";
        if (a.Control || a.Shift || a.Alt || !AllowedKeys.Contains(a.KeyId) || !Enum.IsDefined(a.ProfileId)) throw new ArgumentException("Only supported simple keys may be installed.");
        return $"Bindings=(Name=\"{a.KeyId}\",Command=\"exec {file}\",Control=False,Shift=False,Alt=False,bIgnoreCtrl=False,bIgnoreShift=False,bIgnoreAlt=False)";
    }
}

public interface IGameRunningProbe { bool IsRunning(); }
public sealed class GameRunningProbe(string? installationRoot = null) : IGameRunningProbe
{
    public bool IsRunning()
    {
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try
            {
                var name = process.ProcessName;
                if (name.StartsWith("Subsistence", StringComparison.OrdinalIgnoreCase) && !name.Equals("Subsistence Custom Settings", StringComparison.OrdinalIgnoreCase) || name.Equals("UDK", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UDK-Win", StringComparison.OrdinalIgnoreCase) || name.Equals("ColdGame", StringComparison.OrdinalIgnoreCase))
                {
                    if (installationRoot is null) return true;
                    var executable = process.MainModule?.FileName ?? throw new IOException("Cannot identify the running game's installation. Close it before changing hotkeys.");
                    if (Path.GetFullPath(executable).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch (InvalidOperationException) { /* Process exited during enumeration. */ }
        }
        return false;
    }
}

public sealed record IniUpdateResult(bool Success, bool Changed, string Message);
public sealed class IniHotkeyService
{
    private readonly IAtomicCommitter? committer;
    private readonly Action? beforeCommit;
    public IniHotkeyService(IGameRunningProbe? probe = null, IAtomicCommitter? committer = null, Action? beforeCommit = null)
    {
        // Compatibility with callers/tests: process state no longer controls file editing.
        _ = probe; this.committer = committer; this.beforeCommit = beforeCommit;
    }
    public IniUpdateResult Install(string iniPath, IEnumerable<HotkeyAssignment> assignments) => Update(iniPath, assignments.ToArray());
    public IniUpdateResult MigrateLegacy(string iniPath)
    {
        try
        {
            var doc = new InputIniDocument(File.ReadAllBytes(iniPath));
            if (!doc.NeedsRepair)
                return new(true, false, "No hotkey migration required.");
            var assignments = new List<HotkeyAssignment>();
            foreach (var id in Enum.GetValues<ProfileId>())
            {
                var file = id == ProfileId.Vanilla ? "SCS_Vanilla.txt" : $"SCS_Profile{(int)id}.txt";
                var matches = doc.Bindings.Where(b => b.IsScs && Regex.IsMatch(b.Command.Trim(), "^exec\\s+" + Regex.Escape(file) + "$", RegexOptions.IgnoreCase)).ToArray();
                if (matches.Length == 0 || matches.Any(b => b.Uncertain || b.Control || b.Shift || b.Alt || b.IgnoreControl || b.IgnoreShift || b.IgnoreAlt)
                    || matches.Select(b => b.Name.ToUpperInvariant()).Distinct().Count() != 1)
                    return new(false, false, "Legacy hotkeys are incomplete or ambiguous. Choose five available keys in Hotkeys and install them; existing bindings were preserved.");
                var key = IniHotkeys.AllowedKeys.FirstOrDefault(k => k.Equals(matches[0].Name, StringComparison.OrdinalIgnoreCase)) ?? matches[0].Name;
                assignments.Add(new(id, key));
            }
            // Update re-reads and validates the complete file before its guarded atomic commit.
            return Update(iniPath, assignments.ToArray(), doc.OriginalBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        { return new(false, false, ex.Message); }
    }
    public IniUpdateResult Remove(string iniPath) => Update(iniPath, null);
    private IniUpdateResult Update(string path, HotkeyAssignment[]? assignments, byte[]? expectedOriginal = null)
    {
        try
        {
            var original = File.ReadAllBytes(path);
            if (expectedOriginal is not null && !original.SequenceEqual(expectedOriginal)) return new(false, false, "The INI changed before migration. Reload and try again.");
            var doc = new InputIniDocument(original);
            path = Path.GetFullPath(path);
            if (doc.Bindings.Any(b => b.IsScs && b.Uncertain)) return new(false, false, "An SCS-looking binding is malformed. It was preserved; resolve it before updating hotkeys.");
            if (assignments is not null)
            {
                var errors = IniHotkeys.Validate(assignments, doc);
                if (!errors.IsEmpty) return new(false, false, string.Join("\n", errors.Select(e => e.Message)));
            }
            var replacement = assignments is null ? doc.RemoveScs() : doc.Install(assignments);
            if (replacement.SequenceEqual(original)) return new(true, false, assignments is null ? "No SCS hotkeys to remove." : "Hotkeys already installed.");
            var backup = Path.Combine(Path.GetDirectoryName(path)!, "UDKInput.scs-backup.ini");
            var identity = Path.Combine(Path.GetDirectoryName(path)!, "UDKInput.scs-backup.json");
            if (File.Exists(backup) && File.Exists(identity))
            {
                var recorded = JsonSerializer.Deserialize<BackupIdentity>(File.ReadAllText(identity))!;
                if (!string.Equals(recorded.IniPath, path, StringComparison.OrdinalIgnoreCase) || recorded.Hash != GameInstallation.Hash(File.ReadAllBytes(backup)))
                    return new(false, false, "The original backup failed its identity/hash check. UDKInput.ini was preserved.");
            }
            else if (File.Exists(backup) && !File.ReadAllBytes(backup).SequenceEqual(original)) return new(false, false, "An unverified original backup already exists. It and UDKInput.ini were preserved.");
            var backupResult = new AtomicFileWriter().Write(backup, original, ExistingFileBehavior.Preserve);
            if (!backupResult.Success) return new(false, false, "Mandatory backup failed: " + backupResult.Failure!.Message);
            if (!File.Exists(identity)) RequireWrite(identity, JsonSerializer.SerializeToUtf8Bytes(new BackupIdentity(path, GameInstallation.Hash(original))));
            var snapshot = Path.Combine(Path.GetDirectoryName(path)!, "UDKInput.scs-original-bindings.txt");
            var snapshotResult = new AtomicFileWriter().Write(snapshot, Encoding.UTF8.GetBytes(string.Join("\n", doc.Bindings.Where(b => b.IsScs).Select(b => doc.Text.Substring(b.Start, b.Length)))), ExistingFileBehavior.Preserve);
            if (!snapshotResult.Success) return new(false, false, "SCS snapshot failed: " + snapshotResult.Failure!.Message);
            var operation = Path.Combine(Path.GetDirectoryName(path)!, "UDKInput.scs-operation-" + Guid.NewGuid().ToString("N"));
            RequireWrite(operation + ".ini", original);
            var committed = false;
            void Journal(string state) => RequireWrite(operation + ".json", JsonSerializer.SerializeToUtf8Bytes(new {
                state, IniPath = path, BackupPath = operation + ".ini", BeforeHash = GameInstallation.Hash(original), AfterHash = GameInstallation.Hash(replacement),
                OriginalScs = doc.Bindings.Where(b => b.IsScs).Select(b => new { b.Section, b.Start, Text = doc.Text.Substring(b.Start, b.Length) }).ToArray(),
                AddedLines = assignments?.SelectMany(a => new[] { new { Section = InputIniDocument.LegacySection, Text = IniHotkeys.BindingLine(a) }, new { Section = InputIniDocument.ChildSection, Text = "." + IniHotkeys.BindingLine(a) } }).ToArray()
            }));
            Journal("Prepared");
            var writer = new AtomicFileWriter(new CheckedCommitter(original, committer ?? new AtomicCommitter(), beforeCommit));
            var result = writer.Write(path, replacement);
            if (!result.Success) { Journal("Commit failed; original or external changes preserved"); return new(false, false, result.Failure!.Message); }
            committed = File.ReadAllBytes(path).SequenceEqual(replacement);
            if (!committed) { Journal("Recovery blocked: file changed after commit"); return new(false, true, "The INI changed after commit. External changes were preserved; review " + operation + ".json"); }
            var expectedCount = (assignments?.Length ?? 0) * 2;
            var verifiedDocument = new InputIniDocument(replacement);
            var verifiedBindings = verifiedDocument.Bindings.Where(b => b.IsScs).ToArray();
            if (verifiedBindings.Length != expectedCount || assignments is not null && (!verifiedDocument.HasCompleteLayout || assignments.Any(a => !a.KeyId.Equals(verifiedDocument.InstalledKey(a.ProfileId), StringComparison.OrdinalIgnoreCase)))) throw new IOException("SCS binding pair verification failed.");
            Journal("Complete");
            return new(true, true, assignments is null ? "SCS hotkeys removed. Other bindings preserved." : "✓ Hotkeys installed in INI; pending in-game use.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or System.ComponentModel.Win32Exception or InvalidOperationException or JsonException)
        { return new(false, false, ex.Message); }
    }
    private sealed record BackupIdentity(string IniPath, string Hash);
    private static void RequireWrite(string path, byte[] bytes)
    { var result = new AtomicFileWriter().Write(path, bytes); if (!result.Success) throw new IOException(result.Failure!.Message); }
    private sealed class CheckedCommitter(byte[] expected, IAtomicCommitter inner, Action? beforeCommit) : IAtomicCommitter
    {
        public void Commit(string temporaryPath, string destinationPath, ExistingFileBehavior behavior)
        {
            beforeCommit?.Invoke();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(expected), SHA256.HashData(File.ReadAllBytes(destinationPath)))) throw new IOException("UDKInput.ini changed during the operation. Reload and try again.");
            inner.Commit(temporaryPath, destinationPath, behavior);
        }
    }
}
