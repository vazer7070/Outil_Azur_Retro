using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Combats;

namespace Tool_BotProtocol.Game.Chat
{
    /// <summary>Contexte d'une commande de console : compte, nom en majuscules et arguments découpés comme le client.</summary>
    public sealed class ChatCommandContext
    {
        internal ChatCommandContext(ChatService chat, string name, string[] args)
        {
            Chat = chat;
            Name = name;
            Args = Array.AsReadOnly(args);
        }

        public ChatService Chat { get; }
        public Accounts.Accounts Account => Chat.Account;
        /// <summary>Nom de la commande en majuscules, sans « / ».</summary>
        public string Name { get; }
        /// <summary>Mots qui suivent la commande (séparés par une espace, mots vides de tête retirés).</summary>
        public IReadOnlyList<string> Args { get; }
        /// <summary>Arguments recollés avec une espace, comme <c>r5.join(" ")</c> dans le client.</summary>
        public string Text => string.Join(" ", Args);

        internal Task<ChatResult> Output(string text)
        {
            Chat.AddLocal(ChatMessageKind.Command, text);
            return Task.FromResult(new ChatResult(true, text));
        }

        internal Task<ChatResult> Error(string text) => Task.FromResult(Chat.Refuse(text, true));
        internal Task<ChatResult> Syntax(string usage) => Error("Syntaxe : " + usage);
        internal Task<ChatResult> Send(string packet) => Chat.SendPacketsAsync(null, packet);
    }

    /// <summary>Une commande de console : noms acceptés, syntaxe, aide et action.</summary>
    public sealed class ChatCommand
    {
        internal ChatCommand(string[] names, string usage, string help, Func<ChatCommandContext, Task<ChatResult>> run)
        {
            Names = Array.AsReadOnly(names);
            Usage = usage;
            Help = help;
            Run = run;
        }

        /// <summary>Noms en majuscules, le premier étant le nom principal.</summary>
        public IReadOnlyList<string> Names { get; }
        public string Usage { get; }
        public string Help { get; }
        internal Func<ChatCommandContext, Task<ChatResult>> Run { get; }
    }

    /// <summary>
    /// Commandes de la console du client 1.34 (<c>Console.process</c>) : changement de canal (<c>/s /t /g /p /a /r /b /i /q</c>),
    /// chuchotement, <c>/whois</c>, amis et ennemis, invitation, émotes, commandes locales. Les commandes du serveur StarLoco
    /// (préfixe « . ») s'écrivent dans le canal général et partent telles quelles (aide : <see cref="ServerCommands"/>).
    /// <c>/away</c> et <c>/invisible</c> envoient <c>BYA</c>/<c>BYI</c> comme le client, seulement quand l'utilisateur les tape
    /// (<see cref="PlayerPresence"/>) : jamais à l'entrée en jeu (matrice §2 n° 35).
    /// </summary>
    public static class ChatCommands
    {
        private static readonly List<ChatCommand> commands = new List<ChatCommand>();
        private static readonly Dictionary<string, ChatCommand> byName = new Dictionary<string, ChatCommand>(StringComparer.Ordinal);

        /// <summary>Commandes que le client réserve à son interface (déconnexion, base de connaissances…) : non reprises par le bot.</summary>
        private static readonly string[] ClientOnly =
            { "CONSOLE", "DEBUG", "CHANGECHARACTER", "LOGOUT", "QUIT", "KB", "RELEASE", "SELECTION", "WTF", "DOFUS2", "TACTIC",
              "FILEOUTPUT", "SPEAKINGITEM", "GOD", "GODMODE" };

