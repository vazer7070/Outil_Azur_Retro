using System;
using System.IO;
using System.Reflection;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Tools_protocol.Json;
using Tools_protocol.Query;

internal static class ModerationIntegrationSmoke
{
    private static readonly Type Service = typeof(InitializeForm).Assembly.GetType(
        "Outil_Azur_complet.KryoneModerationService");

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

    private static object Call(string name, params object[] arguments)
    {
        try
        {
            return Service.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, arguments);
        }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static bool Rejected(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException) { return true; }
        catch (FormatException) { return true; }
    }

    private static void Run()
    {
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        string auth = "azur_auth_mod_" + suffix;
        string world = "azur_world_mod_" + suffix;
        string server = Connection("").ConnectionString;
        try
        {
            Sql(server, "CREATE DATABASE `" + auth + "`");
            Sql(server, "CREATE DATABASE `" + world + "`");
            string authConnection = Connection(auth).ConnectionString;
            string worldConnection = Connection(world).ConnectionString;
            Sql(authConnection,
                "CREATE TABLE accounts (guid INT UNSIGNED PRIMARY KEY, account VARCHAR(50) NOT NULL, logged INT NOT NULL, banned TINYINT NOT NULL) ENGINE=InnoDB");
            Sql(authConnection,
                "CREATE TABLE players (id INT PRIMARY KEY, name VARCHAR(50) NOT NULL, account INT NOT NULL, logged INT NOT NULL, objets TEXT NOT NULL, storeObjets TEXT NOT NULL, INDEX(account)) ENGINE=InnoDB");
            Sql(worldConnection,
                "CREATE TABLE items (guid INT PRIMARY KEY, template INT NOT NULL, qua INT NOT NULL, pos INT NOT NULL, stats TEXT NOT NULL, puit INT NOT NULL) ENGINE=InnoDB");
            Sql(authConnection,
                "INSERT INTO accounts VALUES (10,'alpha',0,0),(20,'delete-me',0,0),(30,'busy',0,0),(40,'myisam-check',0,0)");
            Sql(authConnection,
                "INSERT INTO players VALUES " +
                "(1,'Alice',10,0,'100|',''),(2,'Online',10,1,'','')," +
                "(3,'DeleteOne',10,0,'300|','301|'),(4,'Broken',10,0,'bad|','400|')," +
                "(20,'First',20,0,'200|','201|'),(21,'Second',20,0,'202|','')," +
                "(30,'BusyChild',30,1,'500|',''),(40,'Engine',40,0,'','')");
            Sql(worldConnection,
                "INSERT INTO items VALUES (100,1,1,-1,'',0),(300,1,1,-1,'',0),(301,1,1,-1,'',0)," +
                "(400,1,1,-1,'',0),(200,1,1,-1,'',0),(201,1,1,-1,'',0),(202,1,1,-1,'',0)," +
                "(500,1,1,-1,'',0),(999,1,1,-1,'',0)");

            InitializeForm.EMUSELECT = "Kryone";
            DatabaseManager.ConnectionString = authConnection;
            DatabaseManager2.ConnectionString = worldConnection;
            JsonManager.Auth_dico["comptes"] = "accounts";
            JsonManager.Auth_dico["perso"] = "players";
            JsonManager.World_dico["items"] = "items";

            Call("SetAccountBannedForCharacter", 1, "Alice", true);
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT banned FROM accounts WHERE guid=10")) == 1,
                "Character account was not banned");
            Call("SetAccountBannedForCharacter", 1, "Alice", false);
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT banned FROM accounts WHERE guid=10")) == 0,
                "Character account was not unbanned");
            Check(Rejected(() => Call("SetAccountBannedForCharacter", 2, "Online", true)),
                "Online character could change account ban");

            Call("DeleteCharacter", 3, "DeleteOne");
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT COUNT(*) FROM players WHERE id=3")) == 0,
                "Character was not deleted");
            Check(Convert.ToInt32(Scalar(worldConnection, "SELECT COUNT(*) FROM items WHERE guid IN (300,301)")) == 0,
                "Character items were not deleted");
            Check(Convert.ToInt32(Scalar(worldConnection, "SELECT COUNT(*) FROM items WHERE guid=999")) == 1,
                "Unrelated item was deleted");

            Check(Rejected(() => Call("DeleteCharacter", 4, "Broken")),
                "Malformed inventory was accepted");
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT COUNT(*) FROM players WHERE id=4")) == 1 &&
                  Convert.ToInt32(Scalar(worldConnection, "SELECT COUNT(*) FROM items WHERE guid=400")) == 1,
                "Malformed inventory deletion did not roll back");

            Call("DeleteAccount", (uint)20, "delete-me");
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT COUNT(*) FROM accounts WHERE guid=20")) == 0,
                "Account was not deleted");
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT COUNT(*) FROM players WHERE account=20")) == 0,
                "Account characters were not deleted");
            Check(Convert.ToInt32(Scalar(worldConnection, "SELECT COUNT(*) FROM items WHERE guid IN (200,201,202)")) == 0,
                "Account character items were not deleted");

            Check(Rejected(() => Call("DeleteAccount", (uint)30, "busy")),
                "Account with online character was deleted");
            Check(Convert.ToInt32(Scalar(authConnection, "SELECT COUNT(*) FROM accounts WHERE guid=30")) == 1 &&
                  Convert.ToInt32(Scalar(authConnection, "SELECT COUNT(*) FROM players WHERE account=30")) == 1 &&
                  Convert.ToInt32(Scalar(worldConnection, "SELECT COUNT(*) FROM items WHERE guid=500")) == 1,
                "Online-account deletion was not atomic");

            Sql(authConnection, "ALTER TABLE accounts ENGINE=MyISAM");
            Check(Rejected(() => Call("SetAccountBannedForCharacter", 40, "Engine", true)),
                "MyISAM moderation table was accepted");
            Console.WriteLine("OK: isolated MySQL moderation, cascade cleanup, rollback, online and MyISAM guards");
        }
        finally
        {
            Sql(server, "DROP DATABASE IF EXISTS `" + auth + "`");
            Sql(server, "DROP DATABASE IF EXISTS `" + world + "`");
        }
    }
}
