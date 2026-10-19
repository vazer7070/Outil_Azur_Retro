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
using Tool_BotProtocol.Game.Session;

// Lot X3 : écrans de la connexion contre des serveurs simulés en boucle locale, avec la boucle de messages de l'interface.
// Compte neuf (B-05) : AlEr ouvre ChooseNickName au-dessus du choix du serveur (contrôles locaux, confirmation, ligne brute,
// AlEs, Ad/AlK, Annuler). Fenêtre de jeu : AN ouvre NEW_LEVEL (V-20) ; M04 (m-08) puis la coupure du serveur ouvrent la
// boîte CONNECTION (DISCONNECT + SRV_MSG_4) ; « OK » ferme la fenêtre et rouvre l'écran de connexion prérempli (V-25).
// Lot X32 : en jeu, AlEr ouvre ChooseNickName au-dessus de la fenêtre de jeu (B-07), TC ouvre la boîte « mini-jeu » (V-45).
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
            Console.WriteLine("OK: ChooseNickName au-dessus du choix du serveur (contrôles locaux, confirmation, ligne brute, AlEs, Ad/AlK, Annuler), boîte NEW_LEVEL unique, boîte de perte de connexion avec M04 et la question de reconnexion : Non → écran de connexion prérempli, Oui → même compte reconnecté ; X32 : nom en jeu (AlEr → ChooseNickName sur la fenêtre de jeu, refus locaux, ligne brute, AlEs, GM|~ ferme, Annuler sans déconnexion), boîte « mini-jeu » sur TC avec TV envoyé");
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
    private static Button DialogButton(Form dialog, BotDialogResult value) => All(dialog).OfType<Button>().First(x => Equals(x.Tag, value));
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
            NameChangeAndMiniGame(config);
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
    /// Lot X32 : en jeu, <c>AlEr</c> ouvre la fenêtre <c>ChooseNickName</c> au-dessus de la fenêtre de jeu (contrôles locaux : trop
    /// court, nom du compte, première lettre lue comme un paquet par StarLoco), « OK » puis « OK » envoie la ligne brute, <c>AlEs</c>
    /// rouvre la saisie avec <c>NICKNAME_ALREADY_USED</c>, <c>AlEr</c> puis le <c>GM|~</c> du personnage au nouveau nom referment la
    /// fenêtre sans déconnecter ; « Annuler » la ferme sans déconnecter ; <c>TC30|7001010000</c> envoie <c>TV0</c> (sans cellule
    /// connue) et ouvre la boîte « mini-jeu » dans la fenêtre de jeu.
    /// </summary>
    private static void NameChangeAndMiniGame(AccountConfig config)
    {
        const string miniGameName = "mini-jeu/compte-fictif";
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var account = new Accounts(config);
            Task<Socket> accepted = listener.AcceptSocketAsync();
            Await(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port));
            Pump(() => accepted.IsCompleted, "The game window account did not connect");
            account.Game.character.SetPerso_Data(7, "Ancien", 25, 0, 8);
            account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            var dashboard = new GameClientFullform(account);
            Hidden(dashboard); dashboard.Show(); Application.DoEvents();
            object prompts = Get(dashboard, "connectionPrompts");
            Check(prompts != null, "The game window has no ConnectionPrompts");
            NameChange change = account.Game.Session.NameChange;
            int lost = 0; account.ConnectionLost += loss => lost++;
            using (Socket peer = accepted.Result)
            {
                peer.ReceiveTimeout = 7000;
                Await(MessagesReception.ReceptionAsync(account.Connexion, "AlEr"));
                Pump(() => Prop(prompts, "NameForm") != null, "AlEr in game did not open the name window");
                var form = (ChooseNickNameForm)Prop(prompts, "NameForm");
                Pump(() => form.Visible, "The name window is not shown");
                Check(ReferenceEquals(form.Owner, dashboard) && form.Text == ConnectionTexts.NicknameTitle && Step(form) == "Input" && Error(form) == null,
                    "The first name request is not an input window owned by the game window");
                Check(change.IsPending && !account.Nickname.IsPending, "AlEr in game did not open the NameChange request");
                NoLine(peer, "AlEr in game sent a line");

                // Contrôles locaux (client, puis règle de StarLoco sur la première lettre) : rien ne part.
                Type(form, "ab"); Ok(form);
                Check(Error(form) == ConnectionTexts.NicknameTooShort && Step(form) == "Input", "A two-character name was not refused locally");
                Type(form, "COMPTE-FICTIF"); Ok(form);
                Check(Error(form) == ConnectionTexts.NicknameEqualsLogin && Step(form) == "Input", "The account name was not refused locally");
                Type(form, "Gaston"); Ok(form);
                Check(Error(form) == NameChange.UnreadableByServer("Gaston") && Step(form) == "Input", "A name starting with a packet letter was not refused locally: " + Error(form));
                NoLine(peer, "A locally refused name was sent");
                Type(form, "Nouveau"); Ok(form);
                Check(Step(form) == "Confirm" && (string)Prop(form, "HelpText") == ConnectionTexts.NicknameConfirm("Nouveau") && Error(form) == null,
                    "The first valid OK did not ask for the confirmation");
                Ok(form);
                Check(Receive(peer) == "Nouveau" && Step(form) == "Waiting", "The confirmed name was not sent as a raw line");
                Send(peer, "AlEs\0");
                Pump(() => Error(form) == ConnectionTexts.NicknameAlreadyUsed, "AlEs did not reopen the input with NICKNAME_ALREADY_USED");
                Check(ReferenceEquals(Prop(prompts, "NameForm"), form) && (string)Prop(form, "Nickname") == "Nouveau" && Step(form) == "Input" && lost == 0 && account.Connexion != null,
                    "AlEs opened a second window, lost the refused name or disconnected");
                Type(form, "Nouvel"); Ok(form); Ok(form);
                Check(Receive(peer) == "Nouvel", "The second name was not sent");
                // StarLoco renvoie AlEr même quand il accepte, puis le GM|~ du personnage au nouveau nom.
                Send(peer, "AlEr\0");
                Pump(() => Step(form) == "Input" && Error(form) == null, "AlEr after the sent name did not reopen a blank input");
                Check((string)Prop(form, "Nickname") == "Nouvel" && !form.IsDisposed, "AlEr after the sent name closed the window or lost the name");
                Send(peer, "GM|~0;1;0;7;Nouvel;1;10^100;1;0,0,0,37;-1;-1;-1;;0;;;;;0;;0\0");
                Pump(() => form.IsDisposed && Prop(prompts, "NameForm") == null, "The GM|~ with the new name did not close the name window");
                Check(account.Game.character.Name == "Nouvel" && !change.IsPending && account.Connexion != null && lost == 0 && !dashboard.IsDisposed,
                    "The accepted name disconnected or was not applied");
                NoLine(peer, "The accepted name sent a line");

                // « Annuler » : fermeture sans déconnexion (le client, lui, se déconnecte : écart documenté).
                Send(peer, "AlEr\0");
                Pump(() => Prop(prompts, "NameForm") != null && ((ChooseNickNameForm)Prop(prompts, "NameForm")).Visible, "A new AlEr did not reopen the name window");
                var again = (ChooseNickNameForm)Prop(prompts, "NameForm");
                Check(!ReferenceEquals(again, form) && ReferenceEquals(again.Owner, dashboard) && Step(again) == "Input" && Error(again) == null,
                    "The reopened name window is the old one, is not owned by the game window or is not blank");
                Call(again, "CancelStep");
                Pump(() => again.IsDisposed && Prop(prompts, "NameForm") == null, "Cancel did not close the name window");
                Check(account.Connexion != null && lost == 0 && !change.IsPending && !dashboard.IsDisposed, "Cancel in game disconnected");
                NoLine(peer, "Cancel sent a line");

                // TC : fin envoyée aussitôt (sans cellule connue : TV0 seul) et boîte d'information dans la fenêtre de jeu.
                Send(peer, "TC30|7001010000\0");
                Check(Receive(peer) == "TV0", "TC30 did not send TV0");
                Pump(() => Dialog(miniGameName) != null, "TC did not open the mini-game box");
                Form box = Dialog(miniGameName);
                TutorialRequest last = account.Game.Session.Tutorial.Last;
                Check(last != null && last.Id == 30 && box.Text == ConnectionTexts.InformationsTitle && DialogText(box) == last.Describe() && ReferenceEquals(box.Owner, dashboard),
                    "The mini-game box is wrong: " + DialogText(box));
                Check(DialogText(box).Contains("Roulette du Bouftou") && DialogText(box).Contains("TV0"), "The mini-game box does not name the roulette and the end packet: " + DialogText(box));
                Send(peer, "TC31|7001010000\0");
                Check(Receive(peer) == "TV1", "TC31 did not send TV1");
                Pump(() => BotDialogs.OpenDialogs.Count(x => x.Name == miniGameName && !x.IsDisposed && x.Visible) == 1 && DialogText(Dialog(miniGameName)).Contains("gagné"),
                    "A second TC did not replace the mini-game box");
                All(Dialog(miniGameName)).OfType<Button>().First(x => x.Visible).PerformClick();
                Pump(() => Dialog(miniGameName) == null, "OK did not close the mini-game box");
                Check(account.Connexion != null && lost == 0 && !dashboard.IsDisposed, "The mini-game disconnected");
                account.Disconnect();
            }
            Application.DoEvents();
            Check(Dialog("connexion-perdue/compte-fictif") == null, "A requested disconnection opened the lost-connection box");
            dashboard.Close(); Application.DoEvents();
            account.Dispose();
        }
        finally { listener.Stop(); }
    }

    /// <summary>
    /// Fenêtre de jeu : AN ouvre la boîte NEW_LEVEL, un deuxième AN la remplace ; M04 n'ouvre rien seul ; la coupure du serveur ouvre
    /// la boîte CONNECTION (DISCONNECT, SRV_MSG_4 puis ATTEMPT_RECONNECT, en Oui / Non comme SIMPLE_AUTO_RECONNECT le demande) ;
    /// « Non » ferme la fenêtre et rouvre l'écran de connexion prérempli ; « Oui », sur une deuxième coupure, reconnecte le même compte.
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
            Check(lost.Text == "Connexion" && DialogText(lost) == "Connexion interrompue avec le serveur.\n\nVotre connexion a été coupée pour des raisons de maintenance."
                + "\n\nVoulez-vous tenter de vous reconnecter en jeu ?", "The lost-connection box is not DISCONNECT, SRV_MSG_4 then ATTEMPT_RECONNECT: " + DialogText(lost));
            Check(All(lost).OfType<Button>().Where(b => b.Visible).Select(b => b.Tag).OfType<BotDialogResult>().OrderBy(x => x)
                .SequenceEqual(new[] { BotDialogResult.Yes, BotDialogResult.No }), "The lost-connection box does not ask Yes / No");
            Check(Dialog(levelName) == null && ReferenceEquals(lost.Owner, dashboard), "The NEW_LEVEL box stayed over the lost-connection box");
            Check(!dashboard.IsDisposed, "The game window closed before the lost-connection box was answered");
            DialogButton(lost, BotDialogResult.No).PerformClick();
            Pump(() => dashboard.IsDisposed && Application.OpenForms.OfType<LoginForm>().Any(), "No did not close the game window and open the login screen");
            Check(!Application.OpenForms.OfType<PersoSelection>().Any(), "No reconnected the account");
            var login = Application.OpenForms.OfType<LoginForm>().First();
            Check(((TextBox)Get(login, "login")).Text == "compte-fictif" && ((TextBox)Get(login, "password")).Text == "secret de test",
                "The login screen lost the account credentials");
            string status = ((Label)Get(login, "status")).Text;
            Check(status.Contains("Connexion interrompue") && status.Contains("maintenance") && status.Contains("Reconnectez-vous"),
                "The login screen does not explain the disconnection: " + status);
            login.Close();
            Application.DoEvents();

            // « Oui » (Login.autoLogin du client) : la fenêtre se ferme et le même compte se reconnecte aussitôt au Login.
            var loginListener = new TcpListener(IPAddress.Loopback, 0); loginListener.Start();
            try
            {
                GlobalConfig.writenewconfig("127.0.0.1", ((IPEndPoint)loginListener.LocalEndpoint).Port.ToString(), "5555", "1.34.1", "2528660", "2362079");
                var second = new Accounts(config);
                Task<Socket> acceptedAgain = listener.AcceptSocketAsync();
                Await(second.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port));
                Pump(() => acceptedAgain.IsCompleted, "The second game window account did not connect");
                second.Game.character.SetPerso_Data(7, "Personnage fictif", 27, 0, 8);
                var window = new GameClientFullform(second);
                Hidden(window); window.Show(); Application.DoEvents();
                Task<Socket> relogin = loginListener.AcceptSocketAsync();
                using (Socket peer = acceptedAgain.Result) peer.Shutdown(SocketShutdown.Both);
                Pump(() => Dialog(lostName) != null, "The second server closing did not open the lost-connection box");
                Form ask = Dialog(lostName);
                Check(DialogText(ask) == "Connexion interrompue avec le serveur.\n\nVoulez-vous tenter de vous reconnecter en jeu ?",
                    "A loss without M0 is not DISCONNECT then ATTEMPT_RECONNECT: " + DialogText(ask));
                DialogButton(ask, BotDialogResult.Yes).PerformClick();
                Pump(() => window.IsDisposed && relogin.IsCompleted && Application.OpenForms.OfType<PersoSelection>().Any(),
                    "Yes did not close the game window and connect the account again");
                Check(!Application.OpenForms.OfType<LoginForm>().Any(), "Yes went back to the login screen instead of reconnecting");
                var servers = Application.OpenForms.OfType<PersoSelection>().First();
                Check(ReferenceEquals(((Accounts)Get(servers, "A")).accountConfig, config), "Yes reconnected another account");
                using (Socket peer = relogin.Result)
                {
                    peer.ReceiveTimeout = 7000;
                    Send(peer, "HCabcdefghijklmnopqrstuvwxyzabcdef\0");
                    Check(Receive(peer) == "1.34.1" && Receive(peer) == "compte-fictif", "Yes did not log the same account in again");
                    servers.Close();
                    Application.DoEvents();
                }
            }
            finally { loginListener.Stop(); }
        }
        finally { listener.Stop(); }
    }
}
