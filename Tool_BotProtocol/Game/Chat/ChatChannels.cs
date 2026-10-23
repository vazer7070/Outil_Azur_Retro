using System;
using System.Collections.Generic;
using System.Linq;

namespace Tool_BotProtocol.Game.Chat
{
    /// <summary>
    /// Familles de messages du chat du client 1.34 (<c>ChatManager.TYPE_*</c>) : elles servent aux filtres d'affichage
    /// (<c>_btnFilter0..8</c>) et à la couleur des lignes.
    /// </summary>
    public enum ChatFilter
    {
        Infos = 0,
        Errors = 1,
        Messages = 2,
        /// <summary>Chuchotements, équipe et groupe (<c>WHISP_CHAT</c>, <c>PARTY_CHAT</c>).</summary>
        Whispers = 3,
        Guild = 4,
        /// <summary>Alignement et conquête (<c>PVP_CHAT</c>).</summary>
        Alignment = 5,
        Recruitment = 6,
        Trade = 7,
        /// <summary>Canal des débutants d'Incarnam (<c>MEETIC_CHAT</c>).</summary>
        Incarnam = 8,
        /// <summary>Canal admin et sortie des commandes (<c>ADMIN_CHAT</c>, <c>COMMANDS_CHAT</c>).</summary>
        Admin = 9,
    }

    /// <summary>Un canal du chat : lettre du protocole, commande de la console, couleur et règles de StarLoco.</summary>
    public sealed class ChatChannel
    {
        internal ChatChannel(char code, string label, string command, string color, ChatFilter filter, int cooldownMilliseconds,
            bool canSend, string serverRule)
        {
            Code = code;
            Label = label;
            Command = command;
            Color = color;
            Filter = filter;
            CooldownMilliseconds = cooldownMilliseconds;
            CanSend = canSend;
            ServerRule = serverRule;
        }

        /// <summary>Lettre envoyée dans <c>BM&lt;lettre&gt;|…</c> et reçue dans <c>cMK&lt;lettre&gt;|…</c> (<c>*</c> = canal par défaut, vide dans <c>cMK</c>).</summary>
        public char Code { get; }
        public string Label { get; }
        /// <summary>Commande de la console du client (<c>/s</c>, <c>/t</c>…), vide pour un canal de réception seule.</summary>
        public string Command { get; }
        /// <summary>Couleur du client (<c>dofus.Constants.*_CHAT_COLOR</c>) en hexadécimal, sans « # ».</summary>
        public string Color { get; }
        public ChatFilter Filter { get; }
        /// <summary>Délai imposé par StarLoco entre deux messages d'un joueur sans droits (0 = aucun) ; le client ne bloque rien localement.</summary>
        public int CooldownMilliseconds { get; }
        /// <summary>Faux pour les canaux que le bot n'envoie pas : réception seule (<c>F</c>, <c>T</c>) ou désactivé chez StarLoco (<c>¤</c>).</summary>
        public bool CanSend { get; }
        /// <summary>Règle appliquée par StarLoco (<c>GameClient.tchat</c>), pour l'aide et les infobulles.</summary>
        public string ServerRule { get; }

        public override string ToString() => Label;
    }

