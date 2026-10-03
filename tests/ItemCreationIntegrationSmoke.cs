using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MySql.Data.MySqlClient;
using Outil_Azur_complet;
using Tool_Editor.items;
using Tools_protocol.Json;
using Tools_protocol.Kryone.Database;
using Tools_protocol.Managers;
using Tools_protocol.Query;

internal static class ItemCreationIntegrationSmoke
{
    private static Type Service;
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
    private static object Call(string name, params object[] args)
    {
        try { return Service.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static string Connection(string db)
    {
        return new MySqlConnectionStringBuilder {
            Server = "127.0.0.1", Port = 43306, UserID = "root", Password = "",
            Database = db, ConnectionTimeout = 3
        }.ConnectionString;
    }
    private static void Sql(string cs, string sql)
    {
        using (var cn = new MySqlConnection(cs)) using (var cmd = new MySqlCommand(sql, cn))
        { cn.Open(); cmd.ExecuteNonQuery(); }
    }
    private static object Scalar(string cs, string sql)
    {
        using (var cn = new MySqlConnection(cs)) using (var cmd = new MySqlCommand(sql, cn))
        { cn.Open(); return cmd.ExecuteScalar(); }
    }
    private static string Template(int id, bool exchangeable, bool shop)
    {
        return (string)Call("BuildTemplateQuery", "templates", new[] {
            id.ToString(), "1", "L'Azur \\" + id, "20", "7d#1#3#0#1d3+0", "2", "10",
            "100", "CI>30", "0;0;0;0;0;0;0", "0", "0", "5"
        }, exchangeable, shop);
    }
    private static Dictionary<string,string> Queries(int id, int guid)
    {
        return new Dictionary<string,string> {
            { "template", Template(id, true, false) },
            { "item", QueryBuilder.InsertIntoQuery("items", new[] { "guid", "template", "qua", "pos", "stats", "puit" },
                new[] { guid.ToString(), id.ToString(), "0", "-1", "", "0" }, "") },
            { "craft", QueryBuilder.InsertIntoQuery("crafts", new[] { "id", "craft" }, new[] { id.ToString(), "1*2" }, "") },
            { "pano", (string)Call("BuildPanoplyQuery", "sets", "items", "name", "Azur", id) }
        };
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Run()
    {
        Service = typeof(InitializeForm).Assembly.GetType("Outil_Azur_complet.editeur_items.ItemCreationService", true);
        string suffix = Guid.NewGuid().ToString("N").Substring(0,8);
        string auth = "azur_create_auth_" + suffix, world = "azur_create_world_" + suffix;
        string server = Connection("");
        try
        {
            Sql(server, "CREATE DATABASE `" + auth + "`; CREATE DATABASE `" + world + "`");
            DatabaseManager.ConnectionString = Connection(auth);
            DatabaseManager2.ConnectionString = Connection(world);
            EmuManager.EMUSELECTED = "Kryone";
            JsonManager.Auth_dico["Template"] = "templates";
            JsonManager.Auth_dico["crafts"] = "crafts";
            JsonManager.Auth_dico["panoplies"] = "sets";
            JsonManager.World_dico["items"] = "items";
            Sql(DatabaseManager.ConnectionString,
                "CREATE TABLE templates (id INT PRIMARY KEY,type INT,name VARCHAR(50),level INT,statsTemplate VARCHAR(300),pod INT,panoplie INT,prix INT,conditions VARCHAR(100),armesInfos VARCHAR(100),sold INT,avgPrice INT,points INT,doplons INT,exchangeable INT,heroique INT) ENGINE=InnoDB;" +
                "CREATE TABLE crafts (id INT PRIMARY KEY,craft TEXT) ENGINE=InnoDB;" +
                "CREATE TABLE sets (id INT PRIMARY KEY,name VARCHAR(50),items TEXT) ENGINE=InnoDB; INSERT INTO sets VALUES(10,'Azur','1,2')");
            Sql(DatabaseManager2.ConnectionString,
                "CREATE TABLE items (guid INT PRIMARY KEY,template INT,qua INT,pos INT,stats TEXT,puit INT) ENGINE=InnoDB");
            Call("Inject", Queries(101,201),101,201);
            Check(Convert.ToInt32(Scalar(DatabaseManager.ConnectionString,"SELECT COUNT(*) FROM templates WHERE id=101"))==1,"template missing");
            Check(Convert.ToInt32(Scalar(DatabaseManager2.ConnectionString,"SELECT COUNT(*) FROM items WHERE guid=201"))==1,"item missing");
            Check(Convert.ToString(Scalar(DatabaseManager.ConnectionString,"SELECT items FROM sets WHERE id=10"))=="1,2,101","panoply not appended");
            using (var reader=DatabaseManager2.SelectQuery("SELECT guid FROM items WHERE guid=201"))
                Check(reader!=null && reader.Read() && reader.GetInt32(0)==201,"world reader was disposed early");

            Sql(DatabaseManager.ConnectionString,"INSERT INTO crafts VALUES(102,'occupied')");
            bool failed=false;
            try { Call("Inject",Queries(102,202),102,202); } catch (MySqlException) { failed=true; }
            Check(failed,"duplicate craft unexpectedly succeeded");
            Check(Convert.ToInt32(Scalar(DatabaseManager.ConnectionString,"SELECT COUNT(*) FROM templates WHERE id=102"))==0,"template not rolled back");
            Check(Convert.ToInt32(Scalar(DatabaseManager2.ConnectionString,"SELECT COUNT(*) FROM items WHERE guid=202"))==0,"world item not rolled back");
            Check(Convert.ToString(Scalar(DatabaseManager.ConnectionString,"SELECT items FROM sets WHERE id=10"))=="1,2,101","panoply changed after rollback");

            Sql(DatabaseManager2.ConnectionString,"ALTER TABLE items ENGINE=MyISAM");
            bool engineRejected=false;
            try { Call("Inject",Queries(103,203),103,203); } catch (InvalidOperationException) { engineRejected=true; }
            Check(engineRejected,"MyISAM accepted");
            Check(Convert.ToInt32(Scalar(DatabaseManager.ConnectionString,"SELECT COUNT(*) FROM templates WHERE id=103"))==0,"engine guard wrote partial template");

            Sql(DatabaseManager.ConnectionString,"DROP TABLE templates; CREATE TABLE templates (id INT PRIMARY KEY,type INT,name VARCHAR(50),level INT,statsTemplate VARCHAR(300),pod INT,panoplie INT,prix INT,conditions VARCHAR(100),armesInfos VARCHAR(100),sold INT,avgPrice INT,points INT,exchangesObject INT,boutique INT) ENGINE=InnoDB");
            Sql(DatabaseManager.ConnectionString,Template(104,false,true));
            Check(Convert.ToInt32(Scalar(DatabaseManager.ConnectionString,"SELECT boutique FROM templates WHERE id=104"))==1,"legacy boutique option lost");
            using (var cn=new MySqlConnection(DatabaseManager.ConnectionString)) using(var cmd=new MySqlCommand("SELECT * FROM templates WHERE id=104",cn))
            { cn.Open(); using(var reader=cmd.ExecuteReader()) { Check(reader.Read(),"legacy row missing"); var model=new ItemTemplateList(reader); Check(model.Id==104,"legacy template loader failed"); } }
            string line=EditorManager.CreateSwfLine(104,"L'Azur \"quoted\"","line\nnext","1","1","20",true,"2","1;2;3;4;5;6","CI>30","100",true,false);
            Check(line.Contains("fm:true") && line.Contains("n:\"L'Azur \\\"quoted\\\"\""),"SWF strings or booleans invalid");
            Check(!line.Contains("wd:,") && !line.Contains("an:,"),"invalid empty SWF fields");
            Console.WriteLine("OK: item creation across schemas, rollback, MyISAM guard, legacy schema, reader lifetime and SWF text");
        }
        finally
        {
            Sql(server,"DROP DATABASE IF EXISTS `"+auth+"`; DROP DATABASE IF EXISTS `"+world+"`");
        }
    }
}
