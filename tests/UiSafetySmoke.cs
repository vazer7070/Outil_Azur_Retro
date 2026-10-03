using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Outil_Azur_complet.maps;
using Outil_Azur_complet.outil_recherche;

internal static class UiSafetySmoke
{
    [STAThread]
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
        using (var search = new Recherche())
        {
            search.SayNumberItems(12);
            var combo = (ComboBox)GetField(search, "iTalk_ComboBox1");
            Check(combo.Items.Count == 11, "Set bonus choices stop before the actual item count");
            var delete = (Control)GetField(search, "iTalk_Button_23");
            Check(!delete.Enabled, "Unsafe search deletion is still enabled");
            var picture = new PictureBox();
            typeof(Recherche).GetMethod("LoadPicture", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { picture, Path.Combine(TestPaths.Work, "missing", "image.png") });
            picture.Dispose();
        }

        using (var size = new OtherSizeForm())
        {
            ((Control)GetField(size, "iTalk_TextBox_Small1")).Text = "19";
            ((Control)GetField(size, "iTalk_TextBox_Small2")).Text = "22";
            typeof(OtherSizeForm).GetMethod("ApplySize", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(size, new object[] { size, EventArgs.Empty });
            Check(size.IsValid && size.MapWidth == 19 && size.MapHeight == 22,
                "Custom map size was not applied");
        }
        Console.WriteLine("OK: search safeguards and custom map size workflow");
    }

    private static object GetField(object instance, string name)
    {
        return instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