    /// <summary>
    /// Table des canaux : lettres de <c>Chat.send</c> du client 1.34 (<c>BM&lt;canal&gt;|&lt;texte&gt;|</c>), commandes de
    /// <c>Console.process</c>, couleurs <c>dofus.Constants</c> et délais de <c>GameClient.tchat</c> de StarLoco.
    /// </summary>
    public static class ChatChannels
    {
        public static readonly ChatChannel Default = new ChatChannel('*', "Général", "/s", "111111", ChatFilter.Messages, 500, true,
            "anti-flood de 500 ms (M10) ; ignoré si le joueur s'est désabonné de « * » ; les messages commençant par « . » sont des commandes du serveur");
        public static readonly ChatChannel Team = new ChatChannel('#', "Équipe", "/t", "0066FF", ChatFilter.Whispers, 0, true,
            "diffusé à l'équipe (ou aux spectateurs) seulement en combat");
        public static readonly ChatChannel Party = new ChatChannel('$', "Groupe", "/p", "006699", ChatFilter.Whispers, 0, true,
            "réservé aux membres d'un groupe");
        public static readonly ChatChannel Guild = new ChatChannel('%', "Guilde", "/g", "663399", ChatFilter.Guild, 0, true,
            "réservé aux membres d'une guilde");
        public static readonly ChatChannel Alignment = new ChatChannel('!', "Alignement", "/a", "DD7700", ChatFilter.Alignment, 30000, true,
            "personnage aligné et sans déshonneur (Im183), un message toutes les 30 s (Im0115)");
        public static readonly ChatChannel Recruitment = new ChatChannel('?', "Recrutement", "/r", "737373", ChatFilter.Recruitment, 40000, true,
            "un message toutes les 40 s (Im0115), niveau 6 minimum, zone abonnée");
        public static readonly ChatChannel Trade = new ChatChannel(':', "Commerce", "/b", "663300", ChatFilter.Trade, 50000, true,
            "un message toutes les 50 s (Im0115), niveau 6 minimum, zone abonnée");
        public static readonly ChatChannel Incarnam = new ChatChannel('^', "Incarnam", "/i", "0000CC", ChatFilter.Incarnam, 30000, true,
            "un message toutes les 30 s (Im0115), niveau 15 maximum");
        public static readonly ChatChannel Admin = new ChatChannel('@', "Admin", "/q", "FF00FF", ChatFilter.Admin, 0, true,
            "réservé aux comptes d'un groupe d'administration");
        /// <summary>Canal « privé » <c>¤</c> du client (<c>/m</c>) : son traitement est commenté dans StarLoco, le bot ne l'envoie pas.</summary>
        public static readonly ChatChannel Meta = new ChatChannel('¤', "Privé", "/m", "111111", ChatFilter.Messages, 0, false,
            "désactivé : le cas « ¤ » est commenté dans GameClient.tchat");
        /// <summary>Chuchotement reçu : <c>cMKF|&lt;id&gt;|&lt;nom&gt;|&lt;texte&gt;</c>.</summary>
        public static readonly ChatChannel WhisperFrom = new ChatChannel('F', "De", string.Empty, "0066FF", ChatFilter.Whispers, 0, false,
            "chuchotement reçu");
        /// <summary>Copie d'un chuchotement envoyé : <c>cMKT|&lt;id du destinataire&gt;|&lt;nom&gt;|&lt;texte&gt;</c>.</summary>
        public static readonly ChatChannel WhisperTo = new ChatChannel('T', "À", string.Empty, "0066FF", ChatFilter.Whispers, 0, false,
            "copie d'un chuchotement envoyé");

        private static readonly ChatChannel[] channels =
            { Default, Team, Party, Guild, Alignment, Recruitment, Trade, Incarnam, Admin, Meta, WhisperFrom, WhisperTo };

        /// <summary>Canaux que l'on peut choisir pour écrire, dans l'ordre du menu des canaux du client (<c>_btnHelp</c>).</summary>
        public static IReadOnlyList<ChatChannel> Sendable { get; } = Array.AsReadOnly(channels.Where(channel => channel.CanSend).ToArray());
        public static IReadOnlyList<ChatChannel> All { get; } = Array.AsReadOnly(channels);

        /// <summary>Lettres que le client envoie dans <c>cC±</c> (filtres 0, 2 à 8).</summary>
        public const string SubscriptionLetters = "i*#$p%!?:^";

        public static bool TryGet(char code, out ChatChannel channel)
        {
            channel = channels.FirstOrDefault(entry => entry.Code == code);
            return channel != null;
        }

        /// <summary>Canal d'un <c>cMK</c> : lettre inconnue ou vide = canal par défaut, comme <c>Chat.onMessage</c> du client.</summary>
        public static ChatChannel FromReceived(string code)
        {
            ChatChannel channel;
            return !string.IsNullOrEmpty(code) && TryGet(code[0], out channel) ? channel : Default;
        }

