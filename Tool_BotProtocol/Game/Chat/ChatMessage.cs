using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Tool_BotProtocol.Game.Chat
{
    /// <summary>Origine d'une ligne du chat.</summary>
    public enum ChatMessageKind
    {
        /// <summary>Message d'un canal (<c>cMK&lt;canal&gt;</c>).</summary>
        Channel,
        /// <summary>Chuchotement reçu (<c>cMKF</c>).</summary>
        WhisperReceived,
        /// <summary>Copie d'un chuchotement envoyé (<c>cMKT</c>).</summary>
        WhisperSent,
        /// <summary>Message du serveur dans le chat (<c>cs&lt;texte&gt;</c>, message du jour, annonces).</summary>
        Server,
        /// <summary>Sortie d'une commande locale (<c>/mapid</c>, <c>/help</c>…), couleur <c>COMMANDS_CHAT</c>.</summary>
        Command,
        /// <summary>Erreur : <c>cME</c>, saisie refusée localement, commande inconnue.</summary>
        Error,
        /// <summary>Information du bot ou du serveur (<c>M1</c>, émote apprise…).</summary>
        Info,
    }

    /// <summary>Présentation d'un message de canal, comme <c>Chat.onMessage</c> du client.</summary>
    public enum ChatMessageStyle
    {
        Normal,
        /// <summary><c>*texte*</c> (commande <c>/me</c>) : affiché en italique « Nom texte ».</summary>
        Emote,
        /// <summary><c>!THINK!texte</c> (commande <c>/think</c>) : bulle de pensée.</summary>
        Think,
        /// <summary><c>**nombre**</c> : objet parlant, affiché par le client seulement avec l'option correspondante.</summary>
        SpeakingItem,
    }

    /// <summary>Une ligne du chat, reçue du serveur ou produite localement. Immuable.</summary>
    public sealed class ChatMessage
    {
        private static readonly Regex Tags = new Regex("<[^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex FontColor = new Regex("<font[^>]*color\\s*=\\s*['\"]?#?([0-9A-Fa-f]{6})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        internal ChatMessage(ChatMessageKind kind, ChatChannel channel, string rawChannel, long? authorId, string author,
            string rawText, string items, string color)
        {
            Kind = kind;
            Channel = channel ?? ChatChannels.Default;
            RawChannel = rawChannel ?? string.Empty;
            AuthorId = authorId;
            Author = author ?? string.Empty;
            RawText = rawText ?? string.Empty;
            Items = string.IsNullOrEmpty(items) ? null : items;
            Timestamp = DateTime.Now;
            Style = kind == ChatMessageKind.Channel ? StyleOf(RawText) : ChatMessageStyle.Normal;
            // Sorties de commande et erreurs locales sont du texte brut (« /w <nom> ») : seules les lignes du serveur sont du HTML.
            Text = kind == ChatMessageKind.Command || kind == ChatMessageKind.Error ? RawText : PlainText(Unstyle(RawText, Style));
            Color = color ?? ColorOf(kind, Channel, Style);
            Filter = FilterOf(kind, Channel);
        }

        public ChatMessageKind Kind { get; }
        /// <summary>Canal du message (canal par défaut pour une lettre inconnue, comme le client).</summary>
        public ChatChannel Channel { get; }
        /// <summary>Lettre reçue telle quelle (vide pour le canal par défaut de StarLoco).</summary>
        public string RawChannel { get; }
        /// <summary>Identifiant de l'auteur (sprite de la carte), null s'il est illisible ou absent.</summary>
        public long? AuthorId { get; }
        public string Author { get; }
        /// <summary>Texte affichable : style retiré (<c>*…*</c>, <c>!THINK!</c>), balises supprimées, entités HTML décodées (texte brut pour
        /// <see cref="ChatMessageKind.Command"/> et <see cref="ChatMessageKind.Error"/>, produits localement).</summary>
        public string Text { get; }
        /// <summary>Texte tel que reçu ou saisi.</summary>
        public string RawText { get; }
        /// <summary>Cinquième champ de <c>cMK</c> (objets liés du client) ; StarLoco ne l'envoie jamais rempli.</summary>
        public string Items { get; }
        public DateTime Timestamp { get; }
        /// <summary>Couleur du client en hexadécimal sans « # » (<c>dofus.Constants</c>).</summary>
        public string Color { get; }
        public ChatMessageStyle Style { get; }
        /// <summary>Filtre d'affichage du client auquel la ligne appartient.</summary>
        public ChatFilter Filter { get; }

        /// <summary>Ligne lisible : « (Guilde) Nom : texte », « De Nom : texte », « Nom texte » pour une émote.</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case ChatMessageKind.WhisperReceived: return "De " + Author + " : " + Text;
                case ChatMessageKind.WhisperSent: return "À " + Author + " : " + Text;
                case ChatMessageKind.Channel:
                    string prefix = Channel == ChatChannels.Default ? string.Empty : "(" + Channel.Label + ") ";
                    if (Style == ChatMessageStyle.Emote) return prefix + Author + " " + Text;
                    if (Style == ChatMessageStyle.Think) return prefix + Author + " pense : " + Text;
                    return prefix + Author + " : " + Text;
                default: return Text;
            }
        }

        /// <summary>Style d'un message de canal selon les marqueurs du client (<c>EMOTE_CHAR</c> = « * », <c>!THINK!</c>).</summary>
        public static ChatMessageStyle StyleOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return ChatMessageStyle.Normal;
            bool doubleStar = text.Length >= 2 && text[text.Length - 1] == '*' && text[text.Length - 2] == '*';
            if (!doubleStar && text.Length >= 2 && text[0] == '*' && text[text.Length - 1] == '*') return ChatMessageStyle.Emote;
            if (text.StartsWith("!THINK!", StringComparison.Ordinal)) return ChatMessageStyle.Think;
            if (doubleStar && text.Length > 4 && text.StartsWith("**", StringComparison.Ordinal) &&
                long.TryParse(text.Substring(2, text.Length - 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out long unused))
                return ChatMessageStyle.SpeakingItem;
            return ChatMessageStyle.Normal;
        }

        private static string Unstyle(string text, ChatMessageStyle style)
        {
            switch (style)
            {
                case ChatMessageStyle.Emote: return text.Substring(1, text.Length - 2);
                case ChatMessageStyle.Think: return text.Substring(7);
                case ChatMessageStyle.SpeakingItem: return text.Substring(2, text.Length - 4);
                default: return text;
            }
        }

        /// <summary>Texte sans balises HTML, entités décodées (« &amp;lt; » envoyé par le client redevient « &lt; »).</summary>
        public static string PlainText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            string text = html.IndexOf('<') >= 0 ? Tags.Replace(html.Replace("<br>", " ").Replace("<br/>", " ").Replace("<br />", " "), string.Empty) : html;
            return WebUtility.HtmlDecode(text);
        }

        /// <summary>Couleur de la première balise <c>&lt;font color='#RRGGBB'&gt;</c> d'un message <c>cs</c>, ou null.</summary>
        public static string FontColorOf(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            Match match = FontColor.Match(html);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        private static string ColorOf(ChatMessageKind kind, ChatChannel channel, ChatMessageStyle style)
        {
            switch (kind)
            {
                case ChatMessageKind.WhisperReceived:
                case ChatMessageKind.WhisperSent: return ChatColors.Whisper;
                case ChatMessageKind.Server:
                case ChatMessageKind.Info: return ChatColors.Info;
                case ChatMessageKind.Command: return ChatColors.Commands;
                case ChatMessageKind.Error: return ChatColors.Error;
            }
            if (channel == ChatChannels.Default && style == ChatMessageStyle.Emote) return ChatColors.Emote;
            if (channel == ChatChannels.Default && style == ChatMessageStyle.Think) return ChatColors.Think;
            return channel.Color;
        }

        private static ChatFilter FilterOf(ChatMessageKind kind, ChatChannel channel)
        {
            switch (kind)
            {
                case ChatMessageKind.WhisperReceived:
                case ChatMessageKind.WhisperSent: return ChatFilter.Whispers;
                case ChatMessageKind.Server:
                case ChatMessageKind.Info: return ChatFilter.Infos;
                case ChatMessageKind.Command: return ChatFilter.Admin;
                case ChatMessageKind.Error: return ChatFilter.Errors;
                default: return channel.Filter;
            }
        }
    }

    /// <summary>Message serveur en fenêtre <c>M1&lt;id&gt;[|&lt;a;b;…&gt;[|&lt;nom&gt;]]</c> (<c>Basics.onServerMessage</c> du client, <c>SRV_MSG_&lt;id&gt;</c>).</summary>
    public sealed class ChatServerPopup
    {
        internal ChatServerPopup(int id, string[] args, string name, string text, bool resolved)
        {
            Id = id;
            Args = Array.AsReadOnly(args);
            Name = name ?? string.Empty;
            Text = text;
            Resolved = resolved;
        }

        /// <summary>Numéro du texte <c>SRV_MSG_&lt;id&gt;</c> (0 pour <c>M10</c>, l'anti-flood du canal général).</summary>
        public int Id { get; }
        public System.Collections.Generic.IReadOnlyList<string> Args { get; }
        public string Name { get; }
        public string Text { get; }
        /// <summary>Vrai lorsque le texte vient des fichiers de langue (<see cref="ChatTexts.ServerPopup"/>).</summary>
        public bool Resolved { get; }
        /// <summary>Vrai pour <c>M10</c> : StarLoco l'envoie quand deux messages du canal général sont espacés de moins de 500 ms.</summary>
        public bool IsFlood => Id == 0;
    }

    /// <summary>Résultat d'une demande d'envoi : paquets réellement envoyés et message à présenter.</summary>
    public sealed class ChatResult
    {
        internal ChatResult(bool accepted, string message, params string[] packets)
        {
            Accepted = accepted;
            Message = message ?? string.Empty;
            Packets = Array.AsReadOnly(packets ?? new string[0]);
        }

        /// <summary>Vrai si la demande a été acceptée : paquet envoyé ou commande locale exécutée.</summary>
        public bool Accepted { get; }
        /// <summary>Vrai si au moins un paquet est parti.</summary>
        public bool Sent => Packets.Count > 0;
        public string Message { get; }
        /// <summary>Paquets envoyés, dans l'ordre (un par envoi).</summary>
        public System.Collections.Generic.IReadOnlyList<string> Packets { get; }
        public string Packet => Packets.Count > 0 ? Packets[0] : null;

        internal static ChatResult Refused(string message) => new ChatResult(false, message);
    }
}
