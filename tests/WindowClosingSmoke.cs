using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using AzurMenu=Outil_Azur_complet.Menu;

internal static class WindowClosingSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,args)=>{
            string file=Path.Combine(TestPaths.ApplicationBin,new AssemblyName(args.Name).Name+".dll");
            if(!File.Exists(file))file=Path.ChangeExtension(file,"exe");
            return File.Exists(file)?Assembly.LoadFrom(file):null;
        };
        Run();
    }
    private static void Run()
    {
        using(var menu=new AzurMenu()) using(var module=new Form())
        {
            module.ShowInTaskbar=false;
            module.StartPosition=FormStartPosition.Manual;
            module.Location=new Point(-32000,-32000);
            module.Show();
            bool cancel=true;
            module.FormClosing+=(sender,e)=>e.Cancel=cancel;
            var cache=(Dictionary<string,Form>)typeof(AzurMenu).GetField("_openForms",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(menu);
            cache.Add("unsaved-module",module);
            MethodInfo close=typeof(AzurMenu).GetMethod("OnFormClosing",BindingFlags.NonPublic|BindingFlags.Instance);
            var first=new FormClosingEventArgs(CloseReason.UserClosing,false);
            close.Invoke(menu,new object[] { first });
            if(!first.Cancel || module.IsDisposed)throw new Exception("Menu ignored module cancellation");
            cancel=false;
            var second=new FormClosingEventArgs(CloseReason.UserClosing,false);
            close.Invoke(menu,new object[] { second });
            if(second.Cancel || !module.IsDisposed)throw new Exception("Menu did not close saved module");
            Console.WriteLine("OK: cancelled module closure keeps menu and pending work open");
        }
    }
}
