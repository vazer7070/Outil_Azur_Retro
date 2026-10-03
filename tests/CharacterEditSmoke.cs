using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using Outil_Azur_complet.editeur_perso;
using Outil_Azur_complet.editeur_items;
using Outil_Azur_complet.editeur_compte;
using Tools_protocol.Kryone.Database;

internal static class CharacterEditSmoke
{
    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static object Parse(string field, string value)
    {
        try
        {
            return typeof(editeur_perso).GetMethod("ParseFieldValue", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { field, value });
        }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    [STAThread]
    private static void Main()
    {
        string applicationDirectory = TestPaths.ApplicationBin;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(applicationDirectory, name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(applicationDirectory, name + ".exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    private static void Run()
    {
        CharacterList.PersoAll.Clear();
        foreach (string name in new[] { "Alice", "Bob" })
        {
            var character = (CharacterList)FormatterServices.GetUninitializedObject(typeof(CharacterList));
            character.Id = name == "Alice" ? 1 : 2;
            character.Name = name;
            character.Level = 10;
            CharacterList.PersoAll.Add(name, character);
        }
        using (var form = new editeur_perso())
        {
            form.GetType().GetMethod("editeur_perso_Load", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { form, EventArgs.Empty });
            var list = (ListBox)Field(form, "listBox1");
            list.SelectedItem = "Alice";
            var level = (Control)Field(form, "iTalk_TextBox_Small8");
            Check(level.Text == "10", "Initial level not loaded");
            Check(level.Enabled, "Level disabled");
            level.Text = "11";
            Check(((Control)Field(form, "iTalk_Button_31")).Enabled, "Save button not enabled");
            list.SelectedItem = "Bob";
            Check(level.Text == "10", "Second character not loaded");
            list.SelectedItem = "Alice";
            Check(level.Text == "11", "Pending change lost on selection switch");
            level.Text = "10";
            Check(!((Control)Field(form, "iTalk_Button_31")).Enabled, "Reverted change still pending");
            level.Text = "12";
            var search = (TextBox)Field(form, "textBox1");
            search.Text = "Bob";
            search.Text = "";
            list.SelectedItem = "Alice";
            Check(level.Text == "12", "Pending change lost on search");
            Check(((IDictionary)Field(form, "_pendingChanges")).Count == 1, "Wrong pending count");
        }
        Check((short)Parse("class", "1") == 1, "Class parsing failed");
        Check((sbyte)Parse("sexe", "Femelle") == 1, "Sex parsing failed");
        Check((int)Parse("groupe", "Administrateur") == 3, "Group parsing failed");
        bool invalidRejected = false;
        try { Parse("kamas", "-1"); }
        catch (FormatException) { invalidRejected = true; }
        Check(invalidRejected, "Negative kamas accepted");
        var player = CharacterList.PersoAll["Alice"];
        var updateMethod = typeof(editeur_perso).GetMethod("CreateCharacterUpdateCommand",
            BindingFlags.Static | BindingFlags.NonPublic);
        using (var command = (IDisposable)updateMethod.Invoke(null, new object[] {
            "players", player, new Dictionary<string, object> { { "name", "O'Brien" }, { "kamas", 100L } },
            null, null }))
        {
            string sql = (string)command.GetType().GetProperty("CommandText").GetValue(command, null);
            Check(sql.Contains("`name`=@value0") && sql.Contains("`kamas`=@value1"), "Fields not parameterized");
            Check(!sql.Contains("O'Brien"), "Character name leaked into SQL");
            Check(sql.Contains("`logged`=0"), "Online player protection missing");
        }
        CharacterList.PersoAll["Alice"].Objets = "123|";
        ItemList.ItemsList.Clear();
        var itemRecord = (ItemList)FormatterServices.GetUninitializedObject(typeof(ItemList));
        itemRecord.Guid = 123;
        itemRecord.Template = 99;
        itemRecord.Qua = 5;
        ItemList.ItemsList[123] = itemRecord;
        Check(JobsList.Name_Jobs("999") == "Métier #999", "Unknown job ID not shown");
        SpellsList.SpellsShow.Clear();
        SpellsList.AddSpellsToList("42;3");
        Check(SpellsList.SpellsShow.Count == 1 && SpellsList.SpellsShow[0].Contains("Sort #42"),
            "Spell ID lost when lookup unavailable");
        using (var inventory = new itemeditor())
        {
            inventory.LoadStaticInventory();
            var players = (ListBox)Field(inventory, "listBox4");
            players.SelectedItem = "Alice";
            Check(!((Control)Field(inventory, "iTalk_Button_23")).Enabled,
                "Unsafe inventory save is enabled");
            Check(inventory.Persoinventory["Alice"].Count == 1,
                "Inventory disappears after selection");
            Check(inventory.Persoinventory["Alice"][0].Contains("x5"),
                "Inventory quantity not shown");
            players.SelectedItem = "Bob";
            players.SelectedItem = "Alice";
            Check(inventory.Persoinventory["Alice"].Count == 1,
                "Cached inventory disappears after switching players");

            var availability = inventory.GetType().GetField("_inventoryAvailable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            availability.SetValue(inventory, true);
            var choiceType = typeof(itemeditor).Assembly.GetType(
                "Outil_Azur_complet.editeur_items.InventoryTemplateChoice");
            var choice = Activator.CreateInstance(choiceType, true);
            choiceType.GetProperty("Id").SetValue(choice, 99, null);
            choiceType.GetProperty("Name").SetValue(choice, "Potion", null);
            ((IDictionary)Field(inventory, "_inventoryTemplates")).Add("Potion (#99)", choice);
            var templates = (ListBox)Field(inventory, "listBox5");
            templates.Items.Add("Potion (#99)");
            templates.SelectedItem = "Potion (#99)";
            var quantity = Field(inventory, "iTalk_NumericUpDown6");
            quantity.GetType().GetProperty("Value").SetValue(quantity, 2L, null);
            inventory.GetType().GetMethod("StageAddItem", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(inventory, new object[] { inventory, EventArgs.Empty });
            var stagedRows = (ListView)Field(inventory, "listView1");
            Check(stagedRows.Items.Count == 1 && stagedRows.Items[0].SubItems[1].Text == "2",
                "Inventory addition was not staged");
            Check(((Control)Field(inventory, "iTalk_Button_23")).Enabled,
                "Apply button did not enable for staged inventory");
        }
        Outil_Azur_complet.InitializeForm.EMUSELECT = "Kryone";
        AccountList.AllAccount.Clear();
        AccountList.AllAccount["alpha"] = new AccountList { Account = "alpha", Guid = 1 };
        AccountList.AllAccount["beta"] = new AccountList { Account = "beta", Guid = 2 };
        using (var accounts = new editeurcompte())
        {
            var data = new List<Dictionary<string, object>> {
                new Dictionary<string, object> { { "Guid", 1u }, { "Account", "alpha" }, { "Pseudo", "A" } },
                new Dictionary<string, object> { { "Guid", 2u }, { "Account", "beta" }, { "Pseudo", "B" } }
            };
            accounts.GetType().GetField("allAccounts", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(accounts, data);
            var accountList = (ListBox)Field(accounts, "listBox1");
            accountList.Items.AddRange(new object[] { "alpha", "beta" });
            accountList.SelectedItem = "alpha";
            var pseudo = (Control)Field(accounts, "iTalk_TextBox_Small3");
            pseudo.Text = "Updated";
            accountList.SelectedItem = "beta";
            accountList.SelectedItem = "alpha";
            Check(pseudo.Text == "Updated", "Pending account edit lost on selection switch");
            pseudo.Text = "A";
            object pendingAccounts = Field(accounts, "_pendingChanges");
            Check((int)pendingAccounts.GetType().GetProperty("Count").GetValue(pendingAccounts, null) == 0,
                "Reverted account edit still pending");
        }
        Console.WriteLine("OK: character/account pending edits, inventory display and SQL parameters");
    }
}
