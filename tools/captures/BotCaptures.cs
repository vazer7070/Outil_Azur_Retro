using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.NPC;

// Captures d'écran de l'interface du bot (fenêtre de jeu Retro) pour la documentation : chaque vue est ouverte contre un
// serveur fictif local (paquets synthétiques repris des tests tests/Bot*Smoke.cs), avec des noms inventés, puis l'écran Xvfb
// est photographié par « import -window root » (ImageMagick) et découpé à la fenêtre. Aucun compte ni personnage réel.
//
// Données lues (jamais copiées dans le dépôt) :
//  - ressources du bot à côté de l'exécutable (Outil_Azur_complet/bin/Debug/ressources : textes du client, icônes, décors) ;
//  - facultatif, AZUR_CAPTURE_SQL : export SQL du serveur de jeu (table maps) pour dessiner une vraie carte (cellules) ;
//  - facultatif, AZUR_CAPTURE_CLIENT : dossier du client 1.34 (data/maps/*.swf) pour le fond de cette carte.
// Sans ces deux variables, la carte est une prairie synthétique composée avec les décors versionnés.
//
// Usage : mono BotCaptures.exe <dossier de sortie> [vue…]   (voir tools/captures/run-captures.sh)
internal static class BotCaptures
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    internal static readonly Size WindowSize = new Size(1200, 800);
    private static readonly List<Exception> uiErrors = new List<Exception>();
    private static readonly List<string> produced = new List<string>(), skipped = new List<string>(), notes = new List<string>();
    private static string output, bin, work;
    private static HashSet<string> only;

    [STAThread]
    private static int Main(string[] args)
    {
        bin = Environment.GetEnvironmentVariable("AZUR_TEST_BIN") ?? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Outil_Azur_complet", "bin", "Debug"));
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(bin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            if (!File.Exists(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Console.OutputEncoding = new UTF8Encoding(false);
        // Nombres et dates comme sur un Windows français, quelle que soit la langue du système qui fait les captures.
        CultureInfo french = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = french;
        Thread.CurrentThread.CurrentCulture = Thread.CurrentThread.CurrentUICulture = french;
        if (args.Length == 0) { Console.Error.WriteLine("Usage : BotCaptures.exe <dossier de sortie> [vue…]"); return 2; }
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        only = args.Length > 1 ? new HashSet<string>(args.Skip(1), StringComparer.OrdinalIgnoreCase) : null;
        work = Path.Combine(Path.GetTempPath(), "azur-captures-" + Process.GetCurrentProcess().Id);
        Directory.CreateDirectory(work); Environment.CurrentDirectory = work;
        Application.ThreadException += (s, e) => { uiErrors.Add(e.Exception); Console.Error.WriteLine("Erreur d'interface : " + e.Exception); };
        Application.EnableVisualStyles();
        try { Scenes.Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Console.WriteLine("captures=" + produced.Count + " absentes=" + skipped.Count + " erreurs-interface=" + uiErrors.Count);
        foreach (string line in notes) Console.WriteLine("note : " + line);
        foreach (string line in skipped) Console.WriteLine("non capturée : " + line);
        foreach (Exception error in uiErrors) Console.WriteLine("erreur d'interface : " + error.GetType().Name + " " + error.Message);
        return skipped.Count == 0 ? 0 : 3;
    }

    internal static string Bin => bin;
    internal static bool Wanted(string name) => only == null || only.Contains(name);
    internal static void Note(string text) { notes.Add(text); Console.WriteLine("note : " + text); }
    internal static void Skip(string name, string reason) { skipped.Add(name + " : " + reason); Console.WriteLine("non capturée : " + name + " : " + reason); }

    // ------------------------------------------------------------------------------------------------ attente et réseau

    internal static void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
    }

    internal static bool PumpUntil(Func<bool> done, int seconds = 8)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(seconds);
        while (!done()) { if (DateTime.UtcNow > end) return false; Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents(); return true;
    }

    internal static void Complete(Task task)
    {
        if (!PumpUntil(() => task.IsCompleted, 15)) throw new TimeoutException("Tâche bloquée");
        task.GetAwaiter().GetResult();
    }

    internal static object Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Any);
            if (field != null) return field.GetValue(target);
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    internal static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }

    // ------------------------------------------------------------------------------------------------ capture

    /// <summary>Photographie l'écran Xvfb et le découpe au rectangle demandé (coordonnées de l'écran).</summary>
    internal static void Shot(string name, Rectangle area)
    {
        Pump(400);
        string file = Path.Combine(output, name + ".png");
        string crop = area.Width + "x" + area.Height + "+" + area.X + "+" + area.Y;
        var start = new ProcessStartInfo("import", "-window root -crop " + crop + " +repage \"" + file + "\"") { UseShellExecute = false, RedirectStandardError = true };
        using (Process process = Process.Start(start))
        {
            // L'interface continue de se dessiner pendant la prise de vue.
            while (!process.WaitForExit(20)) Application.DoEvents();
            if (process.ExitCode != 0 || !File.Exists(file)) { Skip(name, "import a échoué : " + process.StandardError.ReadToEnd().Trim()); return; }
        }
        produced.Add(name); Console.WriteLine("capture " + name + " " + area.Size.Width + "x" + area.Size.Height);
    }

    /// <summary>Zone cliente de la fenêtre (Xvfb n'a pas de gestionnaire de fenêtres : ni titre ni bordure).</summary>
    internal static void Shot(string name, Form form) => Shot(name, form.RectangleToScreen(form.ClientRectangle));

    internal static void Place(Form form, Size size)
    {
        form.StartPosition = FormStartPosition.Manual; form.Location = Point.Empty; form.Size = size;
        form.ShowInTaskbar = false;
    }
}

/// <summary>Serveur fictif local : accepte la connexion du compte et lit (sans y répondre) les paquets envoyés par le bot.</summary>
internal sealed class FakeServer : IDisposable
{
    private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
    private readonly List<string> received = new List<string>();
    private Socket peer;
    private Thread reader;

    public FakeServer() { listener.Start(); }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public IReadOnlyList<string> Received { get { lock (received) return received.ToArray(); } }

    public void Attach(Accounts account)
    {
        Task<Socket> accept = listener.AcceptSocketAsync();
        BotCaptures.Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, Port));
        BotCaptures.Complete(accept);
        peer = accept.Result;
        reader = new Thread(Drain) { IsBackground = true, Name = "serveur fictif" }; reader.Start();
    }

    private void Drain()
    {
        var buffer = new MemoryStream(); var one = new byte[4096];
        try
        {
            while (true)
            {
                int count = peer.Receive(one); if (count <= 0) return;
                for (int i = 0; i < count; i++)
                {
                    if (one[i] != 0) { buffer.WriteByte(one[i]); continue; }
                    string packet = Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r', '\n'); buffer.SetLength(0);
                    lock (received) received.Add(packet);
                }
            }
        }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        try { peer?.Shutdown(SocketShutdown.Both); } catch (SocketException) { } catch (ObjectDisposedException) { }
        peer?.Close(); listener.Stop();
    }
}