        static ChatCommands()
        {
            Add(new[] { "HELP", "H", "?" }, "/help", "liste des commandes", context => context.Output(HelpText()));
            Add(new[] { "VERSION", "VER", "ABOUT" }, "/version", "version du bot", context => context.Output(VersionText()));
            AddChannel(ChatChannels.Default, new[] { "S" });
            AddChannel(ChatChannels.Team, new[] { "T" });
            AddChannel(ChatChannels.Guild, new[] { "G" });
            AddChannel(ChatChannels.Party, new[] { "P" });
            AddChannel(ChatChannels.Alignment, new[] { "A" });
            AddChannel(ChatChannels.Recruitment, new[] { "R" });
            AddChannel(ChatChannels.Trade, new[] { "B" });
            AddChannel(ChatChannels.Incarnam, new[] { "I" });
            AddChannel(ChatChannels.Admin, new[] { "Q" });
            Add(new[] { "M" }, "/m <message>", "canal « ¤ » du client, ignoré par StarLoco",
                context => context.Error("Le canal « ¤ » (/m) n'est pas traité par StarLoco : message non envoyé."));
            Add(new[] { "W", "MSG", "WHISPER" }, "/w <nom> <message>", "chuchoter à un joueur", Whisper);
            Add(new[] { "WHOAMI" }, "/whoami", "informations sur son compte (BW)", context => context.Send("BW"));
            Add(new[] { "WHOIS" }, "/whois <nom>", "informations sur un joueur (BW<nom>)",
                context => context.Args.Count == 0 ? context.Syntax("/whois <nom>") : context.Send("BW" + context.Args[0]));
            Add(new[] { "F", "FRIEND", "FRIENDS" }, "/f <A|D|L> [nom]", "ajouter (FA), retirer (FD) ou lister (FL) les amis",
                context => People(context, "/f <A/D/L> <nom>", "FA", "FD", "FL"));
            Add(new[] { "IGNORE", "ENEMY" }, "/ignore <A|D|L> [nom]", "ajouter (iA), retirer (iD) ou lister (iL) les ennemis",
                context => People(context, "/ignore <A/D/L> <nom>", "iA", "iD", "iL"));
            Add(new[] { "INVITE" }, "/invite <nom>", "inviter dans son groupe (PI<nom>)",
                context => context.Args.Count == 0 || context.Args[0].Length == 0 ? context.Syntax("/invite <nom>") : context.Send("PI" + context.Args[0]));
            Add(new[] { "PING" }, "/ping", "mesurer le temps de réponse (ping)", context => context.Send("ping"));
            Add(new[] { "APING" }, "/aping", "temps de réponse moyen", AveragePing);
            Add(new[] { "MAPID" }, "/mapid", "identifiant de la carte", MapId);
            Add(new[] { "CELLID" }, "/cellid", "cellule du personnage",
                context => context.Output("Cellule : " + (context.Account?.Game?.character?.Cell?.CellID.ToString(CultureInfo.InvariantCulture) ?? "inconnue")));
            Add(new[] { "TIME" }, "/time", "heure du serveur (BT)", ServerTime);
            Add(new[] { "LIST", "PLAYERS" }, "/list", "joueurs du combat", ListPlayers);
            Add(new[] { "KICK" }, "/kick <nom>", "exclure un joueur de son équipe pendant le placement (GQ<id>)", Kick);
            Add(new[] { "SPECTATOR", "SPEC" }, "/spec", "bloquer les spectateurs du combat (fS)", Spectators);
            // Lot F14 : bascules explicites de l'utilisateur, jamais envoyées seules (matrice §2 n° 35).
            Add(new[] { "AWAY" }, "/away", "basculer l'état absent (BYA) : StarLoco refuse alors tous les messages privés",
                context => Presence(context)?.ToggleAwayAsync() ?? context.Error("/away : session de jeu indisponible."));
            Add(new[] { "INVISIBLE" }, "/invisible", "basculer l'état invisible (BYI) : seuls les amis peuvent alors chuchoter",
                context => Presence(context)?.ToggleInvisibleAsync() ?? context.Error("/invisible : session de jeu indisponible."));
            Add(new[] { "THINK", "METHINK", "PENSE", "TH" }, "/think <texte>", "bulle de pensée dans le canal général",
                context => Styled(context, "!THINK!" + context.Text, "/" + context.Name.ToLowerInvariant() + " <texte>"));
            Add(new[] { "ME", "EM", "MOI", "EMOTE" }, "/me <texte>", "action en italique dans le canal général",
                context => Styled(context, "*" + context.Text + "*", "/" + context.Name.ToLowerInvariant() + " <texte>"));
            Add(new[] { "CLS", "CLEAR" }, "/cls", "effacer le chat", context =>
            {
                context.Chat.ClearMessages();
                return Task.FromResult(new ChatResult(true, "Chat effacé."));
            });
        }

