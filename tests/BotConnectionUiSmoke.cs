using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;

// Lot X3 : écrans de la connexion contre des serveurs simulés en boucle locale, avec la boucle de messages de l'interface.
// Compte neuf (B-05) : AlEr ouvre ChooseNickName au-dessus du choix du serveur (contrôles locaux, confirmation, ligne brute,
// AlEs, Ad/AlK, Annuler). Fenêtre de jeu : AN ouvre NEW_LEVEL (V-25) ; M04 puis la coupure du serveur ouvrent la boîte
// CONNECTION (DISCONNECT + SRV_MSG_4, V-20) ; « OK » ferme la fenêtre et rouvre l'écran de connexion prérempli (m-08).
// Données fictives, fenêtres invisibles, aucune capture d'écran (BotUiSmoke, qui capture par PrintWindow, est propre à Windows).
internal static class BotConnectionUiSmoke
{
    private static readonly List<Exception> uiErrors = new List<Exception>();

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Application.ThreadException += (s, e) => uiErrors.Add(e.Exception);
        try
        {
            Application.EnableVisualStyles();
            Run();
            Check(uiErrors.Count == 0, "Interface errors: " + string.Join(" / ", uiErrors.Select(e => e.GetType().Name + " " + e.Message)));
            Console.WriteLine("OK: ChooseNickName au-dessus du choix du serveur (contrôles locaux, confirmation, ligne brute, AlEs, Ad/AlK, Annuler), boîte NEW_LEVEL unique, boîte de perte de connexion avec M04 puis retour à l'écran de connexion prérempli");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump(Func<bool> condition, string message)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
        Application.DoEvents();
        Check(condition(), message);
    }
    private static void Await(Task task) { Pump(() => task.IsCompleted, "A UI action did not complete"); task.GetAwaiter().GetResult(); }
    private static void Send(Socket peer, string packets)
    {
        byte[] data = Encoding.UTF8.GetBytes(packets);
        int sent = 0;
        while (sent < data.Length) sent += peer.Send(data, sent, data.Length - sent, SocketFlags.None);
    }
    /// <summary>Lit une ligne du bot sur un fil à part pendant que la boucle de messages de l'interface tourne.</summary>
    private static string Receive(Socket peer)
    {
        Task<string> read = Task.Run(() =>
        {
            using (var packet = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    if (peer.Receive(one) != 1) throw new IOException("The bot closed the simulated connection");
                    if (one[0] == 0) return Encoding.UTF8.GetString(packet.ToArray()).TrimEnd('\r', '\n');
                    packet.WriteByte(one[0]);
                }
            }
        });
        Pump(() => read.IsCompleted, "The bot sent nothing to the simulated server");
        return read.Result;
    }
    private static void NoLine(Socket peer, string message)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(200);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
        Check(peer.Available == 0, message);
    }
    private static object Get(object target, string field) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static object Prop(object target, string name) =>
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target, null);
    private static object Call(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);
    private static IEnumerable<Control> All(Control value) { yield return value; foreach (Control child in value.Controls) foreach (var nested in All(child)) yield return nested; }
    private static string Step(ChooseNickNameForm form) => Prop(form, "Step").ToString();
    private static string Error(ChooseNickNameForm form) => (string)Prop(form, "ErrorText");
    private static void Type(ChooseNickNameForm form, string value) =>
        form.GetType().GetProperty("Nickname", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value, null);
    private static void Ok(ChooseNickNameForm form) => Await((Task)Call(form, "OkAsync"));
    private static Form Dialog(string name) => BotDialogs.OpenDialogs.FirstOrDefault(x => x.Name == name && !x.IsDisposed && x.Visible);
    private static string DialogText(Form dialog) => All(dialog).OfType<Label>().First(x => x.Name == "dialog-message").Text;
    private static void Hidden(Form form) { form.ShowInTaskbar = false; form.Opacity = 0; }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string work = Path.Combine(TestPaths.Work, "bot-connection-ui"); Directory.CreateDirectory(work); Environment.CurrentDirectory = work;
        try
        {
            AccountConfig.AccountsDico.Clear();
            GlobalConfig.InitializeConfig();
            GlobalConfig.BYPASS = false;
            MessagesReception.Init();
            var config = new AccountConfig("compte-fictif", "secret de test", "Privé");
            NicknameScreen(config);
            LevelAndLostConnection(config);
        }
        finally
        {
            GlobalConfig.writenewconfig("127.0.0.1", "450", "5555", "1.34.1", "2528660", "2362079");
            Environment.CurrentDirectory = previous;
        }
    }

    /// <summary>
    /// StarLoco répond <c>AlEr</c> au <c>Af</c> d'un compte sans pseudo et lit la ligne suivante comme le pseudo
    /// (<c>ChooseNickName.verify</c>) ; <c>AlEs</c> s'il est pris ; sinon <c>Af</c>, <c>Ad</c>, <c>Ac</c>, <c>AH</c>, <c>AlK</c>, <c>AQ</c>.
    /// </summary>
    private static void NicknameScreen(AccountConfig config)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            GlobalConfig.writenewconfig("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port.ToString(), "5555", "1.34.1", "2528660", "2362079");
            Task<Socket> accepted = listener.AcceptSocketAsync();
            using (var servers = new PersoSelection(config, true))
            {
                Hidden(servers); servers.Show();
                Pump(() => accepted.IsCompleted, "The server screen did not connect to the simulated Login");
                var account = (Accounts)Get(servers, "A");
                using (Socket peer = accepted.Result)
                {
                    peer.ReceiveTimeout = 7000;
                    Send(peer, "HCabcdefghijklmnopqrstuvwxyzabcdef\0");
                    Check(Receive(peer) == "1.34.1" && Receive(peer) == "compte-fictif", "The bot did not send its version and account");
                    Receive(peer);
                    Check(Receive(peer) == "Af", "The bot did not ask for its queue position");

                    Send(peer, "AlEr\0");
                    Pump(() => Prop(servers, "NicknameForm") != null, "AlEr did not open the nickname choice");
                    var form = (ChooseNickNameForm)Prop(servers, "NicknameForm");
                    Pump(() => form.Visible, "The nickname choice is not shown");
                    Check(ReferenceEquals(form.Owner, servers) && form.Text == ConnectionTexts.NicknameTitle, "The nickname choice is not owned by the server screen");
                    Check(Step(form) == "Input" && (string)Prop(form, "HelpText") == ConnectionTexts.NicknameHelp && Error(form) == null,
                        "The first nickname request is not the help and input step");
                    Check(((Label)Get(servers, "status")).Text.StartsWith(ConnectionTexts.NicknameTitle, StringComparison.Ordinal), "The server screen does not say a nickname is needed");
                    Check(All(form).OfType<Button>().All(b => b.Width >= 85 && b.Height >= 30), "Nickname buttons cannot be used comfortably");

                    // Contrôles du client au premier OK (ChooseNickName.click) : rien ne part.
                    Type(form, "ab"); Ok(form);
                    Check(Error(form) == ConnectionTexts.NicknameTooShort && Step(form) == "Input", "A two-character nickname was not refused locally");
                    Type(form, "COMPTE-FICTIF"); Ok(form);
                    Check(Error(form) == ConnectionTexts.NicknameEqualsLogin && Step(form) == "Input", "A nickname equal to the login was not refused locally");
                    Type(form, "Pris"); Ok(form);
                    Check(Step(form) == "Confirm" && (string)Prop(form, "HelpText") == ConnectionTexts.NicknameConfirm("Pris") && Error(form) == null,
                        "The first valid OK did not ask for the DO_CHOOSE_NICKNAME confirmation");
                    NoLine(peer, "A nickname was sent before its confirmation");
                    Call(form, "CancelStep");
                    Check(Step(form) == "Input" && !form.IsDisposed && account.Connexion != null, "Cancel from the confirmation did not return to the input");
                    Ok(form); Ok(form);
                    Check(Receive(peer) == "Pris", "The confirmed nickname was not sent as a raw line");
                    Check(Step(form) == "Waiting", "The window accepts another nickname before the Login answers");
                    Ok(form);
                    NoLine(peer, "A second nickname left before the Login answered");

                    Send(peer, "AlEs\0");
                    Pump(() => Error(form) == ConnectionTexts.NicknameAlreadyUsed, "AlEs did not reopen the input with NICKNAME_ALREADY_USED");
                    Check(ReferenceEquals(Prop(servers, "NicknameForm"), form) && (string)Prop(form, "Nickname") == "Pris" && Step(form) == "Input",
                        "AlEs opened a second window or lost the refused nickname");
                    Type(form, "Libre"); Ok(form); Ok(form);
                    Check(Receive(peer) == "Libre", "The second nickname was not sent");
                    Send(peer, "Af0|0|0|1|-1\0AdLibre\0Ac0\0AH601;1;110;1\0AlK0\0AQquestion\0");
                    Check(Receive(peer) == "Ax", "The connection did not continue after the accepted nickname");
                    Pump(() => form.IsDisposed && Prop(servers, "NicknameForm") == null, "Ad/AlK did not close the nickname choice");
                    Check(account.Name == "Libre" && !account.Nickname.IsPending && account.Connexion != null, "The accepted nickname was not applied");

                    // « Annuler » depuis la saisie : déconnexion demandée, sans boîte de perte ; l'écran garde « Retenter ».
                    int lost = 0; account.ConnectionLost += loss => lost++;
                    Send(peer, "AlEr\0");
                    Pump(() => Prop(servers, "NicknameForm") != null, "A new AlEr did not reopen the nickname choice");
                    var again = (ChooseNickNameForm)Prop(servers, "NicknameForm");
                    Call(again, "CancelStep");
                    Pump(() => again.IsDisposed && Prop(servers, "NicknameForm") == null && account.Connexion == null, "Cancel did not close the nickname choice and disconnect");
                    Pump(() => ((Control)Get(servers, "retry")).Enabled, "Retry stayed disabled after cancelling the nickname");
                    Check(account.LastDisconnect.Cause == DisconnectCause.Requested && lost == 0, "Cancel was not a requested disconnection");
                }
                servers.Close();
            }
        }
        finally { listener.Stop(); }
    }

    /// <summary>
    /// Fenêtre de jeu : AN ouvre la boîte NEW_LEVEL, un deuxième AN la remplace ; M04 n'ouvre rien seul ; la coupure du serveur ouvre
    /// la boîte CONNECTION (DISCONNECT puis SRV_MSG_4) ; « OK » ferme la fenêtre et rouvre l'écran de connexion prérempli.
    /// </summary>
    private static void LevelAndLostConnection(AccountConfig config)
    {
        const string levelName = "nouveau-niveau/compte-fictif", lostName = "connexion-perdue/compte-fictif";
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var account = new Accounts(config);
            Task<Socket> accepted = listener.AcceptSocketAsync();
            Await(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port));
            Pump(() => accepted.IsCompleted, "The game window account did not connect");
            account.Game.character.SetPerso_Data(7, "Personnage fictif", 25, 0, 8);
            var dashboard = new GameClientFullform(account);
            Hidden(dashboard); dashboard.Show(); Application.DoEvents();
            using (Socket peer = accepted.Result)
            {
                Await(MessagesReception.ReceptionAsync(account.Connexion, "AN26"));
                Pump(() => Dialog(levelName) != null, "AN did not open the NEW_LEVEL box");
                Check(Dialog(levelName).Text == ConnectionTexts.InformationsTitle && DialogText(Dialog(levelName)) == ConnectionTexts.NewLevel(26),
                    "The NEW_LEVEL box text is wrong: " + DialogText(Dialog(levelName)));
                Check(ReferenceEquals(Dialog(levelName).Owner, dashboard), "The NEW_LEVEL box is not owned by the game window");
                Await(MessagesReception.ReceptionAsync(account.Connexion, "AN27"));
                Pump(() => BotDialogs.OpenDialogs.Count(x => x.Name == levelName && !x.IsDisposed && x.Visible) == 1 && DialogText(Dialog(levelName)) == ConnectionTexts.NewLevel(27),
                    "A second AN did not replace the first NEW_LEVEL box");
                Check(account.Game.Chat.Messages.Count(m => m.Text == ConnectionTexts.NewLevelLine(26) || m.Text == ConnectionTexts.NewLevelLine(27)) == 2,
                    "The new levels were not written in the chat");
                Await(MessagesReception.ReceptionAsync(account.Connexion, "M04"));
                Application.DoEvents();
                Check(Dialog(lostName) == null && !dashboard.IsDisposed && account.Connexion != null, "M04 opened a box or disconnected before the server closed");
                peer.Shutdown(SocketShutdown.Both);
            }
            Pump(() => Dialog(lostName) != null, "The server closing the socket did not open the lost-connection box");
            Form lost = Dialog(lostName);
            Check(lost.Text == "Connexion" && DialogText(lost) == "Connexion interrompue avec le serveur.\n\nVotre connexion a été coupée pour des raisons de maintenance.",
                "The lost-connection box is not DISCONNECT then SRV_MSG_4: " + DialogText(lost));
            Check(Dialog(levelName) == null && ReferenceEquals(lost.Owner, dashboard), "The NEW_LEVEL box stayed over the lost-connection box");
            Check(!dashboard.IsDisposed, "The game window closed before the lost-connection box was answered");
            All(lost).OfType<Button>().First(b => b.Visible).PerformClick();
            Pump(() => dashboard.IsDisposed && Application.OpenForms.OfType<LoginForm>().Any(), "OK did not close the game window and open the login screen");
            var login = Application.OpenForms.OfType<LoginForm>().First();
            Check(((TextBox)Get(login, "login")).Text == "compte-fictif" && ((TextBox)Get(login, "password")).Text == "secret de test",
                "The login screen lost the account credentials");
            string status = ((Label)Get(login, "status")).Text;
            Check(status.Contains("Connexion interrompue") && status.Contains("maintenance") && status.Contains("Reconnectez-vous"),
                "The login screen does not explain the disconnection: " + status);
            login.Close();
            Application.DoEvents();
        }
        finally { listener.Stop(); }
    }
}
