using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Tools_protocol.Json;
using Tools_protocol.Query;
using Tools_protocol.Kryone.Database;

internal static class InventoryIntegrationSmoke
{
    private static readonly Type Service = typeof(InitializeForm).Assembly.GetType(
        "Outil_Azur_complet.editeur_items.InventoryUpdateService");
    private static readonly Type ChangeType = typeof(InitializeForm).Assembly.GetType(
        "Outil_Azur_complet.editeur_items.InventoryChange");
    private static readonly Type KindType = typeof(InitializeForm).Assembly.GetType(
        "Outil_Azur_complet.editeur_items.InventoryChangeKind");

    private static void Main()
    {
        string app = TestPaths.ApplicationBin;
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            string name = new AssemblyName(args.Name).Name;
            string path = Path.Combine(app, name + ".dll");
            if (!File.Exists(path)) path = Path.Combine(app, name + ".exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static MySqlConnectionStringBuilder Connection(string database)
    {
        return new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1", Port = 43306, UserID = "root", Password = "",
            Database = database, ConnectionTimeout = 3
        };
    }

    private static void Sql(string connectionString, string commandText)
    {
        using (var connection = new MySqlConnection(connectionString))
        using (var command = new MySqlCommand(commandText, connection))
        {
            connection.Open();
            command.ExecuteNonQuery();
        }
    }

    private static object Scalar(string connectionString, string commandText)
    {
        using (var connection = new MySqlConnection(connectionString))
        using (var command = new MySqlCommand(commandText, connection))
        {
            connection.Open();
            return command.ExecuteScalar();
        }
    }

    private static object Change(int characterId, string name, string kind, int guid, int template, int quantity)
    {
        object change = Activator.CreateInstance(ChangeType, true);
        ChangeType.GetProperty("CharacterId").SetValue(change, characterId, null);
        ChangeType.GetProperty("CharacterName").SetValue(change, name, null);
        ChangeType.GetProperty("Kind").SetValue(change, Enum.Parse(KindType, kind), null);
        ChangeType.GetProperty("ItemGuid").SetValue(change, guid, null);
        ChangeType.GetProperty("TemplateId").SetValue(change, template, null);
        ChangeType.GetProperty("Quantity").SetValue(change, quantity, null);
        return change;
    }

    private static IDictionary Apply(params object[] changes)
    {
        Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(ChangeType);
        object list = Activator.CreateInstance(listType);
        var add = listType.GetMethod("Add");
        foreach (object change in changes) add.Invoke(list, new[] { change });
        try
        {
            return (IDictionary)Service.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { list });
        }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static string UnavailableReason()
    {
        return (string)Service.GetMethod("UnavailableReason", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
    }

    private static void Run()
    {
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string auth = "azur_auth_test_" + suffix;
        string world = "azur_world_test_" + suffix;
        string server = Connection("").ConnectionString;
        try
        {
            Sql(server, "CREATE DATABASE `" + auth + "`");
            Sql(server, "CREATE DATABASE `" + world + "`");
            string authConnection = Connection(auth).ConnectionString;
            string worldConnection = Connection(world).ConnectionString;
            Sql(authConnection,
                "CREATE TABLE players (id INT PRIMARY KEY, name VARCHAR(50) NOT NULL, objets TEXT NOT NULL, logged INT NOT NULL) ENGINE=InnoDB");
            Sql(worldConnection,
                "CREATE TABLE items (guid INT PRIMARY KEY, template INT NOT NULL, qua INT NOT NULL, pos INT NOT NULL, stats TEXT NOT NULL, puit INT NOT NULL) ENGINE=InnoDB");
            Sql(authConnection,
                "CREATE TABLE item_template (id INT PRIMARY KEY, name VARCHAR(50) NOT NULL, statstemplate TEXT NOT NULL) ENGINE=InnoDB");
            Sql(authConnection, "INSERT INTO players VALUES (1,'Alice','100|',0),(2,'Bob','',0)");
            Sql(worldConnection, "INSERT INTO items VALUES (100,7,5,-1,'x',0)");
            Sql(authConnection, "INSERT INTO item_template VALUES (7,'Potion','O''Brien')");

            InitializeForm.EMUSELECT = "Kryone";
            DatabaseManager.ConnectionString = authConnection;
            DatabaseManager2.ConnectionString = worldConnection;
            JsonManager.Auth_dico["perso"] = "players";
            JsonManager.Auth_dico["Template"] = "item_template";
            JsonManager.World_dico["items"] = "items";
            Check(UnavailableReason() == null, "InnoDB tables were rejected");

            var player = (CharacterList)FormatterServices.GetUninitializedObject(typeof(CharacterList));
            player.Id = 1; player.Name = "Alice"; player.Objets = "100|";
            CharacterList.PersoAll["Alice"] = player; ItemList.ItemsList.Clear();
            DatabaseManager2.ConnectionString = null;
            CharacterList.GetInventory("Alice");
            Check(CharacterList.InventoryLoadError != null && CharacterList.ItemsPerso[0].Contains("base world indisponible"), "A failed world read hides its cause");
            DatabaseManager2.ConnectionString = worldConnection;
            CharacterList.GetInventory("Alice");
            Check(CharacterList.InventoryLoadError == null && CharacterList.ItemsPerso[0] == "Potion (100) x5" && ItemList.ItemsList.ContainsKey(100), "Inventory did not reload after the world connection recovered");

            Apply(Change(1, "Alice", "Remove", 100, 0, 2));
            Check(Convert.ToInt32(Scalar(worldConnection, "SELECT qua FROM items WHERE guid=100")) == 3,
                "Partial removal failed");
            Apply(Change(1, "Alice", "Add", 0, 7, 4));
            Check(Convert.ToString(Scalar(authConnection, "SELECT objets FROM players WHERE id=1")) == "100|101|",
                "Added object missing from player inventory");
            Check(Convert.ToInt32(Scalar(worldConnection, "SELECT qua FROM items WHERE guid=101")) == 4,
                "Added object quantity wrong");
            Check(Convert.ToString(Scalar(worldConnection, "SELECT stats FROM items WHERE guid=101")) == "O'Brien",
                "Item stats were not parameterized");

            bool rolledBack = false;
            try
            {
                Apply(Change(1, "Alice", "Remove", 100, 0, 1),
                    Change(1, "Alice", "Add", 0, 999, 1));
            }
            catch (InvalidOperationException) { rolledBack = true; }
            Check(rolledBack, "Invalid template was accepted");
            Check(Convert.ToInt32(Scalar(worldConnection, "SELECT qua FROM items WHERE guid=100")) == 3,
                "Removal was not rolled back");
            Check(Convert.ToString(Scalar(authConnection, "SELECT objets FROM players WHERE id=1")) == "100|101|",
                "Player inventory changed after rollback");

            Sql(authConnection, "UPDATE players SET logged=1 WHERE id=1");
            bool onlineBlocked = false;
            try { Apply(Change(1, "Alice", "Remove", 100, 0, 1)); }
            catch (InvalidOperationException) { onlineBlocked = true; }
            Check(onlineBlocked, "Online character was modified");

            Sql(worldConnection, "ALTER TABLE items ENGINE=MyISAM");
            Check(UnavailableReason().Contains("InnoDB"), "MyISAM table was accepted");
            Console.WriteLine("OK: isolated MySQL add/remove, rollback, online guard and MyISAM guard");
        }
        finally
        {
            Sql(server, "DROP DATABASE IF EXISTS `" + auth + "`");
            Sql(server, "DROP DATABASE IF EXISTS `" + world + "`");
        }
    }
}