        /// <summary>Commandes connues, dans l'ordre de l'aide.</summary>
        public static IReadOnlyList<ChatCommand> All => commands.AsReadOnly();

        public static bool TryGet(string name, out ChatCommand command)
        {
            command = null;
            if (string.IsNullOrEmpty(name)) return false;
            string key = (name[0] == '/' ? name.Substring(1) : name).ToUpperInvariant();
            return byName.TryGetValue(key, out command);
        }

        /// <summary>
        /// Traite une ligne commençant par « / » comme <c>Console.process</c> : découpe sur les espaces, commande en majuscules,
        /// mots vides de tête retirés. Une commande inconnue est cherchée comme raccourci d'émote (<c>/sit</c>…) avant d'être refusée.
        /// </summary>
        internal static Task<ChatResult> ExecuteAsync(ChatService chat, string line)
        {
            List<string> words = line.Split(' ').ToList();
            string name = words[0].Substring(1).ToUpperInvariant();
            words.RemoveAt(0);
            while (words.Count > 0 && words[0].Length == 0) words.RemoveAt(0);
            var context = new ChatCommandContext(chat, name, words.ToArray());
            ChatCommand command;
            if (byName.TryGetValue(name, out command)) return command.Run(context);
            if (ClientOnly.Contains(name)) return context.Error("/" + name.ToLowerInvariant() + " : commande de l'interface du client, non reprise par le bot.");
            int? emote = ChatTexts.ResolveEmoteShortcut(name.ToLowerInvariant());
            if (emote.HasValue) return chat.EmoteAsync(emote.Value);
            return context.Error("Commande inconnue : /" + name.ToLowerInvariant() + " (voir /help).");
        }

        private static void Add(string[] names, string usage, string help, Func<ChatCommandContext, Task<ChatResult>> run)
        {
            var command = new ChatCommand(names, usage, help, run);
            commands.Add(command);
            foreach (string name in names) byName.Add(name, command);
        }

        private static void AddChannel(ChatChannel channel, string[] names)
        {
            string usage = channel.Command + " <message>";
            Add(names, usage, "écrire dans le canal " + channel.Label + " (BM" + channel.Code + "|…|)", context =>
            {
                // Comme le client, /p n'est envoyé que par un membre de groupe ; /g est laissé à StarLoco, la guilde (gS) n'étant pas encore lue.
                if (channel == ChatChannels.Party && !(context.Account?.Game?.character?.InGroupe ?? false))
                    return context.Error("Le personnage n'appartient à aucun groupe : message non envoyé.");
                if (context.Text.Length == 0) return Task.FromResult(context.Chat.Refuse("Message vide : rien n'est envoyé.", false));
                return context.Chat.SendAsync(channel.Code, context.Text);
            });
        }

        private static PlayerPresence Presence(ChatCommandContext context) => context.Account?.Game?.Presence;

        private static Task<ChatResult> Whisper(ChatCommandContext context)
        {
            const string usage = "/w <nom> <message>";
            if (context.Args.Count < 2 || context.Args[0].Length < 2) return context.Syntax(usage);
            string name = context.Args[0];
            string text = string.Join(" ", context.Args.Skip(1));
            context.Chat.WhisperHistory.Push("/w " + name + " ");
            return context.Chat.WhisperAsync(name, text);
        }

        private static Task<ChatResult> People(ChatCommandContext context, string usage, string add, string remove, string list)
        {
            if (context.Args.Count == 0) return context.Syntax(usage);
            string action = context.Args[0].ToUpperInvariant();
            if (action == "L") return context.Send(list);
            string prefix = action == "A" || action == "+" ? add : action == "D" || action == "R" || action == "-" ? remove : null;
            if (prefix == null) return context.Syntax(usage);
            string name = context.Args.Count > 1 ? context.Args[1] : string.Empty;
            // Friends.addFriend / Enemies.addEnemy refusent un nom vide et « * ».
            if (name.Length == 0 || name == "*") return context.Syntax(usage);
            return context.Send(prefix + name);
        }

