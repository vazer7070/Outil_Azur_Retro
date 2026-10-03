using System;
using System.IO;
using System.Reflection;
using Tool_Editor.items;
using Tools_protocol.Query;

internal static class QueryBuilderSmoke
{
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

    private static void Run()
    {
        string insert = QueryBuilder.InsertIntoQuery("items", new[] { "name" }, new[] { "O'Brien\\test" }, "");
        Check(insert == "INSERT INTO `items`(`name`) VALUES ('O''Brien\\\\test')", "INSERT escaping failed: " + insert);
        string select = QueryBuilder.SelectFromQuery(new[] { "name" }, "items", "name", "O'Brien");
        Check(select == "SELECT `name` FROM `items` WHERE `name`='O''Brien'", "SELECT escaping failed: " + select);
        string update = QueryBuilder.UpdateFromQuery("items", "name", 1, "D'Azur", "id", "4");
        Check(update == "UPDATE `items` SET `name`='D''Azur' WHERE `id`='4'", "UPDATE escaping failed: " + update);
        string delete = QueryBuilder.DeleteFromQuery("items", "name", "D'Azur");
        Check(delete == "DELETE FROM `items` WHERE `name`='D''Azur'", "DELETE escaping failed: " + delete);
        bool rejected = false;
        try { QueryBuilder.SelectFromQuery(new[] { "*" }, "items;DROP", "", ""); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Unsafe identifier was accepted");

        EditorManager.TradStat.Clear();
        EditorManager.CreateStatItems("64%1|7");
        EditorManager.CreateStatItems("7d%11");
        Check(EditorManager.TradStat[0] == "64#1#7#0#1d7+0", "Range dice expression is wrong");
        Check(EditorManager.TradStat[1] == "7d#B#B#0#1d1+10", "Fixed stat expression is wrong: " + EditorManager.TradStat[1]);
        Console.WriteLine("OK: SQL identifier/value escaping and item stat dice generation");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}


