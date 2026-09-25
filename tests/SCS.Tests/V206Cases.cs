using System.Text;
using System.Text.Json;
using SCS.Core;

internal static class V206Cases
{
    private const string Parent = "[Engine.PlayerInput]\r\nBindings=(Name=\"W\",Command=\"GBA_MoveForward\")\r\nBindings=(Name=\"A\",Command=\"GBA_StrafeLeft\")\r\nBindings=(Name=\"S\",Command=\"GBA_Backward\")\r\nBindings=(Name=\"D\",Command=\"GBA_StrafeRight\")\r\nBindings=(Name=\"LeftMouseButton\",Command=\"GBA_Fire\")\r\n";
    private const string Child = "[ColdGame.ColdPlayerInput]\r\nbIsSprintToggle=TRUE\r\nbIsCrouchToggle=TRUE\r\nbIsAimDownSightsToggle=FALSE\r\n";
    private static void Check(bool condition, string message = "Regression") { if (!condition) throw new Exception(message); }
    public static void Run(Action<string, Action> test, string root)
    {
        HotkeyAssignment[] keys = [new(ProfileId.Profile1,"Home"),new(ProfileId.Profile2,"Insert"),new(ProfileId.Profile3,"PageDown"),new(ProfileId.Profile4,"PageUp"),new(ProfileId.Vanilla,"F10")];
        string Lines(string op = "") => string.Join("\r\n", keys.Select(a => op + IniHotkeys.BindingLine(a))) + "\r\n";
        InputIniDocument Doc(string text) => new(Encoding.UTF8.GetBytes(text));
        string Fixture(string text)
        {
            var dir = Path.Combine(root,"paired-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            var file=Path.Combine(dir,"UDKInput.ini");File.WriteAllText(file,text,new UTF8Encoding(false));return file;
        }
        test("clean INI produces exact plain parent and dotted child pairs preserving WASD mouse and flags", () =>
        {
            var original=Doc(Parent+Child);var installed=new InputIniDocument(original.Install(keys));
            Check(installed.Text.Contains(Parent));Check(installed.Text.Contains(Child));
            Check(installed.Text.Contains(Lines()));Check(installed.Text.Contains(Lines(".")));
            Check(installed.Bindings.Where(b=>b.IsScs&&b.Section==InputIniDocument.ChildSection).All(b=>b.Operator=="."));
            Check(keys.All(k=>installed.InstalledKey(k.ProfileId)==k.KeyId));
            Check(IniHotkeyEditSession.Restore(keys,null).Installed(installed));
            Check(installed.RemoveScs().SequenceEqual(original.OriginalBytes));
        });
        test("manually installed dotted pairs are valid and Verify is a byte-identical no-op", () =>
        {
            var text=Parent+Lines()+Child+Lines(".");var file=Fixture(text);
            Check(!Doc(text).NeedsRepair);Check(Doc(text).HasCompleteLayout);
            var result=new IniHotkeyService().MigrateLegacy(file);
            Check(result.Success&&!result.Changed);Check(File.ReadAllText(file)==text);
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(file)!,"UDKInput.scs-backup.ini")));
        });
        test("single-section legacy forms migrate with identical keys and removal restores clean input", () =>
        {
            foreach(var text in new[]{Parent+Lines()+Child,Parent+Child+Lines(),Parent+Child+Lines("."),Parent+Lines()+Child+Lines()})
            {
                var file=Fixture(text);Check(Doc(text).NeedsRepair);
                var service=new IniHotkeyService();var result=service.MigrateLegacy(file);Check(result.Success&&result.Changed,result.Message);
                var migrated=new InputIniDocument(File.ReadAllBytes(file));Check(migrated.HasCompleteLayout);
                Check(keys.All(k=>migrated.InstalledKey(k.ProfileId)==k.KeyId));Check(!service.MigrateLegacy(file).Changed);
                Check(service.Remove(file).Success);Check(File.ReadAllText(file)==Parent+Child);
            }
        });
        test("mixed old duplicates collapse to ten and dotted foreign near-matches survive", () =>
        {
            const string foreign=".Bindings=(Name=\"F6\",Command=\"exec SCS_Test.txt\")\r\n.Bindings=(Name=\"F7\",Command=\"exec SCS_Profile1.txt | Jump\")\r\n";
            var doc=Doc(Parent+Lines()+Lines(".")+Child+foreign+Lines()+Lines(".")+Lines("+"));
            var installed=new InputIniDocument(doc.Install(keys));Check(installed.HasCompleteLayout);
            Check(installed.RemoveScs().SequenceEqual(Encoding.UTF8.GetBytes(Parent+Child+foreign)));
        });
        test("wrong operators missing counterparts mismatched keys and duplicate profiles are not installed", () =>
        {
            foreach(var text in new[]{Parent+Lines()+Child+Lines(),Parent+Lines(".")+Child+Lines("."),Parent+Lines()+Child,
                Parent+Lines()+Child+Lines(".").Replace("Name=\"Home\"","Name=\"F12\""),Parent+Lines()+Child+Lines(".")+"."+IniHotkeys.BindingLine(keys[0])+"\r\n"})
            {
                Check(Doc(text).NeedsRepair);Check(!IniHotkeyEditSession.Restore(keys,null).Installed(Doc(text)));
            }
        });
        test("ambiguous pair migration refuses writes but explicit user selection resolves it", () =>
        {
            var text=Parent+Lines()+Child+Lines(".").Replace("Name=\"Home\"","Name=\"F12\"");var file=Fixture(text);
            var service=new IniHotkeyService();Check(!service.MigrateLegacy(file).Success);Check(File.ReadAllText(file)==text);
            Check(service.Install(file,keys).Success);Check(new InputIniDocument(File.ReadAllBytes(file)).HasCompleteLayout);
        });
        test("foreign dotted binding conflicts block installation without changing bytes", () =>
        {
            var text=Parent+Child+".Bindings=(Name=\"Home\",Command=\"Unrelated\")\r\n";var file=Fixture(text);
            Check(!new IniHotkeyService().Install(file,keys).Success);Check(File.ReadAllText(file)==text);
        });
        test("reversed sections LF CRLF encodings and no-final-newline roundtrip with both blocks", () =>
        {
            foreach(var encoding in new Encoding[]{new UTF8Encoding(false),new UTF8Encoding(true),Encoding.Unicode,Encoding.BigEndianUnicode,Encoding.Latin1})
            foreach(var newline in new[]{"\n","\r\n"})
            foreach(var order in new[]{Parent+Child,Child+Parent})
            {
                var text=("; café\r\n"+order).Replace("\r\n",newline).TrimEnd('\r','\n');
                var bytes=encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
                var installed=new InputIniDocument(new InputIniDocument(bytes).Install(keys));
                Check(installed.HasCompleteLayout);Check(installed.RemoveScs().SequenceEqual(bytes),encoding.WebName+newline);
                Check(installed.Install(keys).SequenceEqual(installed.OriginalBytes));
            }
        });
        test("each missing required section blocks installation and preserves existing text", () =>
        {
            foreach(var text in new[]{Parent,Child}){var file=Fixture(text);Check(!new IniHotkeyService().Install(file,keys).Success);Check(File.ReadAllText(file)==text);}
        });
        test("update journals record ten section-aware lines and removal clears both operators", () =>
        {
            var file=Fixture(Parent+Child);var service=new IniHotkeyService();Check(service.Install(file,keys).Success);
            var journal=Directory.GetFiles(Path.GetDirectoryName(file)!,"UDKInput.scs-operation-*.json").Single();
            using var data=JsonDocument.Parse(File.ReadAllText(journal));var added=data.RootElement.GetProperty("AddedLines").EnumerateArray().ToArray();
            Check(added.Length==10);Check(added.Count(l=>l.GetProperty("Text").GetString()!.StartsWith(".Bindings="))==5);
            keys[0]=new(ProfileId.Profile1,"F12");Check(service.Install(file,keys).Success);
            Check(new InputIniDocument(File.ReadAllBytes(file)).InstalledKey(ProfileId.Profile1)=="F12");
            Check(service.Remove(file).Success);Check(File.ReadAllText(file)==Parent+Child);
        });
    }
}
