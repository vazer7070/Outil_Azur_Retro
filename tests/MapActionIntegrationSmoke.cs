using System;
using System.IO;
using System.Reflection;
using MySql.Data.MySqlClient;
using Outil_Azur_complet.maps;
using Tool_Editor.maps.data;

internal static class MapActionIntegrationSmoke
{
    private static Type Builder;
    [STAThread]
    private static void Main()
    {
        string bin=TestPaths.ApplicationBin;
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{
            string path=Path.Combine(bin,new AssemblyName(args.Name).Name+".dll");
            if(!File.Exists(path))path=Path.ChangeExtension(path,"exe");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        Run();
    }
    private static object Call(string method,params object[] args)
    {
        try { return Builder.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args); }
        catch(TargetInvocationException e) { throw e.InnerException; }
    }
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static void Run()
    {
        Builder=typeof(MainEditeur).Assembly.GetType("Outil_Azur_complet.maps.MapActionSqlBuilder");
        var connection=new MySqlConnectionStringBuilder { Server="127.0.0.1",Port=43306,UserID="root",ConnectionTimeout=3 };
        string schema="azur_map_actions_"+Guid.NewGuid().ToString("N").Substring(0,8);
        string scratch=Path.Combine(TestPaths.Work,"map-actions-"+Guid.NewGuid().ToString("N"));
        using(var db=new MySqlConnection(connection.ConnectionString))
        {
            db.Open();
            try
            {
                Execute(db,"CREATE DATABASE `"+schema+"`; USE `"+schema+"`; CREATE TABLE scripted_cells(MapID INT,CellID INT,ActionID INT,EventID INT,ActionsArgs TEXT,Conditions TEXT); CREATE TABLE endfight_action(map INT,args VARCHAR(30),fighttype INT,action INT,cond VARCHAR(50), PRIMARY KEY(map,args,fighttype)); INSERT INTO scripted_cells VALUES(1,0,0,1,'old','-1'),(1,0,42,1,'quest','-1'),(1,0,0,1,'conditional','PL>5'); INSERT INTO endfight_action VALUES(1,'old',4,0,''),(1,'quest',4,42,''),(1,'other',3,0,'')");
                string[] trigger=(string[])Call("BuildTrigger","scripted_cells",1,0,2,3);
                string[] end=(string[])Call("BuildEndFight","endfight_action",1,2,3);
                for(int pass=0;pass<2;pass++) { foreach(string query in trigger)Execute(db,query); foreach(string query in end)Execute(db,query); }
                Check(Scalar(db,"SELECT COUNT(*) FROM scripted_cells")==3,"trigger deleted other actions or duplicated");
                Check(Scalar(db,"SELECT COUNT(*) FROM scripted_cells WHERE ActionsArgs='2,3'")==1,"trigger disappeared after insertion");
                Check(Scalar(db,"SELECT COUNT(*) FROM endfight_action")==3,"endfight deleted other actions or duplicated");
                Check(Scalar(db,"SELECT COUNT(*) FROM endfight_action WHERE args='2,3' AND fighttype=4")==1,"endfight route missing");
                string first=(string)Call("Export",scratch,"triggers",trigger);
                string second=(string)Call("Export",scratch,"triggers",trigger);
                Check(first!=second && File.Exists(first) && File.ReadAllText(first).Split(';').Length==3,"SQL exports overwritten or unterminated");
                using(var editor=new MainEditeur()) using(var form=new MapForm())
                {
                    form.MyMap=new Map { ID=1,Cells=new[] { new CellsData { ID=0 } } };
                    editor.AddTrigger(1,0,form);
                    Check(editor.CellTrigger==0 && form.Edited && form.MyMap.Cells[0].Trigger,"cell zero cannot start a trigger");
                }
                Console.WriteLine("OK: trigger cell zero, correct action tables, repeatable SQL, preserved unrelated actions and distinct export files");
            }
            finally { Execute(db,"DROP DATABASE IF EXISTS `"+schema+"`"); if(Directory.Exists(scratch))Directory.Delete(scratch,true); }
        }
    }
    private static void Execute(MySqlConnection db,string text) { using(var command=new MySqlCommand(text,db))command.ExecuteNonQuery(); }
    private static int Scalar(MySqlConnection db,string text) { using(var command=new MySqlCommand(text,db))return Convert.ToInt32(command.ExecuteScalar()); }
}
