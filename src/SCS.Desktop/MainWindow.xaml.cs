using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SCS.Core;

namespace SCS.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsCatalog catalog = SupportedCatalog.Create();
    private readonly Dictionary<ProfileId, ProfileEditSession> sessions = [];
    private readonly Dictionary<ProfileId, ProfileRecord> persisted = [];
    private readonly HashSet<ProfileId> migrated = [];
    private readonly WorkspaceStore store;
    private IniHotkeyEditSession hotkeys = new();
    private ProfileId selected = ProfileId.Profile1;
    private string screen = "Overview", binaries = "", savedBinaries = "";
    private bool HasUnsavedChanges => sessions.Values.Any(s=>s.IsDirty) || hotkeys.IsDirty || migrated.Count>0 || metadataPending || binaries!=savedBinaries;
    private bool rendering, metadataPending, smokeTesting;
    private GameVerification verification = new(null, false, false, false, false, "Choose an installation.");
    private InputIniDocument? inputIni;
    private string iniError = "Choose and verify an installation first.";
    private readonly Dictionary<string, TextBox> settingInputs = [];
    private readonly Dictionary<string, Action> refreshSettings = [];
    private readonly string[] categories = ["Gathering", "Farming", "Animals", "Crafting", "Traps", "Weapons Bench", "Campfire", "BCU", "Power Generator", "Mass Fabricator", "Refinery"];
    private ProfileEditSession Current => sessions[selected];
    private bool Gameplay => categories.Contains(screen);
    private Brush B(string key) => (Brush)FindResource(key);
    public MainWindow(WorkspaceStore store)
    {
        this.store = store;
        foreach (var id in Enum.GetValues<ProfileId>()) { var p = ProfileRecord.CreateDefault(id, catalog); sessions[id] = new(catalog, p); persisted[id] = p; }
        var data = store.Load();
        if (data is not null)
        {
            var profiles = data.Profiles.Select(p => ProfileJson.Deserialize(p.GetRawText())).ToArray();
            if (profiles.Length != 4 || profiles.Select(p => p.Id).Distinct().Count() != 4) throw new FormatException("Four distinct custom profiles are required.");
            foreach (var original in profiles)
            {
                var result = ProfileMigrator.Migrate(original, catalog);
                if (!result.Success) throw new FormatException(string.Join("\n", result.Issues.Select(i => i.Message)));
                persisted[original.Id] = original; sessions[original.Id] = new(catalog, result.Profile!);
                if (result.RequiresSave) migrated.Add(original.Id);
            }
            selected = data.SelectedProfile; binaries = data.BinariesPath; savedBinaries=binaries;
            hotkeys = IniHotkeyEditSession.Restore(data.Hotkeys, data.GeneratedBindingsPath);
        }
        InitializeComponent();
        SaveAllButton.Click+=(_,_)=>SaveAll();
        ProfilePicker.SelectionChanged += (_, _) => { if (!rendering && ProfilePicker.SelectedItem is ComboBoxItem item) { selected = (ProfileId)item.Tag; Render(); } };
        Closing += (_, e) =>
        {
            if(smokeTesting || !HasUnsavedChanges) return;
            var dialog=new CloseChangesDialog { Owner=this };
            dialog.ShowDialog();
            e.Cancel=!ResolveClose(dialog.Choice);
        };
        if(binaries.Length>0) Guard(()=>{verification=GameInstallation.Verify(binaries);if(verification.ProfilesReady && File.Exists(Path.Combine(binaries,"SCS_Vanilla.txt")))new ManagedProfiles().MaintainVanilla(catalog,binaries);});
        if(verification.Ready) { var repair=new IniHotkeyService().MigrateLegacy(verification.Paths!.InputIni); if(!repair.Success) screen="Hotkeys"; }
        ReadIni(); Render();
    }
    private TextBlock Text(string text, double size = 14, string color = "Ink") => new() { Text = text, FontSize = size, Foreground = B(color), Margin = new(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private Button ActionButton(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { b.Background = B("Accent"); b.Foreground = B("Nav"); }
        b.Click += (_, _) => Guard(action); return b;
    }
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        { Notice(e.Message, true); }
    }
    private void Notice(string text, bool error = false) { StatusText.Text = text; StatusText.Foreground = error ? new SolidColorBrush(Color.FromRgb(255, 180, 173)) : B("Accent"); }
    private StackPanel Card()
    {
        var body = new StackPanel(); PageContent.Children.Add(new Border { Child = body, Background = B("Panel"), BorderBrush = B("Line"), BorderThickness = new(1), CornerRadius = new(8), Padding = new(22), Margin = new(0, 0, 0, 16) }); return body;
    }
    private void PageTitle(string title, string description)
    {
        PageContent.Children.Add(Text(Gameplay ? "GAMEPLAY" : "WORKSPACE", 11, "Muted"));
        var heading = Text(title, 28); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new(0, 5, 0, 8); PageContent.Children.Add(heading);
        var intro = Text(description, 13, "Muted"); intro.Margin = new(0, 0, 0, 23); PageContent.Children.Add(intro);
    }
    private bool CanInstallHotkeys => verification.Ready && inputIni is not null && IniHotkeys.Validate(hotkeys.DraftAssignments,inputIni).IsEmpty;
    private string? InstalledKey(ProfileId id)
    {
        return verification.Ready ? inputIni?.InstalledKey(id) : null;
    }
    private string HotkeyState(ProfileId id) => InstalledKey(id) is string key ? "Installed hotkey: " + key : "Hotkey not installed";
    private bool SetupReady => verification.Ready && inputIni is { NeedsRepair:false } && Enum.GetValues<ProfileId>().All(id=>InstalledKey(id) is not null)
        && ManagedProfiles.Names.All(n=>File.Exists(Path.Combine(binaries,n))) && VanillaMaintenance.Current(catalog,binaries);
    private string Key(ProfileId id) => string.IsNullOrEmpty(hotkeys.SavedAssignments.First(k => k.ProfileId == id).KeyId) ? "Not assigned" : hotkeys.SavedAssignments.First(k => k.ProfileId == id).KeyId;
    private static string Slot(ProfileId id) => id == ProfileId.Vanilla ? "Vanilla · Read-only" : $"Profile {(int)id}";
    private void Header()
    {
        rendering = true; ProfilePicker.Items.Clear();
        foreach (var (id, s) in sessions)
        {
            var item = new ComboBoxItem { Content = s.Draft.DisplayName + (s.IsDirty || migrated.Contains(id) ? "  •" : ""), Tag = id };
            ProfilePicker.Items.Add(item); if (id == selected) ProfilePicker.SelectedItem = item;
        }
        EditingLabel.Text = Current.Draft.IsReadOnly ? "Viewing profile" : "Editing profile";
        IdentityLabel.Text = Slot(selected) + " · " + HotkeyState(selected);
        InstallBadge.Text = verification.Ready ? "✓ Game paths verified" : binaries.Length == 0 ? "○ Installation not configured" : "○ Verification required";
        rendering = false;
    }
    private void Navigate(string page) { screen = page; ReadIni(); Render(); PageScroll.ScrollToTop(); }
    private void Render()
    {
        Header(); Navigation.Children.Clear(); PageContent.Children.Clear(); settingInputs.Clear(); refreshSettings.Clear();
        void Nav(string name)
        {
            var button = ActionButton(name, () => Navigate(name)); button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new(0, 1, 0, 1); button.Padding = new(12, 7, 12, 7); button.BorderThickness = new(0);
            button.Background = screen == name ? B("Soft") : B("Nav"); button.Foreground = screen == name ? B("Accent") : B("Muted"); Navigation.Children.Add(button);
        }
        void Group(string name) { var text = Text(name, 10, "Muted"); text.Margin = new(12, 18, 0, 6); Navigation.Children.Add(text); }
        Nav("Overview"); Group("GAMEPLAY"); foreach (var category in categories) Nav(category);
        Group("WORKSPACE"); foreach (var item in new[] { "Profiles", "Hotkeys", "Installation", "App settings" }) Nav(item);
        switch (screen)
        {
            case "Overview": Overview(); break;
            case "Profiles": Profiles(); break;
            case "Hotkeys": Hotkeys(); break;
            case "Installation": Installation(); break;
            case "App settings": AppSettings(); break;
            default: Settings(); break;
        }
        Footer();
    }
    internal bool ResolveClose(CloseChoice choice) => choice switch
    {
        CloseChoice.Discard => true,
        CloseChoice.Save => SaveAll() && !HasUnsavedChanges,
        _ => false
    };
    private void Footer()
    {
        SaveAllButton.IsEnabled=HasUnsavedChanges; GlobalDirty.Text=HasUnsavedChanges?"Unsaved changes":"";
        FooterActions.Children.Clear(); StatusText.Foreground = B("Muted"); StatusText.Text = "";
        if (Gameplay || screen == "Profiles")
        {
            StatusText.Text = Current.Draft.IsReadOnly ? "🔒 Vanilla · Read-only" : !Current.ValidationErrors.IsEmpty ? "Correct the highlighted values before saving." : migrated.Contains(selected) ? "New catalogue defaults added · Save to update this profile" : Current.IsDirty ? "● Unsaved changes" : "Saved values · Use the profile hotkey in-game";
            if (!Current.Draft.IsReadOnly)
            {
                FooterActions.Children.Add(ActionButton(screen == "Profiles" ? "Reset profile" : "Reset category", () =>
                {
                    if (screen == "Profiles") Current.ResetProfile(MessageBox.Show(this, "Reset all settings in this profile to Vanilla?", "Reset profile", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
                    else Current.ResetCategory(screen); Render();
                }));
                var save = ActionButton("Save profile", SaveProfile, true); save.IsEnabled = Current.ValidationErrors.IsEmpty; FooterActions.Children.Add(save);
            }
        }
        else if (screen == "Hotkeys")
        {
            var issues = IniHotkeys.Validate(hotkeys.DraftAssignments, inputIni);
            var button = ActionButton(inputIni is not null && hotkeys.Installed(inputIni) ? "Update hotkeys" : "Install hotkeys", InstallHotkeys, true);
            button.IsEnabled = CanInstallHotkeys && ManagedProfiles.Names.All(n=>File.Exists(Path.Combine(binaries,n))); FooterActions.Children.Add(button);
            StatusText.Text = inputIni is null ? iniError : issues.Length > 0 ? string.Join(" · ", issues.Select(i=>i.Message)) : hotkeys.IsDirty ? "● Pending hotkey changes" : hotkeys.Installed(inputIni) ? "Hotkeys installed in INI · Pending in-game use" : "Hotkeys can be saved while the game is running";
        }
        else if (screen == "Installation")
        {
            FooterActions.Children.Add(ActionButton("Verify", Verify));
            var combined = CanInstallHotkeys;
            var install = ActionButton(combined ? "Install missing profiles and hotkeys" : "Install missing profiles only", combined ? Install : InstallProfiles, true);
            install.IsEnabled = verification.ProfilesReady; FooterActions.Children.Add(install);
            StatusText.Text = !verification.ProfilesReady ? verification.Message : combined ? "Paths ready" : "Profiles can be installed. Hotkeys require valid assignments and verified paths.";
        }
        else StatusText.Text = screen == "App settings" ? "Save all stores pending app preferences locally" : "Save profiles here. Use their hotkeys inside Subsistence.";
        if (inputIni?.NeedsRepair == true) Notice("Input configuration needs repair. Verify the installation or choose five keys and install hotkeys.",true);
        if (metadataPending) Notice("Profile changes may be saved, but workspace metadata is pending. Retry in App settings.", true);
    }
    private void Overview()
    {
        PageTitle("Your game. Your settings.", "A complete profile for each play style, with Vanilla always within reach.");
        var c = Card(); c.Children.Add(Text("More ways to shape your game", 19)); c.Children.Add(Text("Gathering, harvesting, butchering, crafting, traps, wildlife and production. Every available control writes a working profile setting from the corrected catalogue.", 13, "Muted"));
        c.Children.Add(ActionButton("Explore Gathering", () => Navigate("Gathering"), true));
        c = Card();
        foreach (var (title, detail) in new[] { ("01  Choose your installation", "Verify the game folder, profile location and input configuration."), ("02  Install profiles and hotkeys", "Existing bindings and profiles are preserved. Hotkeys can be saved while playing."), ("03  Save and play", "Save gameplay changes while playing, then use the profile hotkey to load them. No console setup is required.") })
        { c.Children.Add(Text(title, 16)); var t = Text(detail, 13, "Muted"); t.Margin = new(24, 0, 0, 16); c.Children.Add(t); }
        c.Children.Add(ActionButton("Installation", () => Navigate("Installation")));
    }
    private void Settings()
    {
        PageTitle(screen, "Choose gameplay values. Each control shows its own limits; ×1 is Vanilla.");
        foreach (var view in EditorProjection.Build(Current, new(false)).Settings.Where(v => v.CategoryPath[0] == screen)) SettingCard(view);
    }
    private void SettingCard(SettingEditorView view)
    {
        var c = Card(); c.Children.Add(Text(view.Name, 18)); c.Children.Add(Text(view.Description, 13, "Muted"));
        var definition = catalog.ById[view.Id];
        var output = Text("", 13); var errors = Text("", 12); errors.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 173));
        if (view.Input is ToggleInput)
        {
            var toggle = new CheckBox { Content = "Enabled — passive unless provoked", IsChecked = Current.Draft.Values[view.Id].Boolean, IsEnabled = !view.IsReadOnly };
            toggle.Click += (_, _) => { Current.SetRawInput(view.Id, (toggle.IsChecked == true).ToString()); refreshSettings[view.Id](); Header(); Footer(); };
            c.Children.Add(toggle);
        }
        else
        {
            var control = new Grid { Margin = new(0, 10, 0, 8) }; control.ColumnDefinitions.Add(new()); control.ColumnDefinitions.Add(new() { Width = new GridLength(view.Input is NumberInput ? 156 : 114) });
            var input = new TextBox { Text = view.DisplayValue, IsReadOnly = view.IsReadOnly, FontSize = 17, Margin = new(20, 5, 0, 0), Padding = new(view.Input is MultiplierInput ? 24 : 10, 8, 8, 8), Height = 42, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(input, 1); control.Children.Add(input); settingInputs[view.Id] = input;
            System.Windows.Automation.AutomationProperties.SetName(input, view.Name);
            if (view.Input is MultiplierInput m)
            {
                var sign = Text("×", 15, "Muted"); sign.Margin = new(28, 13, 0, 0); sign.IsHitTestVisible = false; Grid.SetColumn(sign, 1); control.Children.Add(sign);
                var left = new StackPanel(); var syncing = false;
                double Position(decimal value) { for (var i = 0; i < m.Anchors.Length - 1; i++) if (value <= m.Anchors[i+1]) return i + (double)((value-m.Anchors[i])/(m.Anchors[i+1]-m.Anchors[i])); return m.Anchors.Length - 1; }
                var slider = new Slider { Minimum = 0, Maximum = m.Anchors.Length - 1, Value = Position(Current.Draft.Values[view.Id].Number!.Value), IsEnabled = !view.IsReadOnly, SmallChange = .1, LargeChange = 1 };
                left.Children.Add(slider); var marks = new Canvas { Height = 24 };
                foreach (var a in m.Anchors) { var t = Text(a == 1 ? "1 · Vanilla" : a.ToString(CultureInfo.InvariantCulture), 10, a == 1 ? "Accent" : "Muted"); t.TextWrapping = TextWrapping.NoWrap; marks.Children.Add(t); }
                marks.SizeChanged += (_, _) => { for (var i = 0; i < marks.Children.Count; i++) { var t = (TextBlock)marks.Children[i]; t.Measure(new Size(double.PositiveInfinity, 24)); Canvas.SetLeft(t, Math.Clamp(6+(marks.ActualWidth-12)*i/(m.Anchors.Length-1)-t.DesiredSize.Width/2, 0, Math.Max(0, marks.ActualWidth-t.DesiredSize.Width))); } };
                left.Children.Add(marks); control.Children.Add(left);
                slider.ValueChanged += (_, _) => { if (syncing) return; var i = Math.Min((int)slider.Value, m.Anchors.Length-2); var number = m.Anchors[i]+(m.Anchors[i+1]-m.Anchors[i])*(decimal)(slider.Value-i); number = Math.Clamp(m.Min+decimal.Round((number-m.Min)/m.Step, 0, MidpointRounding.AwayFromZero)*m.Step, m.Min, m.Max); input.Text = number.ToString(CultureInfo.InvariantCulture); };
                input.TextChanged += (_, _) => { if (ValueValidator.TryParse(definition, input.Text, out var value, out _)) { syncing=true; slider.Value=Position(value.Number!.Value); syncing=false; } };
                c.Children.Add(Text($"Range: ×{m.Min.ToString(CultureInfo.InvariantCulture)}–×{m.Max.ToString(CultureInfo.InvariantCulture)}", 11, "Muted"));
            }
            else if (view.Input is NumberInput n)
            {
                var label = Text($"{n.Min:N0}–{n.Max:N0} {n.Unit}", 12, "Muted"); label.VerticalAlignment=VerticalAlignment.Center; control.Children.Add(label);
            }
            input.TextChanged += (_, _) =>
            {
                if (view.IsReadOnly) return; Current.SetRawInput(view.Id, input.Text); foreach (var update in refreshSettings.Values) update(); Header(); Footer();
            };
            c.Children.Add(control);
        }
        c.Children.Add(output); c.Children.Add(errors);
        var meta = new DockPanel(); var reset = ActionButton("Reset", () => { Current.ResetSetting(view.Id); Render(); }); reset.IsEnabled = !view.IsReadOnly; DockPanel.SetDock(reset, Dock.Right); meta.Children.Add(reset);
        meta.Children.Add(Text(view.Input is MultiplierInput ? "Vanilla: ×1" : view.Input is ToggleInput ? "Vanilla: OFF" : "Vanilla: " + definition.DefaultValue!.Value.Format(), 12, "Muted")); c.Children.Add(meta);
        refreshSettings[view.Id] = () =>
        {
            var projection = EditorProjection.Build(Current, new(false)).Settings.First(v => v.Id == view.Id);
            errors.Text = string.Join("\n", projection.ValidationErrors.Select(e => e.Message));
            if (settingInputs.TryGetValue(view.Id, out var field)) field.BorderBrush = errors.Text.Length == 0 ? B("Line") : errors.Foreground;
            var outputs = projection.Outputs;
            if (outputs.IsEmpty) output.Text = "Result: —";
            else if (view.Input is ToggleInput) output.Text = Current.Draft.Values[view.Id].Boolean == true ? "Passive unless provoked" : "Original animal behavior";
            else if (outputs[0].Target.OutputKind == OutputKind.LootTable) output.Text = "Quantity: ×" + Current.Draft.Values[view.Id].Format() + " · Original item selection preserved; stack limits apply";
            else if (outputs.Length > 4) output.Text = $"Result: {outputs.Min(o => o.Value.Number):0.##}–{outputs.Max(o => o.Value.Number):0.##} {outputs[0].Unit}" + (outputs.Any(o => o.Saturated) ? " · Some items have reached the 1 s minimum" : "");
            else if (view.Id is "MassPowerConsumption" or "RefineryPower") output.Text = "Consumes " + string.Join(" / ", outputs.Select(o=>Math.Abs(o.Value.Number!.Value).ToString("0.######",CultureInfo.InvariantCulture))) + (view.Id == "RefineryPower" ? " power/s per laser" : " power/s (normal / Overdrive)");
            else output.Text = "Result: " + string.Join("  /  ", outputs.Select(o => o.FormattedValue)) + " " + outputs[0].Unit + (outputs.Any(o => o.Saturated) ? " · Minimum reached; effective speed is capped" : "");
        };
        refreshSettings[view.Id]();
    }
    private void Profiles()
    {
        PageTitle("Profiles", "Rename a custom profile without changing its file or hotkey.");
        foreach (var (id, s) in sessions)
        {
            var c=Card(); var row=new DockPanel(); var choose=ActionButton(id==ProfileId.Vanilla ? "View" : id==selected ? "Editing" : "Edit", () => { selected=id; Render(); }); DockPanel.SetDock(choose,Dock.Right);row.Children.Add(choose);
            var name=new TextBox { Text=s.Draft.DisplayName, IsReadOnly=s.Draft.IsReadOnly, MaxLength=32, Width=260, HorizontalAlignment=HorizontalAlignment.Left, FontSize=17 };
            name.TextChanged+=(_,_)=>{if(!s.Draft.IsReadOnly){s.Rename(name.Text);Header();Footer();}};row.Children.Add(name);c.Children.Add(row);
            var detail=Text(Slot(id)+" · "+s.Draft.FileName+" · "+HotkeyState(id),12,"Muted");detail.Margin=new(0,10,0,0);c.Children.Add(detail);
        }
    }
    private void ReadIni()
    {
        inputIni=null;
        try { var p=GamePaths.FromBinaries(binaries); inputIni=new(File.ReadAllBytes(p.InputIni));iniError=""; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or FormatException) {iniError=e.Message;}
    }
    private void Hotkeys()
    {
        PageTitle("Hotkeys", "Hotkeys can be saved while playing. Existing game bindings stay protected.");
        var c=Card();
        foreach(var assignment in hotkeys.DraftAssignments)
        {
            c.Children.Add(Text(sessions[assignment.ProfileId].Draft.DisplayName+" · "+Slot(assignment.ProfileId),15));
            c.Children.Add(Text("Saved hotkey: "+Key(assignment.ProfileId)+" · "+HotkeyState(assignment.ProfileId),12,"Muted"));
            var picker=new ComboBox {Margin=new(0,0,0,17),MaxDropDownHeight=300};
            var unassigned=new ComboBoxItem { Content="Not assigned — choose a key",Tag="" };picker.Items.Add(unassigned);if(string.IsNullOrEmpty(assignment.KeyId))picker.SelectedItem=unassigned;
            foreach(var choice in IniHotkeys.Choices(assignment.ProfileId))
            {
                var conflicts=inputIni?.Conflicts(choice)??[];
                var reason=conflicts.IsEmpty ? "" : " — Unavailable: "+string.Join("; ",conflicts.Select(b=>b.FriendlyCommand).Distinct());
                var item=new ComboBoxItem {Content=choice.KeyId+reason,Tag=choice.KeyId,IsEnabled=conflicts.IsEmpty,ToolTip=reason};picker.Items.Add(item);if(choice.KeyId==assignment.KeyId)picker.SelectedItem=item;
            }
            picker.SelectionChanged+=(_,_)=>{if(picker.SelectedItem is ComboBoxItem item){hotkeys.SetKey(assignment.ProfileId,(string)item.Tag);Header();Footer();}};c.Children.Add(picker);
        }
        c.Children.Add(Text(inputIni is null?"Choose keys now; verify the game paths to check availability before installing.":"Unavailable keys belong to existing game bindings and cannot be overwritten. You can clear any saved assignment and choose another key.",12,"Muted"));
        c.Children.Add(Text("External overlays may also use these keys. Their shortcuts cannot be detected from the game's configuration.",12,"Muted"));
        var actions=new WrapPanel();actions.Children.Add(ActionButton("Reload availability",()=>{ReadIni();Render();}));
        actions.Children.Add(ActionButton("Clear saved assignments",()=>{hotkeys.ClearSavedAssignments();var saved=Persist();Render();Notice(saved?"Saved assignments cleared. Installed game bindings were not changed.":"Assignments cleared in memory; retry workspace save.",!saved);}));
        actions.Children.Add(ActionButton("Remove SCS hotkeys",RemoveHotkeys));c.Children.Add(actions);
        c=Card();c.Children.Add(Text("Protected installation",17));c.Children.Add(Text("The original input configuration is backed up once. Each update also keeps a recovery backup. Only bindings for the five SCS profiles are changed.",13,"Muted"));
    }
    private void Installation()
    {
        PageTitle("Subsistence installation", "One installation, two locations: profile files and the game's input configuration.");
        var c=Card();var row=new WrapPanel();row.Children.Add(ActionButton("Automatically detect",Detect));row.Children.Add(ActionButton("Browse…",Browse));c.Children.Add(row);
        c.Children.Add(Text("Binaries folder",12,"Muted"));var path=new TextBox {Text=binaries,Margin=new(0,8,0,14)};
        path.TextChanged+=(_,_)=>{binaries=path.Text.Trim();verification=new(null,false,false,false,false,"Verify the new installation.");inputIni=null;Header();Footer();};c.Children.Add(path);
        if(binaries.Length>0){try{c.Children.Add(Text(GamePaths.FromBinaries(binaries).InputIni,12,"Muted"));}catch(ArgumentException){}}
        foreach(var (ok,label) in new[]{(verification.BinariesFound,"Binaries found"),(verification.IniFound,"UDKInput.ini found"),(verification.BinariesWritable,"Binaries writable"),(verification.IniWritable,"UDKInput.ini writable")})c.Children.Add(Text((ok?"✓ ":"○ ")+label,13,ok?"Accent":"Muted"));
        c.Children.Add(Text(verification.ProfilesReady?"✓ Ready for profile installation":"Paths not ready",13));
        c.Children.Add(Text(SetupReady?"✓ SCS ready to use":"SCS setup incomplete",15));
        c=Card();c.Children.Add(Text("Profile files",17));foreach(var s in sessions.Values)c.Children.Add(Text((File.Exists(Path.Combine(binaries,s.Saved.FileName))?"✓ ":"○ ")+s.Saved.FileName,12,"Muted"));
        c.Children.Add(Text("Custom files are preserved. Vanilla is checked against this catalogue and safely regenerated when outdated, keeping a backup.",12,"Muted"));
        c.Children.Add(Text("Hotkeys: "+(inputIni is not null && hotkeys.Installed(inputIni)?"installed in INI; pending in-game use":"not installed for the saved assignments"),13));
        c.Children.Add(ActionButton("Manage hotkeys",()=>Navigate("Hotkeys")));
        c=Card();c.Children.Add(Text("Remove profile files",17));c.Children.Add(Text("Remove SCS hotkeys first. Only selected, unchanged files recorded in the manifest can be removed. Each is backed up.",12,"Muted"));
        var selections=new List<CheckBox>();foreach(var name in ManagedProfiles.Names){var box=new CheckBox{Content=name};selections.Add(box);c.Children.Add(box);}
        c.Children.Add(ActionButton("Remove selected profile files",()=>{var names=selections.Where(b=>b.IsChecked==true).Select(b=>(string)b.Content).ToArray();if(names.Length==0){Notice("Select profile files first.");return;}if(MessageBox.Show(this,"Back up and remove the selected profile files?","Remove profile files",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;var result=new ManagedProfiles().Remove(binaries,names);Render();Notice(result.Message,!result.Success);}));
    }
    private void Browse(){var dialog=new OpenFolderDialog{Title="Select Subsistence or its Binaries folder"};if(dialog.ShowDialog(this)==true)ChooseFolder(dialog.FolderName);}
    private void ChooseFolder(string path){binaries=Path.GetFileName(Path.TrimEndingDirectorySeparator(path)).Equals("Binaries",StringComparison.OrdinalIgnoreCase)?path:Path.Combine(path,"Binaries");verification=new(null,false,false,false,false,"Verify the selected installation.");ReadIni();Render();Verify();}
    private void Detect()
    {
        var found=InstallationDetector.Detect();if(found.Length==0){Notice("No installation found. Use Browse.");return;}if(found.Length==1){ChooseFolder(found[0].BinariesPath);return;}
        var menu=new ContextMenu();foreach(var p in found){var item=new MenuItem{Header=p.InstallationRoot};item.Click+=(_,_)=>ChooseFolder(p.BinariesPath);menu.Items.Add(item);}menu.IsOpen=true;
    }
    private void Verify()
    {
        verification=GameInstallation.Verify(binaries);
        if(verification.ProfilesReady && File.Exists(Path.Combine(binaries,"SCS_Vanilla.txt")))new ManagedProfiles().MaintainVanilla(catalog,binaries);
        IniUpdateResult? migration=null;
        if(verification.Ready) migration=new IniHotkeyService().MigrateLegacy(verification.Paths!.InputIni);
        ReadIni();var stored=!verification.ProfilesReady||Persist();Render();
        Notice(verification.Message+(migration is { Changed:true }?" Hotkeys migrated to the correct game section.":migration is { Success:false }?" "+migration.Message:"")+(stored?"":" Workspace metadata not saved."),!verification.Ready||!stored||migration is { Success:false });
    }
    private bool Persist(bool includeDraftHotkeys = false)
    {
        try{store.Save(false,binaries,selected,persisted.Values,includeDraftHotkeys?hotkeys.DraftAssignments:hotkeys.SavedAssignments,hotkeys.GeneratedBinariesPath);if(includeDraftHotkeys)hotkeys.MarkPreferencesSaved();savedBinaries=binaries;metadataPending=false;return true;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException){metadataPending=true;Notice(e.Message,true);return false;}
    }
    private bool SaveAll()
    {
        var pending=sessions.Where(p=>!p.Value.Draft.IsReadOnly && (p.Value.IsDirty || migrated.Contains(p.Key))).ToArray();
        var errors=pending.SelectMany(p=>p.Value.ValidationErrors.Select(e=>p.Value.Draft.DisplayName+": "+e.Message))
            .Concat(IniHotkeys.Validate(hotkeys.DraftAssignments,allowUnassigned:true).Select(e=>e.Message)).ToArray();
        if(errors.Length>0){Footer();Notice(string.Join(" · ",errors),true);return false;}
        try
        {
            if(pending.Length>0)
            {
                var check=GameInstallation.Verify(binaries);
                if(!check.ProfilesReady){Footer();Notice(check.Message,true);return false;}
            }
            foreach(var (id,session) in pending)
            {
                var result=new ManagedProfiles().Save(session,binaries);
                if(!result.Success)
                {
                    Persist();Render();Notice("Save all incomplete: "+session.Draft.DisplayName+" · "+string.Join(" · ",result.Issues.Select(i=>i.Message).Append(result.File?.Failure?.Message).Where(t=>t is not null)),true);return false;
                }
                persisted[id]=session.Saved;migrated.Remove(id);metadataPending=true;
                if(result.Warning is not null){Persist();Render();Notice(result.Warning,true);return false;}
            }
            if(!Persist(includeDraftHotkeys:true)){Render();Notice("Save all incomplete: workspace could not be saved. Retry Save all.",true);return false;}
            ReadIni();Render();
            Notice(inputIni is not null && hotkeys.Installed(inputIni)?"All changes saved.":"All changes saved. Hotkey installation may still be pending.");
            return !HasUnsavedChanges;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException or InvalidOperationException or JsonException)
        { Footer();Notice("Save all incomplete: "+ex.Message,true);return false; }
    }
    private void SaveProfile()
    {
        var verify=GameInstallation.Verify(binaries);if(verify.ProfilesReady)new ManagedProfiles().MaintainVanilla(catalog,binaries);
        var result=new ManagedProfiles().Save(Current,binaries);
        if(!result.Success){Footer();Notice(string.Join("\n",result.Issues.Select(i=>i.Message).Append(result.File?.Failure?.Message).Where(s=>s is not null)),true);return;}
        persisted[selected]=Current.Saved;migrated.Remove(selected);var stored=Persist();Render();
        if(result.Warning is not null){Notice(result.Warning,true);return;}
        ReadIni();Header();
        Notice(stored?"✓ Profile saved · "+(InstalledKey(selected) is string installed?"Press "+installed+" in-game to apply":"Hotkey not installed"):"Profile saved; workspace metadata could not be saved.",!stored);
    }
    private void InstallProfiles()
    {
        var result=new ManagedProfiles().InstallProfiles(catalog,sessions.Values.Select(s=>s.Saved),binaries);
        ReadIni();Render();Notice(result.Message,!result.Success);
    }
    private void Install()
    {
        var result=new ManagedProfiles().Install(catalog,sessions.Values.Select(s=>s.Saved),binaries,hotkeys.DraftAssignments);
        if(result.Success){hotkeys.MarkInstalled(binaries);Persist();}ReadIni();Render();Notice(result.Message,!result.Success||metadataPending);
    }
    private void InstallHotkeys()
    {
        verification=GameInstallation.Verify(binaries);if(!verification.Ready){Notice(verification.Message,true);return;}
        if(ManagedProfiles.Names.Any(n=>!File.Exists(Path.Combine(binaries,n)))){Notice("Install missing profiles in Installation first.",true);return;}
        new ManagedProfiles().MaintainVanilla(catalog,binaries);
        var p=verification.Paths!;var result=new IniHotkeyService().Install(p.InputIni,hotkeys.DraftAssignments);
        if(result.Success){hotkeys.MarkInstalled(binaries);Persist();}ReadIni();Render();Notice(result.Message,!result.Success||metadataPending);
    }
    private void RemoveHotkeys()
    {
        if(MessageBox.Show(this,"Remove only SCS profile hotkeys? All other bindings and backups will be preserved.","Remove SCS hotkeys",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var p=GamePaths.FromBinaries(binaries);var result=new IniHotkeyService().Remove(p.InputIni);if(result.Success){hotkeys.ClearSavedAssignments();Persist();}ReadIni();Render();Notice(result.Message,!result.Success);
    }
    private void AppSettings()
    {
        PageTitle("App settings","Your saved workspace is restored when you reopen the app.");var c=Card();c.Children.Add(Text("Local workspace",18));
        c.Children.Add(Text("Saved profiles, friendly names, installation and hotkeys are kept locally. Profile drafts remain in memory until saved. Closing with pending changes asks before discarding them.",13,"Muted"));
        c.Children.Add(Text(store.Folder,12,"Muted"));if(metadataPending)c.Children.Add(ActionButton("Retry workspace save",()=>{var saved=Persist();Render();Notice(saved?"Workspace saved":"Workspace save failed.",!saved);}));
        c=Card();c.Children.Add(Text("About this version",18));c.Children.Add(Text("App version 2.0.6 · Catalogue 2.0.1 · 25 September 2026\nSimple hotkeys · Original settings protected\nThe app does not track which profile is active in-game.",13,"Muted"));
    }
    internal async Task SmokeTest(string folder)
    {
        smokeTesting=true;Directory.CreateDirectory(folder);void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        void Click(string label){var b=Descendants(this).OfType<Button>().First(b=>Equals(b.Content,label));Check(b.IsEnabled,"Disabled: "+label);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        async Task Capture(string name){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();var e=(FrameworkElement)Content;var bmp=new RenderTargetBitmap((int)e.ActualWidth,(int)e.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(e);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(Path.Combine(folder,name+".png"));png.Save(stream);}
        Check(Icon is not null,"Window icon missing");Check(hotkeys.DraftAssignments.All(a=>a.KeyId==""),"Initial slots occupied");
        Navigate("Hotkeys");var firstPicker=Descendants(PageContent).OfType<ComboBox>().First();Check(firstPicker.Items.Cast<ComboBoxItem>().Any(i=>(string)i.Tag=="F10"&&i.IsEnabled),"Unverified picker locked");
        firstPicker.SelectedItem=firstPicker.Items.Cast<ComboBoxItem>().Single(i=>(string)i.Tag=="F10");Check(hotkeys.DraftAssignments[0].KeyId=="F10","Picker selection failed");Check(!FooterActions.Children.OfType<Button>().Single().IsEnabled,"Unverified install enabled");Click("Clear saved assignments");await Capture("00-unassigned-hotkeys");
        Navigate("Overview");await Capture("01-overview");Navigate("Gathering");settingInputs["WoodYield"].Text="10";Check(Current.IsDirty,"Wood edit failed");await Capture("02-gathering");
        Navigate("Farming");settingInputs["CropHarvestYield"].Text="100";await Capture("03-farming");Navigate("Animals");settingInputs["ButcherYield"].Text="100";Check(!FooterActions.Children.OfType<Button>().Last().IsEnabled,"Loot overflow enabled");settingInputs["ButcherYield"].Text="5";await Capture("04-animals");
        Navigate("Crafting");settingInputs["CraftSpeed"].Text="100";await Capture("05-crafting");Navigate("Traps");settingInputs["TrapAttemptSpeed"].Text="10";await Capture("06-traps");
        foreach(var page in new[]{"Weapons Bench","Campfire","BCU","Power Generator","Mass Fabricator","Refinery"}){Navigate(page);if(page=="Refinery"){settingInputs["RefiningSpeed"].Text="51";Check(!Current.ValidationErrors.IsEmpty,"Refinery accepts >50");settingInputs["RefiningSpeed"].Text="50";}
        if(page is "Refinery" or "Mass Fabricator")Check(Descendants(PageContent).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Consumes ")&&!t.Text.Contains("-")),"Consumption sign exposed");await Capture(page.Replace(" ","-"));if(page is "Refinery" or "Mass Fabricator"){PageScroll.ScrollToBottom();await Capture(page.Replace(" ","-")+"-consumption");}}
        binaries=Path.Combine(folder,"Game","Binaries");Directory.CreateDirectory(Path.Combine(binaries,"Win64"));File.WriteAllBytes(Path.Combine(binaries,"Win64","Subsistence.exe"),[77,90]);
        var p=GamePaths.FromBinaries(binaries);Directory.CreateDirectory(Path.GetDirectoryName(p.InputIni)!);
        File.WriteAllText(p.InputIni,"[Engine.PlayerInput]\r\nBindings=(Name=\"F8\",Command=\"GBA_QuickSave\")\r\n[ColdGame.ColdPlayerInput]\r\nBindings=(Name=\"F6\",Command=\"exec SCS_Test.txt\")\r\n");
        hotkeys=IniHotkeyEditSession.Restore(HotkeyCompiler.Defaults,null);
        Navigate("Installation");Check(!FooterActions.Children.OfType<Button>().Last().IsEnabled,"Unverified installation enabled");Click("Verify");
        Navigate("Hotkeys");var legacyPicker=Descendants(PageContent).OfType<ComboBox>().First();Check(!((ComboBoxItem)legacyPicker.SelectedItem).IsEnabled,"Legacy conflict not marked");
        legacyPicker.SelectedItem=legacyPicker.Items.Cast<ComboBoxItem>().Single(i=>(string)i.Tag=="");Check(hotkeys.DraftAssignments[0].KeyId=="","Legacy key could not be cleared");await Capture("00-legacy-recovery");
        Click("Clear saved assignments");Check(hotkeys.SavedAssignments.All(a=>a.KeyId==""),"Clear saved assignments failed");Navigate("Installation");
        hotkeys.SetKey(ProfileId.Profile1,"F8");Render();Check(IdentityLabel.Text.Contains("Hotkey not installed"),"Header claims uninstalled key");
        Click("Install missing profiles only");Check(!new InputIniDocument(File.ReadAllBytes(p.InputIni)).Bindings.Any(b=>b.IsScs),"Profile-only install wrote INI");
        foreach(var a in IniHotkeys.Defaults)hotkeys.SetKey(a.ProfileId,a.KeyId);Render();Click("Install missing profiles and hotkeys");Check(inputIni is not null && hotkeys.Installed(inputIni),"Hotkey installation failed: "+StatusText.Text);Check(inputIni!.HasCompleteLayout,"Wrong target section");
        var migratedFixture=inputIni.Text;var ownLines=string.Join("\r\n",inputIni.Bindings.Where(b=>b.IsScs).Select(b=>inputIni.Text.Substring(b.Start,b.Length).TrimEnd('\r','\n')));
        File.WriteAllBytes(p.InputIni,inputIni.RemoveScs());var cleanFixture=File.ReadAllText(p.InputIni);File.WriteAllText(p.InputIni,cleanFixture.Replace("[Engine.PlayerInput]","[Engine.PlayerInput]\r\n"+ownLines));
        ReadIni();Render();Check(InstalledKey(ProfileId.Profile1) is null,"Legacy header claims installed");Click("Verify");Check(inputIni!.HasCompleteLayout,"Verify migration failed: "+StatusText.Text);await Capture("07-installation");
        Navigate("Gathering");Click("Save profile");Check(!Current.IsDirty,"Save failed: "+StatusText.Text);
        Check(File.ReadAllText(Path.Combine(binaries,"SCS_Profile1.txt")).Contains("set ColdInventoryItem_Log Count 10"),"Wood command missing");
        Navigate("Hotkeys");Check(!Descendants(PageContent).OfType<TextBlock>().Any(t=>t.Text.Contains("exec SCS_Test")),"Internal command visible");
        Check(Descendants(PageContent).OfType<ComboBox>().SelectMany(c=>c.Items.Cast<ComboBoxItem>()).All(i=>!((string)i.Content).Contains("exec ")),"Selector command leak");await Capture("08-hotkeys");Check(Descendants(PageContent).OfType<ComboBox>().First().Items.Cast<ComboBoxItem>().Any(i=>(string)i.Tag=="F8"&&!i.IsEnabled),"Occupied key enabled");
        hotkeys.SetKey(ProfileId.Profile1,"F11");Render();Check(!FooterActions.Children.OfType<Button>().Single().IsEnabled,"Duplicate hotkeys enabled");hotkeys.SetKey(ProfileId.Profile1,"F10");Render();
        selected=ProfileId.Vanilla;Navigate("Gathering");Check(settingInputs.Values.All(t=>t.IsReadOnly)&&FooterActions.Children.Count==0,"Vanilla editable");await Capture("09-vanilla");
        selected=ProfileId.Profile1;Navigate("App settings");await Capture("10-app-settings");var cleanStart="[Engine.PlayerInput]\r\nBindings=(Name=\"W\",Command=\"GBA_MoveForward\")\r\n[ColdGame.ColdPlayerInput]\r\nbIsSprintToggle=TRUE\r\nbIsCrouchToggle=TRUE\r\nbIsAimDownSightsToggle=FALSE\r\n";
        File.WriteAllText(p.InputIni,cleanStart+InputIniDocument.Begin+"\r\n"+string.Join("\r\n",hotkeys.SavedAssignments.Select(IniHotkeys.BindingLine))+"\r\n"+InputIniDocument.End+"\r\n");
        var restored=new MainWindow(store);Check(restored.inputIni is {NeedsRepair:false,HasCompleteLayout:true},"Startup failed to repair shadowing child");Check(restored.Current.Saved.Values["WoodYield"].Number==10,"Restart lost values");restored.smokeTesting=true;restored.Close();
        ReadIni();var iniBeforeSaveAll=File.ReadAllBytes(p.InputIni);
        sessions[ProfileId.Profile1].SetRawInput("WoodYield","11");sessions[ProfileId.Profile2].SetRawInput("WoodYield","bad");
        hotkeys.SetKey(ProfileId.Profile1,"Insert");Render();Check(SaveAllButton.IsEnabled,"Save all disabled with changes");
        var oldProfile1=File.ReadAllBytes(Path.Combine(binaries,"SCS_Profile1.txt"));
        Check(!SaveAll()&&File.ReadAllBytes(Path.Combine(binaries,"SCS_Profile1.txt")).SequenceEqual(oldProfile1),"Invalid other profile allowed partial writes");
        sessions[ProfileId.Profile2].SetRawInput("WoodYield","20");SaveProfile();Check(!Current.IsDirty&&sessions[ProfileId.Profile2].IsDirty&&hotkeys.IsDirty,"Save profile changed another draft");
        migrated.Add(ProfileId.Profile3);Check(SaveAll(),"Save all failed: "+StatusText.Text);
        Check(!HasUnsavedChanges&&!SaveAllButton.IsEnabled&&migrated.Count==0,"Saved changes still dirty");
        Check(File.ReadAllBytes(p.InputIni).SequenceEqual(iniBeforeSaveAll),"Save all modified INI");
        Check(File.ReadAllText(Path.Combine(binaries,"SCS_Profile2.txt")).Contains("Count 20"),"Other profile not saved");
        Check(!hotkeys.Installed(new InputIniDocument(iniBeforeSaveAll)),"Local save installed hotkeys");
        var afterSave=new MainWindow(store);Check(!afterSave.HasUnsavedChanges&&afterSave.hotkeys.SavedAssignments[0].KeyId=="Insert","Restart falsely dirty or preferences lost");afterSave.smokeTesting=true;afterSave.Close();
        Navigate("Hotkeys");await Capture("11-save-all-clean");
        sessions[ProfileId.Profile1].SetRawInput("WoodYield","12");sessions[ProfileId.Profile2].SetRawInput("WoodYield","21");
        var profile2Path=Path.Combine(binaries,"SCS_Profile2.txt");File.SetAttributes(profile2Path,File.GetAttributes(profile2Path)|FileAttributes.ReadOnly);
        try{Check(!ResolveClose(CloseChoice.Save)&&HasUnsavedChanges,"Partial failure allowed close");Check(!sessions[ProfileId.Profile1].IsDirty&&sessions[ProfileId.Profile2].IsDirty,"Partial save lost correct dirty state");}
        finally{File.SetAttributes(profile2Path,FileAttributes.Normal);}
        Check(SaveAll(),"Retry failed");hotkeys.SetKey(ProfileId.Profile1,"Home");
        File.SetAttributes(store.FilePath,File.GetAttributes(store.FilePath)|FileAttributes.ReadOnly);
        try{Check(!ResolveClose(CloseChoice.Save)&&hotkeys.IsDirty&&metadataPending,"Failed metadata save cleared pending state");}
        finally{File.SetAttributes(store.FilePath,FileAttributes.Normal);}
        Check(!ResolveClose(CloseChoice.Cancel)&&HasUnsavedChanges,"Cancel lost draft");
        Check(ResolveClose(CloseChoice.Discard)&&HasUnsavedChanges,"Discard attempted a save");
        foreach(var choice in new[]{CloseChoice.Cancel,CloseChoice.Discard,CloseChoice.Save})
        {
            var dialog=new CloseChangesDialog { Owner=this };
            dialog.Loaded+=(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{
                dialog.UpdateLayout();var panel=(FrameworkElement)dialog.Content;
                if(choice==CloseChoice.Cancel){var bmp=new RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(panel);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(Path.Combine(folder,"12-close-dialog.png"));png.Save(file);}
                var label=choice==CloseChoice.Save?"Save all and close":choice==CloseChoice.Discard?"Discard changes":"Cancel";
                Descendants(dialog).OfType<Button>().Single(b=>Equals(b.Content,label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            dialog.ShowDialog();Check(dialog.Choice==choice,"Close dialog returned wrong action");
        }
        Check(ResolveClose(CloseChoice.Save)&&!HasUnsavedChanges&&!SaveAllButton.IsEnabled,"Save all and close did not resolve pending state");
        Check(File.ReadAllBytes(p.InputIni).SequenceEqual(iniBeforeSaveAll),"Save/close touched INI");
        File.WriteAllText(Path.Combine(folder,"ui-test-results.json"),JsonSerializer.Serialize(new{passed=true,settings=catalog.Supported.Length,commands=new ProfileCompiler(catalog).Compile(Current.Saved).Outputs.Length,hotkeysInstalled=true,occupiedKeyDisabled=true,restartRestored=true,unassignedStart=true,legacyRecovery=true,unverifiedInstallBlocked=true,windowIcon=true,correctInputSection=true,dualSectionBindings=true,childAppendOperator=true,verifyMigratesLegacy=true,startupRepairsShadowing=true,saveAllReadback=true,saveAllDoesNotInstallHotkeys=true,cleanRestart=true,partialFailureKeepsOpen=true,metadataFailureKeepsOpen=true,closeDialogThreeChoices=true},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var item in Descendants(child))yield return item;}}
}