        /// <summary>Canal d'une commande de console (« s » ou « /s », sans tenir compte de la casse), ou null.</summary>
        public static ChatChannel FromCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            string name = command[0] == '/' ? command : "/" + command;
            return channels.FirstOrDefault(entry => entry.Command.Length > 0 && string.Equals(entry.Command, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Lettres d'un bouton de filtre du client (<c>Chat.subscribeChannels</c>) : 0 → <c>i</c>, 2 → <c>*</c>, 3 → <c>#$p</c>,
        /// 4 → <c>%</c>, 5 → <c>!</c>, 6 → <c>?</c>, 7 → <c>:</c>, 8 → <c>^</c>. Le filtre 1 (erreurs) n'a pas de lettre : il reste local.
        /// </summary>
        public static string FilterLetters(int filter)
        {
            switch (filter)
            {
                case 0: return "i";
                case 2: return "*";
                case 3: return "#$p";
                case 4: return "%";
                case 5: return "!";
                case 6: return "?";
                case 7: return ":";
                case 8: return "^";
                default: return string.Empty;
            }
        }

        /// <summary>Filtre d'affichage mis à jour par une lettre de <c>cC±</c> (<c>Chat.onSubscribeChannel</c>), ou null pour une lettre inconnue.</summary>
        public static ChatFilter? FilterOfLetter(char letter)
        {
            switch (letter)
            {
                case 'i': return ChatFilter.Infos;
                case '*': return ChatFilter.Messages;
                case '#':
                case '$':
                case 'p': return ChatFilter.Whispers;
                case '%': return ChatFilter.Guild;
                case '!': return ChatFilter.Alignment;
                case '?': return ChatFilter.Recruitment;
                case ':': return ChatFilter.Trade;
                case '^': return ChatFilter.Incarnam;
                case '@': return ChatFilter.Admin;
                default: return null;
            }
        }
    }

    /// <summary>Couleurs du client pour les lignes qui ne viennent pas d'un canal (<c>dofus.Constants</c>), sans « # ».</summary>
    public static class ChatColors
    {
        public const string Info = "009900";
        public const string Message = "111111";
        public const string Emote = "222222";
        public const string Think = "232323";
        public const string Whisper = "0066FF";
        public const string Error = "C10000";
        public const string Commands = "E4287C";
    }

    /// <summary>
    /// Textes du client branchables (fichiers de langue, lot D4) : sans résolveur, le bot affiche des numéros et n'a besoin
    /// d'aucun texte du client. Un résolveur qui échoue est ignoré (repli), jamais propagé.
    /// </summary>
    public static class ChatTexts
    {
        /// <summary>Nom d'une émote (<c>EM[id].n</c> de <c>emotes_fr</c>), null si inconnue.</summary>
        public static Func<int, string> EmoteName { get; set; }
        /// <summary>Identifiant d'une émote d'après son raccourci de console (<c>lang.getEmoteID</c>, « sit » → 1), null si inconnu.</summary>
        public static Func<string, int?> EmoteShortcut { get; set; }
        /// <summary>Nombre de smileys du client (15 dans le client 1.34) ; 0 ou null = pas de contrôle de borne.</summary>
        public static Func<int> SmileyCount { get; set; }
        /// <summary>Texte d'un message serveur <c>M1&lt;id&gt;|&lt;a;b&gt;</c> (<c>SRV_MSG_&lt;id&gt;</c> de <c>lang_fr</c>), null si inconnu.</summary>
        public static Func<int, string[], string> ServerPopup { get; set; }

        /// <summary>Raccourci connu sans fichier de langue : le bouton « s'asseoir » du client envoie l'émote 1 (<c>eU1</c>).</summary>
        internal const string SitShortcut = "sit";
        internal const int SitEmote = 1;

        internal static string ResolveEmoteName(int id)
        {
            try { return EmoteName?.Invoke(id); }
            catch (Exception) { return null; }
        }

        internal static int? ResolveEmoteShortcut(string shortcut)
        {
            try
            {
                int? id = EmoteShortcut?.Invoke(shortcut);
                if (id.HasValue) return id;
            }
            catch (Exception) { /* Missing or corrupt language data: fall back below. */ }
            return string.Equals(shortcut, SitShortcut, StringComparison.OrdinalIgnoreCase) ? SitEmote : (int?)null;
        }

        internal static int ResolveSmileyCount()
        {
            try { return SmileyCount?.Invoke() ?? 0; }
            catch (Exception) { return 0; }
        }

        internal static string ResolveServerPopup(int id, string[] args)
        {
            try { return ServerPopup?.Invoke(id, (string[])args.Clone()); }
            catch (Exception) { return null; }
        }
    }
}
