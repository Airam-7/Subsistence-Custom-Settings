using SCS.Core;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

var rootArg = args.Length == 2 && args[0] == "--scratch" ? args[1] : Path.Combine(Path.GetTempPath(), "SCS-tests");
var root = Path.Combine(Path.GetFullPath(rootArg), "run-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var catalog = ReferenceCatalog.Create();
var compiler = new ProfileCompiler(catalog);
var passed = new List<string>();
var failed = new List<string>();
var expected = "set ColdAccessModule_Campfire FuelConsumptionPerSec 0.07\r\nset ColdAccessModule_WeaponsBench UpgradeTime 5\r\n";
var sample = ProfileRecord.CreateDefault(ProfileId.Profile1, catalog).WithValues(catalog.DefaultValues()
    .SetItem(ReferenceCatalog.WeaponUpgradeSpeed, SettingValue.Numeric(4))
    .SetItem(ReferenceCatalog.CampfireFuelDuration, SettingValue.Numeric(2)));

void Test(string name, Action action)
{
    try { action(); passed.Add(name); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Equal<T>(T actual, T target) { if (!EqualityComparer<T>.Default.Equals(actual, target)) throw new Exception($"Expected [{target}], got [{actual}]"); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
string Binaries(string name) { var path = Path.Combine(root, name, "Binaries"); Directory.CreateDirectory(path); return path; }
SettingsCatalog ChangeDefinition(Func<SettingDefinition, SettingDefinition> change, string version = "1.0.0") => new(version, catalog.Definitions.Select(change));
SettingDefinition Weapon() => catalog.ById[ReferenceCatalog.WeaponUpgradeSpeed];
SettingDefinition SingleTarget(SettingDefinition s, Func<ConsoleTarget, ConsoleTarget> change) => s with
{ Backend = new ConsolePropertyBackend(((ConsolePropertyBackend)s.Backend).Targets.Select(change).ToImmutableArray()) };

Test("exact requested two-line example", () => Equal(compiler.Compile(sample).Text, expected));
Test("all Vanilla settings are emitted", () =>
{
    var output = compiler.Compile(ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog));
    Equal(output.Outputs.Length, 2); Check(output.Text.Contains("FuelConsumptionPerSec 0.14\r\n")); Check(output.Text.EndsWith("UpgradeTime 20\r\n"));
});
Test("catalog order does not change bytes", () => Equal(new ProfileCompiler(new("1.0.0", catalog.Definitions.Reverse())).Compile(sample).Text, expected));
Test("locale independence", () =>
{
    var previous = CultureInfo.CurrentCulture;
    try { foreach (var locale in new[] { "es-AR", "de-DE", "en-US" }) { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); Equal(compiler.Compile(sample).Text, expected); } }
    finally { CultureInfo.CurrentCulture = previous; }
});
Test("decimal comma accepted; grouping rejected", () =>
{
    Check(ValueValidator.TryParse(Weapon(), "2,5", out var value, out _)); Equal(value.Number, 2.5m);
    Check(!ValueValidator.TryParse(Weapon(), "1,000.5", out _, out _));
});
Test("invalid numeric inputs rejected", () =>
{
    foreach (var text in new[] { "", "abc", "NaN", "Infinity", "1e2", "0", "-1", "101", "0.15" })
        Check(!ValueValidator.TryParse(Weapon(), text, out _, out _), text + " should fail");
});
Test("only Supported enter profile values", () => Equal(catalog.DefaultValues().Count, 2));
Test("candidate cannot be edited", () => Throws<InvalidOperationException>(() => new ProfileEditSession(catalog, sample).SetRawInput("RefinerySpeed", "2")));
Test("candidate cannot be smuggled into compilation", () => Check(!compiler.Compile(sample.WithValues(sample.Values.Add("RefinerySpeed", SettingValue.Numeric(5)))).Success));
Test("missing supported value blocks compilation", () => Check(!compiler.Compile(sample.WithValues(sample.Values.Remove(ReferenceCatalog.WeaponUpgradeSpeed))).Success));
Test("known console property does not promote Candidate", () =>
{
    Equal(catalog.Visible(false).Count(), 2); Equal(catalog.Visible(true).Count(), 6);
    var dev = EditorProjection.Build(new(catalog, sample), new(true));
    var candidate = dev.Settings.Single(s => s.Id == "RefineryLasers");
    Check(candidate.IsReadOnly && candidate.Input is null && candidate.Outputs.IsEmpty);
    Equal(candidate.DeveloperDetails!.Targets[0].KnownBaseline, "Unknown");
    Check(candidate.DeveloperDetails.Targets[0].GeneratedCommand is null);
});
Test("HookRequired and Unsupported stay hidden even in Developer", () =>
{
    var hidden = catalog.Definitions.First(s => s.SupportStatus == SupportStatus.Candidate) with
    { Id = "FutureHook", SupportStatus = SupportStatus.HookRequired, Backend = new NoBackend(), UnavailableReason = "No hook implementation." };
    var extended = new SettingsCatalog("1.0.0", catalog.Definitions.Add(hidden).Add(hidden with { Id = "NeverSupported", SupportStatus = SupportStatus.Unsupported }));
    Equal(extended.Visible(true).Count(), 6);
});
Test("Supported needs known baseline and backend", () =>
{
    Throws<CatalogException>(() => ChangeDefinition(s => s.Id == Weapon().Id ? s with { Backend = new NoBackend() } : s));
    Throws<CatalogException>(() => ChangeDefinition(s => s.Id == Weapon().Id ? SingleTarget(s, t => t with { VanillaValue = null }) : s));
});
Test("duplicate definitions rejected", () => Throws<CatalogException>(() => new SettingsCatalog("1.0.0", catalog.Definitions.Add(Weapon()))));
Test("duplicate property ownership rejected", () => Throws<CatalogException>(() => new SettingsCatalog("1.0.0", catalog.Definitions.Add(Weapon() with { Id = "DuplicateOwner" }))));
Test("command fragments rejected as property names", () => Throws<CatalogException>(() => ChangeDefinition(s => s.Id == Weapon().Id ? SingleTarget(s, t => t with { PropertyName = "UpgradeTime 5 | quit" }) : s)));
Test("default must reproduce exact baseline", () => Throws<CatalogException>(() => ChangeDefinition(s => s.Id == Weapon().Id ? SingleTarget(s, t => t with { Conversion = Conversion.Direct }) : s)));
Test("rounding is explicit and used by both output and command", () =>
{
    var session = new ProfileEditSession(catalog, sample); session.SetRawInput(ReferenceCatalog.CampfireFuelDuration, "3");
    var view = EditorProjection.Build(session, new());
    var item = view.Settings.Single(s => s.Id == ReferenceCatalog.CampfireFuelDuration).Outputs.Single();
    Equal(item.FormattedValue, "0.046667"); Check(item.Command.EndsWith(" 0.046667"));
    Check(ReferenceEquals(item, view.Compilation.Outputs.Single(o => o.SettingId == ReferenceCatalog.CampfireFuelDuration)));
});
Test("no rounding rule rejects non-representable value", () =>
{
    var exact = ChangeDefinition(s => s.Id == Weapon().Id ? SingleTarget(s, t => t with { Rounding = Rounding.None }) : s);
    var result = new ProfileCompiler(exact).Compile(sample.WithValues(sample.Values.SetItem(Weapon().Id, SettingValue.Numeric(3))));
    Check(!result.Success && result.Outputs.IsEmpty);
});
Test("multiple targets keep baseline ratios", () =>
{
    var weapon = Weapon(); var target = ((ConsolePropertyBackend)weapon.Backend).Targets[0];
    var multi = ChangeDefinition(s => s.Id == weapon.Id ? s with { Backend = new ConsolePropertyBackend([target, target with { PropertyName = "TestOverdriveTime", VanillaValue = SettingValue.Numeric(40), OutputMin = .4m, OutputMax = 400m }]) } : s);
    var output = new ProfileCompiler(multi).Compile(sample);
    Equal(output.Outputs.Length, 3); Equal(output.Outputs.Single(o => o.Target.PropertyName == "TestOverdriveTime").FormattedValue, "10");
});
Test("Choice input uses only explicit options", () =>
{
    var d = Weapon() with { Input = new ChoiceInput([1, 2, 3]), DefaultValue = SettingValue.Numeric(1) };
    Check(ValueValidator.Validate(d, SettingValue.Numeric(2)) is null); Check(ValueValidator.Validate(d, SettingValue.Numeric(2.5m)) is not null);
});
Test("Boolean conversion is typed", () =>
{
    var d = Weapon() with { Input = new ToggleInput(), DefaultValue = SettingValue.Logical(false), Backend = new ConsolePropertyBackend([
        new("TestFixture", "Enabled", SettingValue.Logical(false), Conversion.Direct, OutputKind.Boolean, null, null, null, Rounding.None, "")]) };
    var boolCatalog = new SettingsCatalog("1.0.0", [d]);
    var record = ProfileRecord.CreateDefault(ProfileId.Profile1, boolCatalog);
    Equal(new ProfileCompiler(boolCatalog).Compile(record).Text, "set TestFixture Enabled False\r\n");
});
Test("profile switching retains invalid raw text and draft", () =>
{
    var workspace = new ProfileWorkspace(catalog); workspace.Current.SetRawInput(Weapon().Id, "4"); workspace.Current.SetRawInput(Weapon().Id, "abc");
    workspace.Select(ProfileId.Profile2); workspace.Current.SetRawInput(Weapon().Id, "2"); workspace.Select(ProfileId.Profile1);
    Equal(workspace.Current.RawInputs[Weapon().Id], "abc"); Equal(workspace.Current.Draft.Values[Weapon().Id], SettingValue.Numeric(4));
    Check(workspace.Current.IsDirty && !workspace.Current.ValidationErrors.IsEmpty);
    Check(!EditorProjection.Build(workspace.Current, new(true)).Compilation.Success);
});
Test("Reset category affects only that category", () =>
{
    var session = new ProfileEditSession(catalog, sample); session.ResetCategory("Buildables", "Campfire");
    Equal(session.Draft.Values[ReferenceCatalog.CampfireFuelDuration], SettingValue.Numeric(1)); Equal(session.Draft.Values[Weapon().Id], SettingValue.Numeric(4));
});
Test("Reset profile requires confirmation", () =>
{
    var session = new ProfileEditSession(catalog, sample); session.ResetProfile(false); Check(session.Draft.SameContent(sample));
    session.ResetProfile(true); Check(session.Draft.Values.Values.All(v => v == SettingValue.Numeric(1))); Equal(session.Draft.DisplayName, sample.DisplayName);
});
Test("Vanilla is immutable across API and import", () =>
{
    var vanilla = ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog); var session = new ProfileEditSession(catalog, vanilla);
    Throws<InvalidOperationException>(() => vanilla.Rename("Custom")); Throws<InvalidOperationException>(() => vanilla.WithValues(sample.Values));
    Throws<InvalidOperationException>(() => session.SetRawInput(Weapon().Id, "4")); Throws<InvalidOperationException>(() => session.ResetProfile(true));
    Throws<FormatException>(() => ProfileJson.Deserialize(ProfileJson.Serialize(vanilla)));
    Check(!new ProfileWriter().Save(session, Binaries("vanilla")).Success);
});
Test("rename preserves filename and commands", () =>
{
    var renamed = sample.Rename("Casual \"name\" | text"); Equal(renamed.FileName, "SCS_Profile1.txt"); Equal(compiler.Compile(renamed).Text, expected);
});
Test("Developer mode cannot change profile or generated bytes", () =>
{
    var session = new ProfileEditSession(catalog, sample); var off = EditorProjection.Build(session, new(false)); var on = EditorProjection.Build(session, new(true));
    Equal(off.Compilation.Text, on.Compilation.Text); Check(!session.IsDirty); Check(off.Settings.All(s => s.DeveloperDetails is null));
    Check(on.Settings.Where(s => !s.IsReadOnly).All(s => s.DeveloperDetails!.Evidence.Any(e => e.Source.StartsWith("Runtime validation"))));
});
Test("profile JSON round trip and duplicate rejection", () =>
{
    Check(ProfileJson.Deserialize(ProfileJson.Serialize(sample)).SameContent(sample));
    var json = ProfileJson.Serialize(sample).Replace("\"values\": {", "\"values\": {\"WeaponUpgradeSpeed\": 1,");
    Throws<FormatException>(() => ProfileJson.Deserialize(json));
});
Test("catalog version mismatch blocks ordinary compilation", () =>
{
    var future = new SettingsCatalog("2.0.0", catalog.Definitions); Check(!new ProfileCompiler(future).Compile(sample).Success);
});
Test("migration adds defaults, removes obsolete, preserves retained values", () =>
{
    var additional = SingleTarget(Weapon() with { Id = "NewSetting" }, t => t with { ClassName = "TestFixture" });
    var next = new SettingsCatalog("2.0.0", catalog.Definitions.Where(s => s.Id != ReferenceCatalog.CampfireFuelDuration).Append(additional));
    var migration = ProfileMigrator.Migrate(sample, next); Check(migration.Success && migration.RequiresSave);
    Equal(migration.Profile!.Values["NewSetting"], SettingValue.Numeric(1)); Equal(migration.Profile.Values[Weapon().Id], SettingValue.Numeric(4));
    Check(!migration.Profile.Values.ContainsKey(ReferenceCatalog.CampfireFuelDuration)); Check(sample.Values.ContainsKey(ReferenceCatalog.CampfireFuelDuration));
    Equal(migration.Profile.FileName, sample.FileName); Equal(migration.Profile.DisplayName, sample.DisplayName);
});
Test("migration never clamps retained invalid values", () =>
{
    var old = sample.WithValues(sample.Values.SetItem(Weapon().Id, SettingValue.Numeric(100)));
    var next = ChangeDefinition(s => s.Id == Weapon().Id ? s with { Input = new MultiplierInput(.1m, 10, .1m, [.1m, 1, 5, 10]) } : s, "2.0.0");
    var migration = ProfileMigrator.Migrate(old, next); Check(!migration.Success && migration.Profile is null); Equal(old.Values[Weapon().Id], SettingValue.Numeric(100));
});
Test("automatic downgrade is rejected", () =>
{
    var future = ProfileRecord.CreateCustom(sample.Id, sample.DisplayName, "2.0.0", sample.Values);
    Check(!ProfileMigrator.Migrate(future, catalog).Success);
});
Test("Vanilla is rebuilt during migration", () =>
{
    var next = new SettingsCatalog("2.0.0", catalog.Definitions);
    var result = ProfileMigrator.Migrate(ProfileRecord.CreateDefault(ProfileId.Vanilla, catalog), next);
    Check(result.Success); Equal(result.Profile!.CatalogVersion, "2.0.0"); Check(result.Profile.IsReadOnly);
});
Test("write produces exact ASCII CRLF bytes without BOM", () =>
{
    var session = new ProfileEditSession(catalog, ProfileRecord.CreateDefault(ProfileId.Profile1, catalog));
    session.SetRawInput(Weapon().Id, "4"); session.SetRawInput(ReferenceCatalog.CampfireFuelDuration, "2");
    var result = new ProfileWriter().Save(session, Binaries("save")); Check(result.Success); Equal(session.State, SaveState.Clean);
    Check(File.ReadAllBytes(result.File!.Path).SequenceEqual(Encoding.ASCII.GetBytes(expected)));
});
Test("invalid raw input cannot write or clear Dirty", () =>
{
    var folder = Binaries("invalid-raw"); var session = new ProfileEditSession(catalog, sample); session.SetRawInput(Weapon().Id, "oops");
    Check(!new ProfileWriter().Save(session, folder).Success); Check(session.IsDirty); Equal(Directory.GetFiles(folder).Length, 0);
});
Test("temp is a closed sibling; failed commit preserves old file and draft", () =>
{
    var folder = Binaries("failed-commit"); var destination = Path.Combine(folder, sample.FileName); File.WriteAllText(destination, "OLD");
    var session = new ProfileEditSession(catalog, ProfileRecord.CreateDefault(ProfileId.Profile1, catalog)); session.SetRawInput(Weapon().Id, "4");
    var writer = new AtomicFileWriter(new ProbeCommitter((temp, final, _) =>
    {
        Equal(Path.GetDirectoryName(temp), Path.GetDirectoryName(final));
        using (var file = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.None)) Check(file.Length > 0);
        Check(File.ReadAllText(temp).Contains("UpgradeTime 5")); throw new IOException("Simulated commit failure");
    }));
    var result = new ProfileWriter(writer).Save(session, folder); Check(!result.Success); Equal(File.ReadAllText(destination), "OLD");
    Check(session.IsDirty); Equal(session.State, SaveState.SaveFailed); Equal(session.RawInputs[Weapon().Id], "4");
    Equal(Directory.GetFiles(folder, "*.tmp").Length, 0);
});
Test("successful replacement changes the complete file", () =>
{
    var folder = Binaries("replacement"); var file = Path.Combine(folder, sample.FileName); File.WriteAllText(file, "OLD CONTENT LONGER THAN NEW");
    Check(new AtomicFileWriter().WriteConsoleText(file, expected).Success); Equal(File.ReadAllText(file), expected);
});
Test("read-only destination classified separately from permission denial", () =>
{
    var folder = Binaries("read-only"); var file = Path.Combine(folder, sample.FileName); File.WriteAllText(file, "OLD"); File.SetAttributes(file, FileAttributes.ReadOnly);
    try { var result = new AtomicFileWriter().WriteConsoleText(file, expected); Equal(result.Failure!.Kind, FileFailureKind.NotWritable); Equal(File.ReadAllText(file), "OLD"); }
    finally { File.SetAttributes(file, FileAttributes.Normal); }
    var denied = new AtomicFileWriter(new ProbeCommitter((_, _, _) => throw new UnauthorizedAccessException("Simulated ACL denial"))).WriteConsoleText(file, expected);
    Equal(denied.Failure!.Kind, FileFailureKind.PermissionDenied); Equal(File.ReadAllText(file), "OLD");
});
Test("InstallMissing preserves existing custom and Vanilla files", () =>
{
    var folder = Binaries("install"); File.WriteAllText(Path.Combine(folder, "SCS_Profile1.txt"), "EXISTING CUSTOM"); File.WriteAllText(Path.Combine(folder, "SCS_Vanilla.txt"), "EXISTING VANILLA");
    var custom = Enum.GetValues<ProfileId>().Where(id => id != ProfileId.Vanilla).Select(id => ProfileRecord.CreateDefault(id, catalog));
    var result = new InstallationService().InstallMissing(catalog, custom, folder); Check(result.Success); Equal(result.Files.Length, 5);
    Equal(File.ReadAllText(Path.Combine(folder, "SCS_Profile1.txt")), "EXISTING CUSTOM"); Equal(File.ReadAllText(Path.Combine(folder, "SCS_Vanilla.txt")), "EXISTING VANILLA");
    Equal(result.Files.Count(r => r.Status == WriteStatus.AlreadyExists), 2); Equal(Directory.GetFiles(folder, "*.txt").Length, 5);
});
Test("preserve wins even if a file appears during commit", () =>
{
    var folder = Binaries("race"); var file = Path.Combine(folder, "SCS_Profile1.txt");
    var writer = new AtomicFileWriter(new ProbeCommitter((temp, final, behavior) => { File.WriteAllText(final, "OTHER WRITER"); new AtomicCommitter().Commit(temp, final, behavior); }));
    Equal(writer.WriteConsoleText(file, expected, ExistingFileBehavior.Preserve).Status, WriteStatus.AlreadyExists);
    Equal(File.ReadAllText(file), "OTHER WRITER"); Equal(Directory.GetFiles(folder, "*.tmp").Length, 0);
});
Test("invalid installation and missing path are distinct", () =>
{
    var service = new InstallationService(); Check(service.Verify(null).Status == InstallationStatus.Unconfigured);
    Equal(service.Verify(root).Failure!.Kind, FileFailureKind.WrongFolder);
    Equal(service.Verify(Path.Combine(root, "missing", "Binaries")).Failure!.Kind, FileFailureKind.NotFound);
    var valid = Binaries("verify"); Check(service.Verify(valid).Ready); Equal(Directory.GetFiles(valid).Length, 0);
});
Test("detection reads synthetic Steam libraries without a game process", () =>
{
    var steam = Path.Combine(root, "steam"); var library = Path.Combine(root, "second-library");
    Directory.CreateDirectory(Path.Combine(steam, "steamapps")); var target = Path.Combine(library, "steamapps", "common", "Subsistence", "Binaries"); Directory.CreateDirectory(target);
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
    var detected = InstallationDetector.Detect([steam]); Equal(detected.Length, 1); Equal(detected[0].BinariesPath, target);
});
Test("hotkey defaults are deterministic and target fixed filenames", () =>
{
    var keys = new HotkeyCompiler(); var output = keys.Compile(HotkeyCompiler.Defaults.Reverse()); Check(output.Success);
    Check(output.Text.StartsWith("setbind F6 \"exec SCS_Profile1.txt\"\r\n")); Check(output.Text.EndsWith("setbind F10 \"exec SCS_Vanilla.txt\"\r\n")); Equal(output.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length, 5);
});
Test("hotkey duplicates block; potential conflicts only warn", () =>
{
    var keys = new HotkeyCompiler(); var defaults = HotkeyCompiler.Defaults;
    var duplicate = keys.Compile(defaults.SetItem(0, new(ProfileId.Profile1, "F7"))); Check(!duplicate.Success); Check(duplicate.Issues.Any(i => i.Code == "DuplicateHotkey" && i.Message.Contains("F7")));
    var warning = keys.Compile(defaults.SetItem(0, new(ProfileId.Profile1, "F1"))); Check(warning.Success && warning.Warnings.Length == 1);
    Check(!keys.Compile(defaults.SetItem(0, new(ProfileId.Profile1, "F99"))).Success);
});
Test("hotkey injection tokens are rejected", () => Throws<ArgumentException>(() => new HotkeyCompiler([new("danger", "Danger", "F6 | quit", false)])));
Test("failed hotkey generation preserves saved bindings and old file", () =>
{
    var folder = Binaries("hotkeys"); var session = new HotkeyEditSession(new()); Check(session.Generate(folder).Success);
    var old = File.ReadAllBytes(Path.Combine(folder, "SCS_Bindings.txt")); session.SetKey(ProfileId.Profile1, "F1");
    var result = session.Generate(folder, new AtomicFileWriter(new ProbeCommitter((_, _, _) => throw new IOException("Simulated failure"))));
    Check(!result.Success && session.IsDirty); Equal(session.SavedAssignments[0].KeyId, "F6"); Check(File.ReadAllBytes(Path.Combine(folder, "SCS_Bindings.txt")).SequenceEqual(old));
    Equal(session.SetupFileState, HotkeySetupState.WriteFailed);
});
Test("changing installation marks binding output stale", () =>
{
    var session = new HotkeyEditSession(new()); Check(session.Generate(Binaries("bindings-location-a")).Success);
    session.InstallationChanged(Binaries("bindings-location-b")); Equal(session.SetupFileState, HotkeySetupState.Stale);
});

Test("malformed profile metadata reports a controlled format error", () =>
{
    Throws<FormatException>(() => ProfileJson.Deserialize("{}"));
    Throws<FormatException>(() => ProfileJson.Deserialize("[]"));
    Throws<FormatException>(() => ProfileJson.Deserialize("{\"schemaVersion\":1,\"profileId\":0}"));
    Throws<FormatException>(() => ProfileJson.Deserialize("{\"schemaVersion\":1,\"profileId\":\"0\"}"));
});

V2Cases.Run(Test, root);
V206Cases.Run(Test, root);
var report = new { passed = passed.Count, failed = failed.Count, checks = passed, failures = failed, scratch = root };
File.WriteAllText(Path.Combine(root, "test-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"TOTAL: {passed.Count} passed; {failed.Count} failed");
Console.WriteLine("REPORT: " + Path.Combine(root, "test-results.json"));
return failed.Count == 0 ? 0 : 1;

sealed class ProbeCommitter(Action<string, string, ExistingFileBehavior> action) : IAtomicCommitter
{
    public void Commit(string temporaryPath, string destinationPath, ExistingFileBehavior behavior) => action(temporaryPath, destinationPath, behavior);
}
