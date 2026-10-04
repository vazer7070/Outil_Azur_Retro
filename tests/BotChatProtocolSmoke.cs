using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;
using Tool_BotProtocol.Frames.Messages;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Combats;

// Chat protocol of the bot against a local fake server (no real server, no UI): BM per channel and whisper, text rules of
// the 1.34 client, one cC± per letter, cMK with 4 or 5 fields, cMKF/cMKT, cME, cS/BS, cs, M1, eU/eUK, eL/eA/eR, console commands.
internal static class BotChatProtocolSmoke
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string path = Path.Combine(TestPaths.ApplicationBin, new AssemblyName(e.Name).Name + ".dll");
            if (!File.Exists(path)) path = Path.ChangeExtension(path, "exe");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        try { Run(); Console.WriteLine("OK: BM par canal et chuchotement, texte préparé comme le client, cC± lettre par lettre, cMK 4/5 champs, cMKF/cMKT/cME, smileys, émotes, cs, M1, commandes de console"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Complete(Task task)
    {
        if (!task.Wait(TimeSpan.FromSeconds(6))) throw new TimeoutException("Chat loopback timed out");
    }
    private static ChatResult Await(Task<ChatResult> task) { Complete(task); return task.Result; }
    private static void Feed(Accounts account, string packet) => Complete(MessagesReception.ReceptionAsync(account.Connexion, packet));
    private static string Read(Socket socket)
    {
        using (var stream = new MemoryStream())
        {
            byte[] one = new byte[1];
            while (true)
            {
                if (socket.Receive(one) != 1) throw new IOException("Loopback disconnected");
                if (one[0] == 0) return Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n');
                stream.WriteByte(one[0]);
            }
        }
    }
    private static void NoPacket(Socket socket, string message)
    {
        Thread.Sleep(250);
        Check(socket.Available == 0, message + (socket.Available > 0 ? " (reçu : " + Read(socket) + ")" : ""));
    }
    private static void Expect(Socket peer, ChatResult result, string packet, string message)
    {
        Check(result.Accepted && result.Sent && result.Packet == packet, message + " (résultat : " + result.Packet + " / " + result.Message + ")");
        string read = Read(peer);
        Check(read == packet, message + " (lu : " + read + ")");
    }
    private static void Refused(Socket peer, ChatResult result, string message)
    {
        Check(!result.Accepted && !result.Sent && result.Message.Length > 0, message + " (résultat : " + result.Message + ")");
        NoPacket(peer, message);
    }

    private static void Run()
    {
        string previous = Environment.CurrentDirectory;
        string folder = Path.Combine(TestPaths.Work, "bot-chat"); Directory.CreateDirectory(folder); Environment.CurrentDirectory = folder;
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            MessagesReception.Init();
            foreach (string prefix in new[] { "cMK", "cME", "cC+", "cC-", "cS", "cs", "M1", "eUK", "eUE", "eL", "eA", "eR" })
                Check(MessagesReception.messagesDatas.Count(d => d.MessageName == prefix) == 1, "Prefix " + prefix + " has no single handler");

            // Not connected: nothing is sent, the refusal is explained.
            using (var offline = new Accounts(new AccountConfig("synthetic-offline", "synthetic", "loopback")))
            {
                ChatResult refused = Await(offline.Game.Chat.SendAsync('*', "bonjour"));
                Check(!refused.Accepted && refused.Message.Contains("Connectez"), "Chat sent without a connection");
            }

            using (var account = new Accounts(new AccountConfig("compte-chat", "secret-chat-42", "loopback")))
            {
                var debug = new ConcurrentQueue<string>();
                var chatLog = new ConcurrentQueue<KeyValuePair<string, string>>();
                account.Logger.debug_event += entry => debug.Enqueue(entry.message);
                account.Logger.log_eventChat += (entry, color) => chatLog.Enqueue(new KeyValuePair<string, string>(entry.message, color));
                Task<Socket> accept = listener.AcceptSocketAsync();
                Complete(account.Connexion.ConnectToServer(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port)); Complete(accept);
                using (Socket peer = accept.Result)
                {
                    peer.ReceiveTimeout = 6000;
                    account.Game.character.SetPerso_Data(7, "Testeur", 50, 0, 1);
                    ChatService chat = account.Game.Chat;
                    var received = new List<ChatMessage>();
                    chat.MessageReceived += message => { throw new InvalidOperationException("faulty chat subscriber"); };
                    chat.MessageReceived += received.Add;

                    // BM: channel, text, empty linked-items field; text prepared like Chat.send of the 1.34 client.
                    Expect(peer, Await(chat.SendAsync('*', "bonjour")), "BM*|bonjour|", "Send('*') is not BM*|bonjour|");
                    Expect(peer, Await(chat.SendAsync('%', "salut la guilde")), "BM%|salut la guilde|", "Guild message is not BM%|…|");
                    string longText = new string('x', 250);
                    Expect(peer, Await(chat.SendAsync('*', longText)), "BM*|" + new string('x', 199) + "|", "A 250-character message was not cut to 199 like the client");
                    Expect(peer, Await(chat.SendAsync('*', new string('y', 200))), "BM*|" + new string('y', 200) + "|", "A 200-character message was cut");
                    Expect(peer, Await(chat.SendAsync('*', "a\nb\r\0c")), "BM*|abc|", "Line breaks or NUL were not removed");
                    Expect(peer, Await(chat.SendAsync('*', "a<b>|c")), "BM*|a&lt;b&gt;c|", "< > were not escaped or | not removed like the client");
                    Expect(peer, Await(chat.SendAsync('!', "été")), "BM!|été|", "Alignment message or UTF-8 text changed");
                    Refused(peer, Await(chat.SendAsync('*', "\n|\0")), "An empty message was sent");
                    Refused(peer, Await(chat.SendAsync('¤', "méta")), "The ¤ channel (commented in StarLoco) was sent");
                    Refused(peer, Await(chat.SendAsync('F', "x")), "A receive-only channel was sent");
                    Refused(peer, Await(chat.SendAsync('*', "mon code secret-chat-42 !")), "A message with the password was sent");
                    Refused(peer, Await(chat.SendAsync('*', "compte compte-chat")), "A message with the login was sent");
                    Check(received.Any(m => m.Kind == ChatMessageKind.Error && m.Text.Contains("mot de passe")), "The password refusal was not shown in the chat");

                    // Whisper: BM<name>|<text>|, never to oneself.
                    Expect(peer, Await(chat.WhisperAsync("Ami", "coucou")), "BMAmi|coucou|", "Whisper is not BM<name>|<text>|");
                    Refused(peer, Await(chat.WhisperAsync("testeur", "moi")), "A whisper to oneself was sent");
                    Refused(peer, Await(chat.WhisperAsync("A", "court")), "A one-letter recipient was accepted");
                    Refused(peer, Await(chat.WhisperAsync("Nom|x", "barre")), "A recipient with | was accepted");
                    Refused(peer, Await(chat.WhisperAsync("*Nom", "canal")), "A recipient starting with a channel letter was accepted");

                    // cC±: one packet per letter (StarLoco reads charAt(3)); filters of the client.
                    ChatResult subscribe = Await(chat.SubscribeAsync("*$p"));
                    Check(subscribe.Packets.SequenceEqual(new[] { "cC+*", "cC+$", "cC+p" }), "Subscribe did not produce one cC+ per letter");
                    Check(Read(peer) == "cC+*" && Read(peer) == "cC+$" && Read(peer) == "cC+p", "cC+ letters were not sent separately");
                    ChatResult filter = Await(chat.SetFilterAsync(3, false));
                    Check(filter.Packets.SequenceEqual(new[] { "cC-#", "cC-$", "cC-p" }) && Read(peer) == "cC-#" && Read(peer) == "cC-$" && Read(peer) == "cC-p",
                        "Filter 3 (#$p) was not unsubscribed letter by letter");
                    Check(Await(chat.SetFilterAsync(1, false)).Accepted, "The local error filter was refused"); NoPacket(peer, "Filter 1 (errors) sent a packet");
                    Expect(peer, Await(chat.SetFilterAsync(8, true)), "cC+^", "Filter 8 is not cC+^");
                    Refused(peer, Await(chat.SubscribeAsync("*@")), "cC+@ (never sent by the client) was accepted");
                    Check(ChatChannels.FilterLetters(0) == "i" && ChatChannels.FilterLetters(5) == "!" && ChatChannels.FilterLetters(7) == ":", "Filter letters differ from the client");

                    // cC± echoes and the channel list sent when entering the game.
                    string subscriptions = null; chat.SubscriptionsChanged += value => subscriptions = value;
                    Feed(account, "cC+*#$pi:?!%^@");
                    Check(chat.SubscribedChannels == "*#$pi:?!%^@" && subscriptions == chat.SubscribedChannels && account.Game.character.Canal == chat.SubscribedChannels,
                        "The entry cC+ list was not kept");
                    Feed(account, "cC-*"); Check(!chat.IsSubscribed('*') && chat.IsSubscribed('#'), "cC-* echo was not applied");
                    Feed(account, "cC+*"); Feed(account, "cC+*"); Check(chat.SubscribedChannels.Count(c => c == '*') == 1, "A repeated cC+ duplicated the letter");
                    Feed(account, "cC"); Feed(account, "cCx*"); Check(chat.SubscribedChannels.Count(c => c == '*') == 1, "Malformed cC changed the channels");

                    // cMK: 4 fields (StarLoco), 5 fields (trailing | kept by StarLoco), unknown channel = default.
                    received.Clear();
                    Feed(account, "cMK|7|Nom|salut");
                    Feed(account, "cMK|7|Nom|salut|");
                    Feed(account, "cMK*|8|Autre|bonjour|");
                    Feed(account, "cMK$|9|Membre|groupe||");
                    Feed(account, "cMK%|-10|Guildeux|guilde");
                    Check(received.Count == 5 && received.All(m => m.Kind == ChatMessageKind.Channel), "cMK channel messages were not all read");
                    Check(received[0].Channel == ChatChannels.Default && received[0].AuthorId == 7 && received[0].Author == "Nom" && received[0].Text == "salut" && received[0].Items == null,
                        "cMK with 4 fields was not read");
                    Check(received[1].Text == "salut" && received[1].Items == null && received[2].Channel == ChatChannels.Default && received[2].Author == "Autre",
                        "cMK with 5 fields or channel * was not read");
                    Check(received[3].Channel == ChatChannels.Party && received[3].Text == "groupe" && received[3].Color == "006699" && received[3].Filter == ChatFilter.Whispers,
                        "Party cMK$ (extra trailing |) was not read with the client colour");
                    Check(received[4].Channel == ChatChannels.Guild && received[4].AuthorId == -10 && received[4].Color == "663399" && received[4].ToString() == "(Guilde) Guildeux : guilde",
                        "Guild cMK% was not read");
                    Check(chatLog.Any(entry => entry.Key.Contains("(Groupe) Membre : groupe") && entry.Value == "006699"), "The chat journal did not get the 6-digit client colour");
                    Feed(account, "cMK|7|Nom|*danse*"); Check(received.Last().Style == ChatMessageStyle.Emote && received.Last().Text == "danse" && received.Last().Color == "222222", "*emote* style was not read");
                    Feed(account, "cMK|7|Nom|!THINK!hmm"); Check(received.Last().Style == ChatMessageStyle.Think && received.Last().Text == "hmm", "!THINK! style was not read");
                    Feed(account, "cMK|7|Nom|a &lt;b&gt;"); Check(received.Last().Text == "a <b>" && received.Last().RawText == "a &lt;b&gt;", "HTML entities were not decoded for display");

                    // cMKF / cMKT / cME.
                    string whisperedBy = null; account.Game.character.ChatPrivate += who => whisperedBy = who;
                    Feed(account, "cMKF|11|Ami|psst");
                    Check(received.Last().Kind == ChatMessageKind.WhisperReceived && received.Last().Author == "Ami" && received.Last().Color == "0066FF" && whisperedBy == "Ami"
                        && chat.WhisperHistory.Items.Last() == "/w Ami ", "cMKF was not read as a received whisper");
                    Feed(account, "cMKT|11|Ami|psst");
                    Check(received.Last().Kind == ChatMessageKind.WhisperSent && received.Last().ToString() == "À Ami : psst", "cMKT was not read as a sent whisper");
                    Feed(account, "cMEfAbsent"); Check(received.Last().Kind == ChatMessageKind.Error && received.Last().Text.Contains("Absent"), "cMEf<name> was not shown");
                    int count = received.Count;
                    foreach (string bad in new[] { "cMK", "cMK|7|Nom", "cMK|", "cME", "cMEz" }) Feed(account, bad);
                    Check(received.Count == count && debug.Any(m => m.Contains("cMK|7|Nom")), "Malformed cMK/cME produced lines or was not logged");
                    for (int i = 0; i < ChatService.MaxMessages + 10; i++) Feed(account, "cMK|7|Nom|ligne " + i);
                    Check(chat.Messages.Count == ChatService.MaxMessages && chat.Messages.Last().Text == "ligne " + (ChatService.MaxMessages + 9), "More than 150 lines were kept");

                    // cS / BS: the client ignores a second smiley within CLICK_MIN_DELAY (800 ms).
                    var smileys = new List<string>(); chat.SmileyReceived += (actor, id) => smileys.Add(actor + ":" + id);
                    Feed(account, "cS7|3"); Feed(account, "cS-12|15"); Feed(account, "cSx|y"); Feed(account, "cS7");
                    Check(smileys.SequenceEqual(new[] { "7:3", "-12:15" }), "cS<actor>|<smiley> was not read");
                    Expect(peer, Await(chat.SmileyAsync(3)), "BS3", "Smiley 3 is not BS3");
                    Refused(peer, Await(chat.SmileyAsync(4)), "A smiley within 800 ms was sent");
                    Thread.Sleep(ChatService.ClickMinDelayMilliseconds + 100);
                    Expect(peer, Await(chat.SmileyAsync(4)), "BS4", "A smiley after 800 ms was not sent");
                    Refused(peer, Await(chat.SmileyAsync(0)), "Smiley 0 was sent");

                    // eU / eUK / eL / eA / eR.
                    var emotes = new List<string>(); chat.EmoteReceived += (actor, id) => emotes.Add(actor + ":" + id);
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Feed(account, "eUK7|1");
                    Check(emotes.Last() == "7:1" && chat.SelfEmote == 1 && chat.IsSelfSitting && account.AccountStates == AccountStates.REGENERATION, "eUK on self (sit) was not read");
                    Feed(account, "eUK7|0"); Check(!chat.IsSelfSitting && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "eUK<self>|0 did not stand up");
                    Feed(account, "eUK8|19"); Check(emotes.Last() == "8:19" && account.AccountStates == AccountStates.CONNECTED_INACTIVE, "Another actor's emote changed the account state");
                    Feed(account, "eUK7|20|5000"); Check(emotes.Last() == "7:20" && account.AccountStates == AccountStates.REGENERATION, "eUK with a timer field (client format) was refused");
                    Feed(account, "eUK7|0");
                    count = emotes.Count; Feed(account, "eUKabc|1"); Feed(account, "eUK7"); Check(emotes.Count == count, "Malformed eUK raised an emote");
                    Expect(peer, Await(chat.EmoteAsync(3)), "eU3", "Emote 3 is not eU3");
                    Refused(peer, Await(chat.EmoteAsync(5)), "An emote within 800 ms was sent");
                    Thread.Sleep(ChatService.ClickMinDelayMilliseconds + 100);
                    account.AccountStates = AccountStates.FIGHTING;
                    Refused(peer, Await(chat.EmoteAsync(5)), "An emote was sent in a fight");
                    account.AccountStates = AccountStates.CONNECTED_INACTIVE;
                    Expect(peer, Await(chat.SitAsync()), "eU1", "The sit button is not eU1");
                    int emoteChanges = 0; chat.EmotesChanged += () => emoteChanges++;
                    Feed(account, "eL6|0"); Check(chat.AvailableEmotes.SequenceEqual(new[] { 2, 3 }) && emoteChanges == 1, "eL<mask> did not give emotes 2 and 3");
                    Feed(account, "eL0|8"); Check(chat.AvailableEmotes.SequenceEqual(new[] { 4 }), "The second eL mask was ignored");
                    ChatTexts.EmoteName = id => id == 2 ? "Salut" : null;
                    try
                    {
                        Feed(account, "eL6|0"); Check(chat.AvailableEmotes.SequenceEqual(new[] { 2 }), "An emote unknown to the language files was kept");
                        Feed(account, "eA2"); Check(received.Last().Kind == ChatMessageKind.Info && received.Last().Text.Contains("Salut"), "eA was not announced with the emote name");
                    }
                    finally { ChatTexts.EmoteName = null; }
                    count = received.Count;
                    Feed(account, "eA9|0"); Check(chat.AvailableEmotes.Contains(9) && received.Count == count, "eA<id>|0 was announced or not added");
                    Feed(account, "eR9"); Check(!chat.AvailableEmotes.Contains(9) && received.Count == count + 1, "eR was not applied");
                    Feed(account, "eLabc"); Feed(account, "eAx"); Check(chat.AvailableEmotes.SequenceEqual(new[] { 2 }), "Malformed eL/eA changed the list");
                    Feed(account, "eUE"); Check(received.Last().Kind == ChatMessageKind.Error, "eUE was not shown");

                    // cs and M1.
                    Feed(account, "cs<font color='#B9121B'>Bienvenue &amp; bon jeu</font>");
                    Check(received.Last().Kind == ChatMessageKind.Server && received.Last().Text == "Bienvenue & bon jeu" && received.Last().Color == "B9121B", "cs was not read");
                    var popups = new List<ChatServerPopup>(); chat.ServerPopupReceived += popups.Add;
                    Feed(account, "M10"); Check(popups.Count == 1 && popups[0].IsFlood && popups[0].Args.Count == 0 && received.Last().Kind == ChatMessageKind.Info, "M10 (flood) was not read");
                    Feed(account, "M110|500"); Check(popups.Last().Id == 10 && popups.Last().Args.SequenceEqual(new[] { "500" }) && !popups.Last().Resolved, "M110|500 was not read");
                    ChatTexts.ServerPopup = (id, args) => id == 10 ? "Gain de " + args[0] + " kamas" : null;
                    try { Feed(account, "M110|500"); Check(popups.Last().Resolved && popups.Last().Text == "Gain de 500 kamas", "The M1 language resolver was not used"); }
                    finally { ChatTexts.ServerPopup = null; }
                    count = popups.Count; Feed(account, "M1x"); Check(popups.Count == count, "Malformed M1 raised a popup");

                    // Console of the client.
                    Expect(peer, Await(chat.ExecuteAsync("salut à tous")), "BM*|salut à tous|", "A plain line is not sent to the default channel");
                    Expect(peer, Await(chat.ExecuteAsync("/w Nom coucou toi")), "BMNom|coucou toi|", "/w Nom coucou toi is not BMNom|coucou toi|");
                    Check(chat.WhisperHistory.Items.Last() == "/w Nom ", "/w did not fill the whisper history");
                    Expect(peer, Await(chat.ExecuteAsync("/g bonjour")), "BM%|bonjour|", "/g is not the guild channel");
                    Expect(peer, Await(chat.ExecuteAsync("/T  en équipe")), "BM#|en équipe|", "/t (upper case, extra space) is not the team channel");
                    Expect(peer, Await(chat.ExecuteAsync("/s général")), "BM*|général|", "/s is not the default channel");
                    Expect(peer, Await(chat.ExecuteAsync("/a pvp")), "BM!|pvp|", "/a is not the alignment channel");
                    Expect(peer, Await(chat.ExecuteAsync("/r recrute")), "BM?|recrute|", "/r is not the recruitment channel");
                    Expect(peer, Await(chat.ExecuteAsync("/b vends")), "BM:|vends|", "/b is not the trade channel");
                    Expect(peer, Await(chat.ExecuteAsync("/i débutant")), "BM^|débutant|", "/i is not the Incarnam channel");
                    Expect(peer, Await(chat.ExecuteAsync("/q admin")), "BM@|admin|", "/q is not the admin channel");
                    Refused(peer, Await(chat.ExecuteAsync("/p groupe")), "/p was sent without a party");
                    account.Game.character.InGroupe = true;
                    Expect(peer, Await(chat.ExecuteAsync("/p groupe")), "BM$|groupe|", "/p is not the party channel");
                    account.Game.character.InGroupe = false;
                    Refused(peer, Await(chat.ExecuteAsync("/m méta")), "/m (¤) was sent");
                    Refused(peer, Await(chat.ExecuteAsync("/s")), "An empty /s was sent");
                    Refused(peer, Await(chat.ExecuteAsync("/w Nom")), "/w without a message was sent");
                    Refused(peer, Await(chat.ExecuteAsync("/away")), "/away sent BYA");
                    Refused(peer, Await(chat.ExecuteAsync("/invisible")), "/invisible sent BYI");
                    Expect(peer, Await(chat.ExecuteAsync("/whois Nom")), "BWNom", "/whois is not BW<name>");
                    Expect(peer, Await(chat.ExecuteAsync("/whoami")), "BW", "/whoami is not BW");
                    Refused(peer, Await(chat.ExecuteAsync("/whois")), "/whois without a name was sent");
                    Expect(peer, Await(chat.ExecuteAsync("/f A Ami")), "FAAmi", "/f A is not FA<name>");
                    Expect(peer, Await(chat.ExecuteAsync("/friend - Ami")), "FDAmi", "/friend - is not FD<name>");
                    Expect(peer, Await(chat.ExecuteAsync("/f l")), "FL", "/f L is not FL");
                    Refused(peer, Await(chat.ExecuteAsync("/f A *")), "/f A * was sent");
                    Expect(peer, Await(chat.ExecuteAsync("/ignore + Ennemi")), "iAEnnemi", "/ignore + is not iA<name>");
                    Expect(peer, Await(chat.ExecuteAsync("/enemy D Ennemi")), "iDEnnemi", "/enemy D is not iD<name>");
                    Expect(peer, Await(chat.ExecuteAsync("/ignore L")), "iL", "/ignore L is not iL");
                    Expect(peer, Await(chat.ExecuteAsync("/invite Ami")), "PIAmi", "/invite is not PI<name>");
                    Expect(peer, Await(chat.ExecuteAsync("/me danse")), "BM*|*danse*|", "/me is not *text* in the default channel");
                    Expect(peer, Await(chat.ExecuteAsync("/think hmm hmm")), "BM*|!THINK!hmm hmm|", "/think is not !THINK! in the default channel");
                    Refused(peer, Await(chat.ExecuteAsync("/me")), "/me without text was sent");
                    Refused(peer, Await(chat.ExecuteAsync("/kick Ami")), "/kick was sent outside a fight placement");
                    Refused(peer, Await(chat.ExecuteAsync("/list")), "/list worked outside a fight");
                    Refused(peer, Await(chat.ExecuteAsync("/spec")), "/spec (fS) was sent outside a fight");
                    Refused(peer, Await(chat.ExecuteAsync("/logout")), "A client-only command was accepted");
                    Refused(peer, Await(chat.ExecuteAsync("/zzz")), "An unknown command was accepted");
                    Check(received.Last().Kind == ChatMessageKind.Error && received.Last().Text.Contains("/zzz"), "The unknown command was not reported");
                    Expect(peer, Await(chat.ExecuteAsync("/sit")), "eU1", "/sit is not eU1");
                    Thread.Sleep(ChatService.ClickMinDelayMilliseconds + 100);
                    ChatTexts.EmoteShortcut = shortcut => shortcut == "bye" ? 2 : (int?)null;
                    try { Expect(peer, Await(chat.ExecuteAsync("/bye")), "eU2", "An emote shortcut from the language files was not used"); }
                    finally { ChatTexts.EmoteShortcut = null; }
                    Expect(peer, Await(chat.ExecuteAsync(".infos")), "BM*|.infos|", "A StarLoco player command (.infos) was not sent as is");

                    // Local commands: output in the chat, no packet.
                    account.Game.Map.MapID = 7411;
                    ChatResult local = Await(chat.ExecuteAsync("/mapid"));
                    Check(local.Accepted && !local.Sent && local.Message.Contains("7411") && received.Last().Kind == ChatMessageKind.Command && received.Last().Color == "E4287C",
                        "/mapid was not answered locally");
                    foreach (string command in new[] { "/cellid", "/time", "/help", "/version", "/aping" })
                        Check(Await(chat.ExecuteAsync(command)).Accepted, command + " was refused");
                    Check(received.Any(m => m.Text.Contains("/w <nom> <message>")), "/help does not list the commands");
                    NoPacket(peer, "A local command sent a packet");
                    Expect(peer, Await(chat.ExecuteAsync("/ping")), "ping", "/ping is not ping");
                    int cleared = 0; chat.Cleared += () => cleared++;
                    Check(Await(chat.ExecuteAsync("/cls")).Accepted && chat.Messages.Count == 0 && cleared == 1, "/cls did not clear the chat");

                    // Restrictions: bit 16 of AR forbids the default channel for plain lines, /me and /think (canChatToAll).
                    Feed(account, "ARg");
                    Check(!chat.CanChatToAll, "AR bit 16 did not forbid the default channel");
                    Refused(peer, Await(chat.ExecuteAsync("interdit")), "A plain line was sent despite canChatToAll");
                    Refused(peer, Await(chat.ExecuteAsync("/me interdit")), "/me was sent despite canChatToAll");
                    Expect(peer, Await(chat.ExecuteAsync("/s permis")), "BM*|permis|", "/s is not checked by the client and must still be sent");
                    Feed(account, "AR6bk"); Check(chat.CanChatToAll, "AR6bk still forbids the default channel");

                    // Input history (50, no consecutive duplicate, Up/Down like the client).
                    ChatHistory history = chat.InputHistory;
                    Check(history.Items.Last() == "/s permis" && history.Items.Count <= ChatService.HistorySize, "The input history was not filled");
                    Check(history.Up() == "/s permis" && history.Up() == "/me interdit" && history.Down() == "/s permis" && history.Down() == string.Empty,
                        "History Up/Down does not follow the client");
                    for (int i = 0; i < 60; i++) history.Push("ligne " + i);
                    history.Push("ligne 59");
                    Check(history.Items.Count == ChatService.HistorySize && history.Items.First() == "ligne 10", "The history kept more than 50 lines or a duplicate");

                    // Fight: /list and /kick read the fighters during placement; a spectator's default channel becomes the team channel.
                    Fights fight = account.Game.Fight;
                    Feed(account, "GJK2|1|1|0|30000|0");
                    Feed(account, "GM|+3;1;0;77;Ami;1;10^100;0");
                    Check(fight.Phase == CombatPhase.Placement && fight.Fighters.ContainsKey(77), "The synthetic placement was not set up");
                    Check(Await(chat.ExecuteAsync("/list")).Accepted && received.Last().Kind == ChatMessageKind.Command && received.Last().Text.Contains("- Ami"),
                        "/list does not name the players of the fight");
                    Expect(peer, Await(chat.ExecuteAsync("/kick Ami")), "GQ77", "/kick is not GQ<id> during placement");
                    Refused(peer, Await(chat.ExecuteAsync("/kick Inconnu")), "/kick accepted a name absent from the fight");
                    Refused(peer, Await(chat.ExecuteAsync("/spec")), "/spec (fS) was sent during placement");
                    Refused(peer, Await(chat.EmoteAsync(3)), "An emote was sent during a fight");
                    Feed(account, "GJK3|0|0|1|0|4");
                    Check(fight.IsSpectator, "The synthetic spectator fight was not set up");
                    Expect(peer, Await(chat.ExecuteAsync("bravo")), "BM#|bravo|", "A spectator's default channel did not become the team channel");
                    Refused(peer, Await(chat.ExecuteAsync("/spec")), "/spec (fS) was sent by a spectator");

                    Check(debug.All(m => !m.Contains("faulty")), "A faulty subscriber leaked into the debug log");
                    NoPacket(peer, "Unexpected packet at the end of the chat test");
                }
            }
        }
        finally { listener.Stop(); Environment.CurrentDirectory = previous; }
    }
}