        private static Task<ChatResult> Styled(ChatCommandContext context, string text, string usage)
        {
            if (context.Args.Count < 1) return context.Syntax(usage);
            if (!context.Chat.CanChatToAll) return context.Error("Les restrictions du personnage interdisent le canal général.");
            return context.Chat.SendAsync('*', text);
        }

        private static Task<ChatResult> AveragePing(ChatCommandContext context)
        {
            var connection = context.Account?.Connexion;
            int count = connection?.GetTotalPings() ?? 0;
            return context.Output(count == 0 ? "Aucune mesure de ping (envoyez /ping)." :
                "Ping moyen : " + connection.GetPingAverage() + " ms (sur " + count + " mesures).");
        }

        private static Task<ChatResult> MapId(ChatCommandContext context)
        {
            var map = context.Account?.Game?.Map;
            if (map == null || map.MapID == 0) return context.Output("Carte : inconnue");
            return context.Output("Carte : " + map.MapID.ToString(CultureInfo.InvariantCulture) + " " + map.GetCoordinates);
        }

        private static Task<ChatResult> ServerTime(ChatCommandContext context)
        {
            DateTime? time = context.Account?.Game?.Session?.EstimatedServerTime;
            // BT de StarLoco = heure Unix en ms décalée de son fuseau : affichée telle que reçue, sans conversion.
            return context.Output(time.HasValue
                ? "Heure du serveur : " + time.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                : "Heure du serveur inconnue : aucun BT reçu.");
        }

        private static IEnumerable<CombatFighter> Players(ChatCommandContext context)
        {
            var fight = context.Account?.Game?.Fight;
            if (fight == null) return Enumerable.Empty<CombatFighter>();
            // Les joueurs portent leur classe (> 0) dans le champ type de GM ; monstres, percepteurs et prismes sont négatifs.
            return fight.Fighters.Values.Where(fighter => fighter.Type > 0);
        }

        private static Task<ChatResult> ListPlayers(ChatCommandContext context)
        {
            if (!(context.Account?.Game?.Fight?.IsInFight ?? false)) return context.Error("/list n'est utilisable qu'en combat.");
            string[] names = Players(context).Select(fighter => "- " + fighter.Name).ToArray();
            return context.Output("Joueurs du combat :" + (names.Length == 0 ? " aucun" : Environment.NewLine + string.Join(Environment.NewLine, names)));
        }

        private static Task<ChatResult> Kick(ChatCommandContext context)
        {
            var fight = context.Account?.Game?.Fight;
            if (fight == null || !fight.IsInFight || fight.Phase != CombatPhase.Placement)
                return context.Error("/kick n'est utilisable que pendant le placement d'un combat.");
            string name = context.Args.Count > 0 ? context.Args[0] : string.Empty;
            CombatFighter target = Players(context).FirstOrDefault(fighter => fighter.Name == name);
            if (target == null) return context.Error("Impossible d'exclure " + (name.Length == 0 ? "ce joueur" : name) + " : joueur absent du combat.");
            return context.Send("GQ" + target.Id.ToString(CultureInfo.InvariantCulture));
        }

        private static Task<ChatResult> Spectators(ChatCommandContext context)
        {
            var fight = context.Account?.Game?.Fight;
            if (fight == null || fight.Phase != CombatPhase.Active || fight.IsSpectator)
                return context.Error("/spec n'est utilisable que dans un combat commencé, hors mode spectateur.");
            return context.Send("fS");
        }

        private static string HelpText()
        {
            var lines = new List<string> { "Commandes du chat :" };
            lines.AddRange(commands.Select(command => command.Usage + " — " + command.Help));
            lines.Add("Une ligne sans « / » part dans le canal général ; « .commande » est transmise au serveur.");
            return string.Join(Environment.NewLine, lines);
        }

        private static string VersionText()
        {
            Version version = typeof(ChatCommands).Assembly.GetName().Version;
            return "Bot Azur " + version + " — protocole du client Dofus Retro 1.34, serveur StarLoco.";
        }
    }
}
