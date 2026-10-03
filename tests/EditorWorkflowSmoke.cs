using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Outil_Azur_complet;
using Outil_Azur_complet.Editors;
using Outil_Azur_complet.editeur_perso;

internal static class EditorWorkflowSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            string name = new AssemblyName(args.Name).Name, path = Path.Combine(TestPaths.ApplicationBin, name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(TestPaths.ApplicationBin, name + ".exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Application.EnableVisualStyles(); Run();
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static object Get(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    private static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
    private static void Property(object target, string name, object value) { target.GetType().GetProperty(name).SetValue(target, value, null); }
    private static IEnumerable<Control> All(Control control) { yield return control; foreach (Control child in control.Controls) foreach (var nested in All(child)) yield return nested; }
    private static Control Find(Control form, string name) { return All(form).First(control => control.Name == name); }
    private static void Layout(Control control) { control.PerformLayout(); foreach (Control child in control.Controls) Layout(child); }
    private static void Render(Form form, string name)
    {
        var eventList = (System.ComponentModel.EventHandlerList)typeof(System.ComponentModel.Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form, null);
        object key = typeof(Form).GetField("EVENT_SHOWN", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Delegate handlers = eventList[key]; if (handlers != null) eventList.RemoveHandler(key, handlers);
        form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Layout(form);
        using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(TestPaths.Work, name + ".png")); }
        form.Hide();
    }
    private static ServerDataSnapshot Snapshot(ServerResourceKind kind, DataTable table, string[] keys = null)
    {
        if (keys == null) keys = new[] { "id" };
        var snapshot = new ServerDataSnapshot(); Property(snapshot, "Data", table); Property(snapshot, "Kind", kind);
        Set(snapshot, "Keys", keys); Set(snapshot, "Defaults", table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => c.AllowDBNull ? (object)DBNull.Value : c.DataType == typeof(string) ? "" : Activator.CreateInstance(c.DataType), StringComparer.OrdinalIgnoreCase));
        table.PrimaryKey = keys.Select(key => table.Columns[key]).ToArray(); table.AcceptChanges(); return snapshot;
    }
    private static void Bind(ServerDataForm form, ServerDataSnapshot snapshot)
    { Set(form, "snapshot", snapshot); Call(form, "RefreshList", new object[] { null }); Call(form, "SetBusy", false); typeof(AzurEditorWindow).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { "Choisissez une fiche à gauche. Les options sont regroupées à droite." }); }
    private static void Run()
    {
        const string level19 = "108;11;16;-1;0;0;1d6+10,108;16;16;-1;0;0;0d0+16,3,1,4,30,100,false,true,false,true,2,1,3,PaPa,-1,18;19,42,false";
        string level20 = level19.Replace(",3,PaPa", ",3,7,PaPa");
        Check(SpellLevelData.Parse(level19).Serialize() == level19, "19-field spell options were lost");
        Check(SpellLevelData.Parse(level20).Serialize() == level20, "20-field extra parameter was lost");
        var level = SpellLevelData.Parse(level20); level.Fields[2] = "5"; level.Fields[level.ZoneIndex + 3] = "100";
        Check(SpellLevelData.Parse(level.Serialize()).Fields[14] == "7", "Editing other options removed the server parameter");
        var table = new DataTable(); table.Columns.Add("id", typeof(int)); table.Columns.Add("nom", typeof(string)); table.Columns.Add("sprite", typeof(int)); table.Columns.Add("spriteInfos", typeof(string));
        for (int i = 1; i <= 6; i++) table.Columns.Add("lvl" + i, typeof(string));
        table.Columns.Add("effectTarget", typeof(string)); table.Columns.Add("type", typeof(int)); table.Columns.Add("custom_option", typeof(string));
        foreach (DataColumn column in table.Columns) column.AllowDBNull = false; table.Columns["custom_option"].AllowDBNull = true;
        table.Rows.Add(new object[] { 10, "Soin", 108, "0,0,0", level19, level20, "-1", "-1", "-1", "-1", "", 0, DBNull.Value });
        table.Rows.Add(new object[] { 11, "Autre sort", 101, "0,0,0", "-1", "-1", "-1", "-1", "-1", "-1", "", 0, "option" });
        var snapshot = Snapshot(ServerResourceKind.Spells, table);
        using (var form = new ServerDataForm(ServerResourceKind.Spells, "Éditeur de sorts"))
        {
            Bind(form, snapshot);
            Check(All(form).Any(control => control is iTalk.iTalk_ThemeContainer), "Azur theme is absent");
            Check(!All(form).Any(control => control is DataGridView), "Raw database grid is still present");
            Check(((IList)Get(form, "fields")).Count == table.Columns.Count, "Some options have no editable field");
            Check((bool)Call(form, "CommitCurrent") && table.GetChanges() == null, "Merely viewing a resource marks it modified");
            Render(form, "editeur-sorts");
            Find(form, "sprite_valeur").Text = "invalide";
            ((ListBox)Get(form, "list")).SelectedIndex = 1;
            Check(((ListBox)Get(form, "list")).SelectedIndex == 0 && Find(form, "sprite_valeur").Text == "invalide", "Invalid draft was discarded when selecting another row");
            Check(table.GetChanges() == null, "Invalid draft partially changed the snapshot");
            Find(form, "sprite_valeur").Text = "200"; Find(form, "nom_valeur").Text = "Soin amélioré";
            Check((bool)Call(form, "CommitCurrent") && (int)table.Rows[0]["sprite"] == 200 && (string)table.Rows[0]["nom"] == "Soin amélioré", "Typed form does not apply edits");
            var tabs = (TabControl)Get(form, "tabs");
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(page => page.Text == "Niveaux"); Render(form, "editeur-niveaux-sorts");
            table.AcceptChanges();
        }
        Type spellFormType = typeof(ServerDataForm).Assembly.GetType("Outil_Azur_complet.Editors.SpellLevelForm");
        using (var form = (Form)Activator.CreateInstance(spellFormType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "Sort — niveau 2", level20 }, null))
        {
            Check((string)Call(form, "Read") == level20, "Spell form changes an untouched level");
            Find(form, "param2_valeur").Text = "6";
            Check(SpellLevelData.Parse((string)Call(form, "Read")).Fields[2] == "6", "Spell form ignores cost changes");
            Render(form, "options-niveau-sort");
            ((TabControl)Get(form, "tabs")).SelectedIndex = 1; Render(form, "options-effets-sort");
            ((TabControl)Get(form, "tabs")).SelectedIndex = 3;
            Check(((Control)Get(form, "raw")).Text.Contains(",6,1,4,"), "Visiting the advanced format discarded form changes");
            Find(form, "param2_valeur").Text = "7";
            ((Control)Get(form, "raw")).Text = level19;
            ((TabControl)Get(form, "tabs")).SelectedIndex = 0;
            bool rejected = false; try { Call(form, "Apply"); } catch (TargetInvocationException error) { rejected = error.InnerException is FormatException; }
            Check(rejected, "Conflicting raw and form edits were silently overwritten");
            // Reloading the advanced text must recreate all fields without disposing the text box.
            var parsed = SpellLevelData.Parse(level19); Call(form, "Build", parsed);
            Check(!((Control)Get(form, "raw")).IsDisposed && (string)Call(form, "Read") == level19, "Reloading the raw spell format broke the editor");
        }
        Check(Tools_protocol.Data.EffectsListing.SpellsEffectList["108"] == "Soin", "Built-in French effects require unrelated startup loading");
        Type specsType = typeof(ServerDataForm).Assembly.GetType("Outil_Azur_complet.Editors.FieldSpec");
        Type listFormType = typeof(ServerDataForm).Assembly.GetType("Outil_Azur_complet.Editors.DelimitedListForm");
        Array gradeSpecs = Array.CreateInstance(specsType, 5); string[] statNames = { "Force", "Sagesse", "Intelligence", "Chance", "Agilité" };
        for (int i = 0; i < statNames.Length; i++) gradeSpecs.SetValue(specsType.GetMethod("Number", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { statNames[i], "", false }), i);
        using (var form = (Form)Activator.CreateInstance(listFormType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "Caractéristiques par grade", "Une ligne par grade", "80,0,30,0,40,keep|90,1,31,2,41", '|', ',', gradeSpecs, new Func<string, string>(value => "Grade : " + value), 5 }, null))
        {
            Check((bool)Call(form, "Commit"), "Grade fields rejected their initial data");
            Find(form, "part0_valeur").Text = "invalide"; ((ListBox)Get(form, "list")).SelectedIndex = 1;
            Check(((ListBox)Get(form, "list")).SelectedIndex == 0 && Find(form, "part0_valeur").Text == "invalide", "Invalid grade was lost when navigating");
            Find(form, "part0_valeur").Text = "100"; Check((bool)Call(form, "Commit"), "Grade field cannot be modified");
            Check(((Label)Get(form, "Status")).ForeColor != Color.Firebrick, "Correcting the field keeps a stale validation error");
            Check(((List<string>)Get(form, "records"))[0] == "100,0,30,0,40,keep", "Editing characteristics lost additional grade parameters");
            Render(form, "editeur-caracteristiques-monstres");
        }
        var jobs = new DataTable(); jobs.Columns.Add("id", typeof(int)); jobs.Columns.Add("name", typeof(string)); jobs.Columns.Add("tools", typeof(string)); jobs.Columns.Add("crafts", typeof(string)); jobs.Columns.Add("skills", typeof(string)); jobs.Columns.Add("AP", typeof(string));
        foreach (DataColumn column in jobs.Columns) column.AllowDBNull = false; jobs.Columns["skills"].AllowDBNull = true;
        jobs.Rows.Add(2, "Bûcheron", "454,8539", "101;459,2539", "7003;101", "5,10,20,30");
        var jobSnapshot = Snapshot(ServerResourceKind.Jobs, jobs);
        using (var form = new ServerDataForm(ServerResourceKind.Jobs, "Éditeur de métiers"))
        {
            Bind(form, jobSnapshot); ((TabControl)Get(form, "tabs")).SelectedTab = ((TabControl)Get(form, "tabs")).TabPages.Cast<TabPage>().First(page => page.Text == "Activités");
            Check((bool)Call(form, "CommitCurrent") && jobs.GetChanges() == null, "Serialized job activities changed while browsing"); Render(form, "editeur-metiers");
            var first = ServerDataService.Add(jobSnapshot); var second = ServerDataService.Add(jobSnapshot);
            Check((int)first["id"] == 3 && (int)second["id"] == 4, "Adding multiple resources collides on default identifiers"); jobs.RejectChanges();
        }
        var monsters = new DataTable(); monsters.Columns.Add("id", typeof(int)); monsters.Columns.Add("name", typeof(string)); foreach (string column in new[] { "grades", "stats", "spells", "pdvs", "points", "inits", "exps" }) monsters.Columns.Add(column, typeof(string)); foreach (DataColumn column in monsters.Columns) column.AllowDBNull = false;
        monsters.Rows.Add(31, "Larve bleue", "2@1;5;5;-9;-9;5;3|3@2;6;6;-8;-8;6;4", "80,0,30,0,40|85,0,30,0,40", "212@1;213@1|212@2;213@2", "10|15", "4;2|4;2", "1|1", "150|160");
        using (var form = new ServerDataForm(ServerResourceKind.Monsters, "Éditeur de monstres"))
        {
            Bind(form, Snapshot(ServerResourceKind.Monsters, monsters)); Check((bool)Call(form, "CommitCurrent") && monsters.GetChanges() == null, "Browsing monster grades changes the data");
            var monsterFields = (IList)Get(form, "fields"); foreach (Control field in monsterFields) if (field.Name != "id" && field.Name != "name") Check(Get(Get(field, "spec"), "Edit") != null, "Monster grade option still requires a raw encoded string: " + field.Name);
        }
        var placement = new DataTable(); placement.Columns.Add("mapid", typeof(int)); placement.Columns.Add("npcid", typeof(int)); placement.Columns.Add("cellid", typeof(int)); placement.Columns.Add("orientation", typeof(int)); placement.Columns.Add("isMovable", typeof(byte)); foreach (DataColumn column in placement.Columns) column.AllowDBNull = false; placement.Rows.Add(100, 10, 8, 2, 0);
        var placementSnapshot = Snapshot(ServerResourceKind.Npcs, placement, new[] { "mapid", "npcid", "cellid" }); Property(placementSnapshot, "MapId", (int?)100); Property(placementSnapshot, "CellCount", (int?)17); Set(placementSnapshot, "MapColumn", "mapid"); Set(placementSnapshot, "ReferenceNames", new Dictionary<string, string> { { "10", "PNJ 10 · apparence 100" } });
        using (var form = new ServerDataForm(ServerResourceKind.Npcs, "PNJ de la carte", 100, 17, 8))
        {
            Bind(form, placementSnapshot); var orientation = (ComboBox)Find(form, "orientation_choix"); Check(orientation.Text == "Sud", "Directions are not translated"); orientation.SelectedIndex = 6;
            Check((bool)Call(form, "CommitCurrent") && (int)placement.Rows[0]["orientation"] == 6, "French direction choices do not apply");
            Render(form, "editeur-placement-pnj"); placement.AcceptChanges();
        }
        var interactives = new DataTable(); interactives.Columns.Add("id", typeof(int)); interactives.Columns.Add("walkable", typeof(bool)); interactives.Rows.Add(1, true);
        using (var form = new ServerDataForm(ServerResourceKind.Interactives, "Objets interactifs"))
        {
            Bind(form, Snapshot(ServerResourceKind.Interactives, interactives)); var option = (ComboBox)Find(form, "walkable_choix");
            Check(option.Text == "Oui", "Boolean SQL values are displayed in English"); option.SelectedIndex = 0;
            Check((bool)Call(form, "CommitCurrent") && !(bool)interactives.Rows[0]["walkable"], "Numeric Yes/No options cannot edit a boolean SQL field"); interactives.AcceptChanges();
        }
        var character = new CharacterSkillsSnapshot(); Property(character, "CharacterName", "Alice"); Property(character, "Kind", CharacterSkillKind.Spells);
        Property(character, "Choices", new List<CharacterSkillChoice> { new CharacterSkillChoice { Id = 10, Name = "Soin" }, new CharacterSkillChoice { Id = 11, Name = "Attaque" } });
        using (var form = new CharacterSkillsForm(1, CharacterSkillKind.Spells))
        {
            Set(form, "snapshot", character); var entries = (List<CharacterSkillEntry>)Get(form, "entries"); entries.Add(new CharacterSkillEntry { Id = 10, Value = 2, Tail = "8;server" }); entries.Add(new CharacterSkillEntry { Id = 11, Value = 3, Tail = "-1" });
            Call(form, "RefreshList", new object[] { null }); Call(form, "SetBusy", false);
            Check((bool)Call(form, "Commit") && !(bool)Get(form, "dirty"), "Viewing character options changes them");
            Find(form, "position_valeur").Text = "9"; Find(form, "suite_valeur").Text = "changed";
            Check((bool)Call(form, "Commit") && entries[0].Tail == "9;changed", "Character position and extra options are not editable");
            Render(form, "editeur-sorts-personnage"); Set(form, "dirty", false);
        }
        Console.WriteLine("OK: themed resource and character forms, all options, spell formats, invalid drafts and UI renders");
    }
}
