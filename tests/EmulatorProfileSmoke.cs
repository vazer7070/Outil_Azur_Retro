using System;
using System.IO;
using System.Reflection;
using Tools_protocol.Emulators;
using Tools_protocol.Json;
using Tools_protocol.Managers;

internal static class EmulatorProfileSmoke
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
        Console.WriteLine("EmulatorProfileSmoke OK");
    }

    private static void Run()
    {
        string work = Path.Combine(TestPaths.Work, "emulators-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        Check(JsonManager.Initialize(Path.Combine(work, "auth.json"), Path.Combine(work, "world.json"), Path.Combine(work, "config.json")),
            "Default configuration could not be written: " + JsonManager.LastError);

        // Every known emulator is selectable, case-insensitively, and unknown names fall back to no SQL tool.
        foreach (string id in new[] { "Kryone", "StarLoco", "Sunshine", "Codebreak" })
            Check(EmulatorRegistry.Find(id.ToLowerInvariant()) != null, id + " is not registered");
        Check(!EmulatorRegistry.Select("Inconnu") && !EmulatorRegistry.HasEmulator, "Unknown emulator was accepted");
        Check(EmulatorRegistry.Current.Features == EmulatorFeature.None, "Fallback profile exposes features");
        Check(EmuManager.EMUSELECTED == "", "Fallback identifier is not empty");

        // Kryone resolves tables in the right database: characters in auth, item instances in world.
        Check(EmulatorRegistry.Select("kryone") && EmuManager.EMUSELECTED == "Kryone", "Kryone selection failed");
        EmulatorProfile kryone = EmulatorRegistry.Current;
        Check(kryone.Locate("perso") == TableLocation.Auth && kryone.Table("perso") == "players", "Kryone characters: " + kryone.Table("perso"));
        Check(kryone.Locate("items") == TableLocation.World && kryone.Table("items") == "items", "Kryone items: " + kryone.Table("items"));
        Check(kryone.Table("panoplies") == "itemsets" && kryone.Table("comptes") == "accounts", "Kryone auth tables were not resolved");
        Check(EmuManager.ReturnTable("Template", "Kryone") == "item_template", "Legacy table lookup changed");
        Check(kryone.Table("inexistante") == "", "Unknown logical table resolved to a name");
        foreach (EmulatorFeature feature in Enum.GetValues(typeof(EmulatorFeature)))
            Check(kryone.Supports(feature), "Kryone lacks " + feature);

        // Partial emulators only expose what Azur can actually do with their schema.
        EmulatorProfile sunshine = EmulatorRegistry.Find("Sunshine");
        Check(sunshine.Supports(EmulatorFeature.Accounts) && !sunshine.Supports(EmulatorFeature.AccountEditing), "Sunshine features are wrong");
        Check(!sunshine.UsesWorldDatabase, "Sunshine should only use the auth database");
        EmulatorProfile codebreak = EmulatorRegistry.Find("Codebreak");
        Check(codebreak.Locate("perso") == TableLocation.World, "Codebreak characters should live in world");
        EmulatorProfile starloco = EmulatorRegistry.Find("StarLoco");
        Check(starloco.Features == EmulatorFeature.None && starloco.UsesWorldDatabase, "StarLoco profile changed");

        // The legacy panoply helpers keep their Kryone behaviour and are inert elsewhere.
        EmulatorRegistry.Select("Kryone");
        Check(EmuManager.UpdateRowPano("", "12") == "12" && EmuManager.UpdateRowPano("1,2", "12") == "1,2,12", "Panoply row update changed");
        Check(EmuManager.ReturnPanoCol() == "items" && EmuManager.ReturnInfoCol("pano") == "name", "Panoply columns changed");
        EmulatorRegistry.Select("StarLoco");
        Check(EmuManager.UpdateRowPano("1", "12") == "" && EmuManager.ReturnPanoCol() == "", "Panoply helpers wrote for StarLoco");

        // The menu refuses SQL tools an emulator does not support, but keeps offline tools.
        Type menu = Type.GetType("Outil_Azur_complet.Menu, Outil_Azur_complet", true);
        Type init = Type.GetType("Outil_Azur_complet.InitializeForm, Outil_Azur_complet", true);
        init.GetField("NoDB", BindingFlags.Public | BindingFlags.Static).SetValue(null, false);
        MethodInfo unavailable = menu.GetMethod("Unavailability", BindingFlags.NonPublic | BindingFlags.Static);
        Func<string, string> reason = tool => (string)unavailable.Invoke(null, new object[] { tool });
        Check(reason("Éditeur de maps") == null && reason("AzurBot") == null, "Offline tools were blocked for StarLoco");
        Check(reason("Éditeur de sorts") != null && reason("Éditeur de compte") != null, "SQL tools were allowed for StarLoco");
        EmulatorRegistry.Select("Sunshine");
        Check(reason("Éditeur de compte") == null && reason("Éditeur de personnage") != null, "Sunshine menu gating is wrong");
        EmulatorRegistry.Select("Kryone");
        Check(reason("Éditeur d'objets") == null && reason("Éditeur de quêtes") == null, "Kryone tools were blocked");
        init.GetField("NoDB", BindingFlags.Public | BindingFlags.Static).SetValue(null, true);
        Check(reason("Éditeur de compte") != null && reason("Gestionnaire") == null, "Missing database was not reported");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
