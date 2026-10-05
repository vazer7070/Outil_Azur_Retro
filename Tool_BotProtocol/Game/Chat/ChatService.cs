using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Utils.Interfaces;

namespace Tool_BotProtocol.Game.Chat
{
    /// <summary>Historique de saisie du client (<c>Console.pushHistory</c>, <c>pushWhisper</c>) : 50 entrées, pas de doublon consécutif.</summary>
    public sealed class ChatHistory
    {
        private readonly object sync = new object();
        private readonly List<string> items = new List<string>();
        private readonly int capacity;
        private int pointer;

        internal ChatHistory(int capacity) { this.capacity = capacity; }

        public IReadOnlyList<string> Items { get { lock (sync) return items.ToArray(); } }

        /// <summary>Ajoute une entrée (ignorée si elle répète la dernière) et replace le curseur après la fin.</summary>
        public void Push(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (sync)
            {
                if (items.Count == 0 || items[items.Count - 1] != value)
                {
                    items.Add(value);
                    if (items.Count > capacity) items.RemoveAt(0);
                }
                pointer = items.Count;
            }
        }

        /// <summary>Entrée précédente (touche Haut), chaîne vide s'il n'y en a pas.</summary>
        public string Up()
        {
            lock (sync)
            {
                if (pointer > 0) pointer--;
                return pointer < items.Count ? items[pointer] : string.Empty;
            }
        }

        /// <summary>Entrée suivante (touche Bas), chaîne vide après la dernière.</summary>
        public string Down()
        {
            lock (sync)
            {
                if (pointer < items.Count) pointer++;
                return pointer < items.Count ? items[pointer] : string.Empty;
            }
        }

        public void ResetPointer() { lock (sync) pointer = items.Count; }
    }

    /// <summary>
    /// Service de chat d'un compte, indépendant de l'interface : envois au format du client 1.34 (<c>Chat.send</c>,
    /// <c>subscribeChannels</c>, <c>useSmiley</c>, <c>Emotes.useEmote</c>, <c>Console.process</c>) et lecture des paquets de
    /// StarLoco (<c>cMK</c>, <c>cME</c>, <c>cC±</c>, <c>cS</c>, <c>cs</c>, <c>M1</c>, <c>eUK</c>, <c>eL</c>, <c>eA</c>, <c>eR</c>).
    /// Les événements des paquets reçus sont levés sur le fil réseau, ceux des lignes locales sur le fil appelant : un abonné
    /// d'interface repasse sur son fil (<c>BeginInvoke</c>). Une exception d'abonné est journalisée, jamais propagée.
    /// </summary>
    public sealed class ChatService : IEliminable
    {
        /// <summary>Longueur maximale d'un message (<c>MAX_MESSAGE_LENGTH</c>) : au-delà, le client le coupe à 199 caractères.</summary>
        public const int MaxMessageLength = 200;
        /// <summary>Marge de saisie pour les préfixes (<c>MAX_MESSAGE_LENGTH_MARGIN</c>) : le champ du client accepte 250 caractères.</summary>
        public const int MaxMessageLengthMargin = 50;
        public const int MaxInputLength = MaxMessageLength + MaxMessageLengthMargin;
        /// <summary>Lignes conservées (<c>ChatManager.MAX_ALL_LENGTH</c>).</summary>
        public const int MaxMessages = 150;
        public const int HistorySize = 50;
        /// <summary>Délai minimal entre deux smileys ou deux émotes (<c>CLICK_MIN_DELAY</c>) ; un clic plus rapproché est ignoré.</summary>
        public const int ClickMinDelayMilliseconds = 800;
        /// <summary>Durée d'affichage d'un smiley au-dessus du personnage (<c>SMILEY_DELAY</c>), pour le rendu.</summary>
        public const int SmileyDisplayMilliseconds = 3000;
        /// <summary>Bit « peut parler dans le canal général » des restrictions du personnage (<c>AR</c>, <c>Player.canChatToAll</c>).</summary>
        public const int CannotChatToAllRestriction = 16;

        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly LinkedList<ChatMessage> messages = new LinkedList<ChatMessage>();
        private readonly StringBuilder subscribed = new StringBuilder();
        private readonly SortedSet<int> emotes = new SortedSet<int>();
        private long lastSmiley, lastEmote;
        private int selfEmote;
        private ChatServerPopup lastPopup;

        internal ChatService(Accounts.Accounts owner)
        {
            account = owner;
            InputHistory = new ChatHistory(HistorySize);
            WhisperHistory = new ChatHistory(HistorySize);
        }

        internal Accounts.Accounts Account => account;

        /// <summary>Historique des lignes saisies (Haut/Bas).</summary>
        public ChatHistory InputHistory { get; }
        /// <summary>Historique des chuchotements « /w &lt;nom&gt; » envoyés et reçus (Maj + Haut/Bas).</summary>
        public ChatHistory WhisperHistory { get; }

        /// <summary>Ligne ajoutée au chat (reçue ou locale).</summary>
        public event Action<ChatMessage> MessageReceived;
        /// <summary>Abonnements confirmés par le serveur (<c>cC±</c>) : lettres actuellement actives.</summary>
        public event Action<string> SubscriptionsChanged;
        /// <summary>Smiley au-dessus d'un acteur (<c>cS&lt;acteur&gt;|&lt;smiley&gt;</c>).</summary>
        public event Action<long, int> SmileyReceived;
        /// <summary>Émote jouée par un acteur (<c>eUK&lt;acteur&gt;|&lt;émote&gt;</c>, 0 = aucune).</summary>
        public event Action<long, int> EmoteReceived;
        /// <summary>Liste des émotes disponibles modifiée (<c>eL</c>, <c>eA</c>, <c>eR</c>).</summary>
        public event Action EmotesChanged;
        /// <summary>Message serveur en fenêtre (<c>M1</c>).</summary>
        public event Action<ChatServerPopup> ServerPopupReceived;
        /// <summary>Lignes effacées (<c>/cls</c>).</summary>
        public event Action Cleared;

        public IReadOnlyList<ChatMessage> Messages { get { lock (sync) return messages.ToArray(); } }
        /// <summary>Lettres des canaux auxquels le serveur dit le personnage abonné, dans l'ordre de réception.</summary>
        public string SubscribedChannels { get { lock (sync) return subscribed.ToString(); } }
        public bool IsSubscribed(char letter) { lock (sync) return subscribed.ToString().IndexOf(letter) >= 0; }
        /// <summary>Émotes annoncées par <c>eL</c>/<c>eA</c> (StarLoco n'y fait jamais figurer l'émote 1, « s'asseoir », qui reste utilisable).</summary>
        public IReadOnlyList<int> AvailableEmotes { get { lock (sync) return emotes.ToArray(); } }
        /// <summary>Dernière émote du personnage annoncée par <c>eUK</c> (0 = aucune).</summary>
        public int SelfEmote { get { lock (sync) return selfEmote; } }
        /// <summary>Assis selon StarLoco (<c>useEmote</c> : émotes 1, 19 et 20).</summary>
        public bool IsSelfSitting { get { int emote = SelfEmote; return IsSittingEmote(emote); } }
        public ChatServerPopup LastServerPopup { get { lock (sync) return lastPopup; } }

        /// <summary>Faux si les restrictions <c>AR</c> du personnage interdisent le canal général (<c>canChatToAll</c>).</summary>
        public bool CanChatToAll
        {
            get
            {
                int raw = account?.Game?.Session?.RawRestrictions ?? -1;
                return raw < 0 || (raw & CannotChatToAllRestriction) == 0;
            }
        }

        public static bool IsSittingEmote(int emote) => emote == 1 || emote == 19 || emote == 20;

        // ---------------------------------------------------------------- envois

        /// <summary>
        /// Prépare un texte comme <c>Chat.send</c> du client : retours à la ligne et NUL retirés (StarLoco tronque le paquet
        /// au premier <c>\n</c>), « &lt; » et « &gt; » échappés en entités, « | » supprimé, puis coupé à 199 caractères
        /// s'il dépasse 200. Renvoie une chaîne vide si rien ne reste.
        /// </summary>
        public static string PrepareText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var builder = new StringBuilder(text.Length + 8);
            foreach (char character in text)
            {
                if (character == '<') builder.Append("&lt;");
                else if (character == '>') builder.Append("&gt;");
                else if (character == '|' || char.IsControl(character)) continue;
                else builder.Append(character);
            }
            string prepared = builder.ToString();
            return prepared.Length > MaxMessageLength ? prepared.Substring(0, MaxMessageLength - 1) : prepared;
        }

        /// <summary>
        /// Envoie <c>BM&lt;canal&gt;|&lt;texte&gt;|</c> (le troisième champ, objets liés, reste vide : StarLoco ne les gère pas).
        /// Spectateur d'un combat : le canal général devient le canal d'équipe, comme dans le client. Aucun délai local :
        /// StarLoco répond <c>M10</c> (anti-flood) ou <c>Im0115;&lt;s&gt;</c> (délai du canal).
        /// </summary>
        public Task<ChatResult> SendAsync(char channel, string text)
        {
            ChatChannel target;
            if (!ChatChannels.TryGet(channel, out target) || !target.CanSend)
                return Task.FromResult(Refuse(channel == '¤'
                    ? "Le canal « ¤ » n'est pas traité par StarLoco (cas commenté) : message non envoyé."
                    : "Canal de discussion inconnu : « " + channel + " ».", true));
            if (target == ChatChannels.Default && (account?.Game?.Fight?.IsSpectator ?? false)) target = ChatChannels.Team;
            string message = PrepareText(text);
            if (message.Length == 0) return Task.FromResult(Refuse("Message vide : rien n'est envoyé.", false));
            string secret = Secret(text);
            if (secret != null) return Task.FromResult(Refuse(secret, true));
            return SendPacketsAsync(null, "BM" + target.Code + "|" + message + "|");
        }

        /// <summary>Chuchote : <c>BM&lt;nom&gt;|&lt;texte&gt;|</c> ; StarLoco répond <c>cMKT</c> (copie), <c>cMEf&lt;nom&gt;</c> ou <c>Im114;&lt;nom&gt;</c>.</summary>
        public Task<ChatResult> WhisperAsync(string name, string text)
        {
            string target = (name ?? string.Empty).Trim();
            string invalid = InvalidName(target);
            if (invalid != null) return Task.FromResult(Refuse(invalid, true));
            string self = account?.Game?.character?.Name;
            if (!string.IsNullOrEmpty(self) && string.Equals(self, target, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Refuse("Impossible de se chuchoter à soi-même.", true));
            string message = PrepareText(text);
            if (message.Length == 0) return Task.FromResult(Refuse("Message vide : rien n'est envoyé.", false));
            string secret = Secret(text);
            if (secret != null) return Task.FromResult(Refuse(secret, true));
            return SendPacketsAsync(null, "BM" + target + "|" + message + "|");
        }

        /// <summary>Active des canaux : un <c>cC+&lt;lettre&gt;</c> par lettre (StarLoco ne lit que le premier caractère).</summary>
        public Task<ChatResult> SubscribeAsync(string letters) => SubscriptionAsync('+', letters);

        /// <summary>Désactive des canaux : un <c>cC-&lt;lettre&gt;</c> par lettre. Sans « * », StarLoco ignore aussi les messages envoyés dans le canal général.</summary>
        public Task<ChatResult> UnsubscribeAsync(string letters) => SubscriptionAsync('-', letters);

        /// <summary>Bouton de filtre du client (0 à 8) : envoie les lettres du filtre une à une ; le filtre 1 (erreurs) reste local.</summary>
        public Task<ChatResult> SetFilterAsync(int filter, bool visible)
        {
            if (filter < 0 || filter > 8) return Task.FromResult(Refuse("Filtre de discussion inconnu : " + filter + ".", false));
            string letters = ChatChannels.FilterLetters(filter);
            if (letters.Length == 0) return Task.FromResult(new ChatResult(true, "Filtre local : aucun paquet."));
            return SubscriptionAsync(visible ? '+' : '-', letters);
        }

        /// <summary>Smiley : <c>BS&lt;id&gt;</c>, ignoré s'il suit le précédent de moins de 800 ms (<c>CLICK_MIN_DELAY</c>).</summary>
        public Task<ChatResult> SmileyAsync(int id)
        {
            int count = ChatTexts.ResolveSmileyCount();
            if (id < 1 || (count > 0 && id > count)) return Task.FromResult(Refuse("Smiley inconnu : " + id + ".", true));
            if (!Throttle(ref lastSmiley)) return Task.FromResult(Refuse("Smiley ignoré : moins de 800 ms depuis le précédent.", false));
            return SendPacketsAsync(() => ResetTimer(ref lastSmiley), "BS" + id.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Émote : <c>eU&lt;id&gt;</c>, refusée en combat et à moins de 800 ms de la précédente, comme <c>Emotes.useEmote</c>.
        /// Aucun <c>eUE</c> n'est attendu (StarLoco ne l'envoie pas) : seul <c>eUK</c> confirme.
        /// </summary>
        public Task<ChatResult> EmoteAsync(int id)
        {
            if (id < 1) return Task.FromResult(Refuse("Émote inconnue : " + id + ".", true));
            if ((account?.Game?.Fight?.IsInFight ?? false) || (account?.IsFighting() ?? false))
                return Task.FromResult(Refuse("Les émotes ne sont pas utilisables en combat.", true));
            if (!Throttle(ref lastEmote)) return Task.FromResult(Refuse("Émote ignorée : moins de 800 ms depuis la précédente.", false));
            return SendPacketsAsync(() => ResetTimer(ref lastEmote), "eU" + id.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Bouton « s'asseoir » du client : émote 1.</summary>
        public Task<ChatResult> SitAsync() => EmoteAsync(ChatTexts.SitEmote);

        /// <summary>
        /// Ligne saisie dans la console du client : « /commande … » (voir <see cref="ChatCommands"/>) ou message du canal
        /// général. La ligne est ajoutée à l'historique avant d'être traitée, comme dans le client.
        /// </summary>
        public Task<ChatResult> ExecuteAsync(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return Task.FromResult(Refuse("Message vide : rien n'est envoyé.", false));
            InputHistory.Push(line);
            if (line[0] == '/') return ChatCommands.ExecuteAsync(this, line);
            if (!CanChatToAll) return Task.FromResult(Refuse("Les restrictions du personnage interdisent le canal général.", true));
            return SendAsync('*', line);
        }

        /// <summary>Efface les lignes conservées (<c>/cls</c>).</summary>
        public void ClearMessages()
        {
            lock (sync) messages.Clear();
            Raise(Cleared, handler => handler());
        }

        /// <summary>Ajoute une ligne locale (sortie de commande, erreur, information) et la publie.</summary>
        public ChatMessage AddLocal(ChatMessageKind kind, string text)
        {
            var message = new ChatMessage(kind, null, null, null, null, text, null, null);
            Publish(message);
            return message;
        }

        /// <summary>Oublie l'état de session (abonnements, émotes, délais) ; les lignes et les historiques sont gardés.</summary>
        public void Clear()
        {
            bool changed;
            lock (sync)
            {
                changed = subscribed.Length > 0 || emotes.Count > 0;
                subscribed.Clear();
                emotes.Clear();
                selfEmote = 0;
                lastSmiley = lastEmote = 0;
                lastPopup = null;
            }
            if (changed)
            {
                Raise(SubscriptionsChanged, handler => handler(string.Empty));
                Raise(EmotesChanged, handler => handler());
            }
        }

        internal ChatResult Refuse(string message, bool show)
        {
            if (show) AddLocal(ChatMessageKind.Error, message);
            return ChatResult.Refused(message);
        }

        internal async Task<ChatResult> SendPacketsAsync(Action onFailure, params string[] packets)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected())
            {
                onFailure?.Invoke();
                return Refuse("Connectez le personnage avant d'écrire dans le chat.", true);
            }
            var sent = new List<string>();
            try
            {
                foreach (string packet in packets)
                {
                    await connection.SendPacketAsync(packet).ConfigureAwait(false);
                    sent.Add(packet);
                }
            }
            catch (Exception error)
            {
                account?.Logger?.LogException("CHAT", error);
                if (sent.Count == 0) onFailure?.Invoke();
                return new ChatResult(sent.Count > 0, "Envoi interrompu : " + error.Message, sent.ToArray());
            }
            return new ChatResult(true, string.Empty, sent.ToArray());
        }

        private Task<ChatResult> SubscriptionAsync(char sign, string letters)
        {
            if (string.IsNullOrEmpty(letters)) return Task.FromResult(Refuse("Aucun canal indiqué.", false));
            var packets = new List<string>();
            foreach (char letter in letters)
            {
                if (ChatChannels.SubscriptionLetters.IndexOf(letter) < 0)
                    return Task.FromResult(Refuse("Canal « " + letter + " » : le client n'envoie que les lettres " + ChatChannels.SubscriptionLetters + ".", true));
                string packet = "cC" + sign + letter;
                if (!packets.Contains(packet)) packets.Add(packet);
            }
            return SendPacketsAsync(null, packets.ToArray());
        }

        /// <summary>Comme le client : refuse un message qui contient l'identifiant ou le mot de passe du compte.</summary>
        private string Secret(string text)
        {
            var config = account?.accountConfig;
            if (config == null || string.IsNullOrEmpty(text)) return null;
            bool login = !string.IsNullOrEmpty(config.Account) && text.IndexOf(config.Account, StringComparison.Ordinal) >= 0;
            bool password = !string.IsNullOrEmpty(config.Password) && text.IndexOf(config.Password, StringComparison.Ordinal) >= 0;
            return login || password ? "Message refusé : il contient l'identifiant ou le mot de passe du compte." : null;
        }

        /// <summary>Nom de destinataire acceptable : deux caractères au moins, sans espace, « | », « ; » ni lettre de canal en tête.</summary>
        internal static string InvalidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 2) return "Indiquez le nom du destinataire (deux caractères au moins).";
            if (name.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == '|' || character == ';' || character == '<' || character == '>'))
                return "Nom de destinataire invalide : " + name + ".";
            ChatChannel channel;
            if (ChatChannels.TryGet(name[0], out channel)) return "Nom de destinataire invalide : il commence par la lettre d'un canal.";
            return null;
        }

        private bool Throttle(ref long last)
        {
            long now = Stopwatch.GetTimestamp();
            lock (sync)
            {
                if (last != 0 && (now - last) * 1000L / Stopwatch.Frequency < ClickMinDelayMilliseconds) return false;
                last = now;
                return true;
            }
        }

        private void ResetTimer(ref long field) { lock (sync) field = 0; }

        // ---------------------------------------------------------------- réception (fil réseau)

        /// <summary><c>cMK&lt;canal&gt;|&lt;id&gt;|&lt;nom&gt;|&lt;texte&gt;[|&lt;objets&gt;[|…]]</c> : 4 champs chez StarLoco, 5ᵉ toléré.</summary>
        internal void OnMessagePacket(string packet)
        {
            string[] parts = packet.Length > 3 ? packet.Substring(3).Split(new[] { '|' }, 5) : new string[0];
            if (parts.Length < 4)
            {
                Debug("Message de chat illisible ignoré : " + packet);
                return;
            }
            long id;
            long? author = long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out id) ? id : (long?)null;
            string items = parts.Length > 4 ? parts[4].Split('|')[0] : null;
            ChatChannel channel = ChatChannels.FromReceived(parts[0]);
            ChatMessageKind kind = channel == ChatChannels.WhisperFrom ? ChatMessageKind.WhisperReceived
                : channel == ChatChannels.WhisperTo ? ChatMessageKind.WhisperSent : ChatMessageKind.Channel;
            var message = new ChatMessage(kind, channel, parts[0], author, parts[2], parts[3], items, null);
            if (kind == ChatMessageKind.WhisperReceived && parts[2].Length > 0)
            {
                WhisperHistory.Push("/w " + parts[2] + " ");
                try { account?.Game?.character?.CheckWhoSpeak(parts[2]); }
                catch (Exception error) { account?.Logger?.LogError("CHAT", "Un abonné aux chuchotements a échoué : " + error.Message); }
            }
            Publish(message);
        }

        /// <summary><c>cME&lt;type&gt;&lt;nom&gt;</c> : f = destinataire absent (seul envoyé par StarLoco), e, n, S comme le client.</summary>
        internal void OnErrorPacket(string packet)
        {
            if (packet.Length < 4)
            {
                Debug("Erreur de chat illisible ignorée : " + packet);
                return;
            }
            string name = packet.Substring(4);
            string text;
            switch (packet[3])
            {
                case 'f': text = name + " n'est pas connecté ou n'existe pas : message non remis."; break;
                case 'e': text = name + " n'est pas connecté : message non remis."; break;
                case 'n': text = name + " est inconnu : message non remis."; break;
                case 'S': text = "Syntaxe : /w <nom> <message>"; break;
                default:
                    Debug("Erreur de chat inconnue ignorée : " + packet);
                    return;
            }
            AddLocal(ChatMessageKind.Error, text);
        }

        /// <summary><c>cC+&lt;lettres&gt;</c> / <c>cC-&lt;lettres&gt;</c> : écho d'un abonnement (une lettre) ou liste à l'entrée en jeu.</summary>
        internal void OnSubscriptionPacket(string packet)
        {
            if (packet.Length < 4 || (packet[2] != '+' && packet[2] != '-'))
            {
                Debug("Abonnement de chat illisible ignoré : " + packet);
                return;
            }
            bool add = packet[2] == '+';
            string current;
            lock (sync)
            {
                foreach (char letter in packet.Substring(3))
                {
                    int index = subscribed.ToString().IndexOf(letter);
                    if (add && index < 0) subscribed.Append(letter);
                    else if (!add && index >= 0) subscribed.Remove(index, 1);
                }
                current = subscribed.ToString();
            }
            var character = account?.Game?.character;
            if (character != null) character.Canal = current;
            Raise(SubscriptionsChanged, handler => handler(current));
        }

        /// <summary><c>cS&lt;acteur&gt;|&lt;smiley&gt;</c>.</summary>
        internal void OnSmileyPacket(string packet)
        {
            string[] parts = packet.Substring(2).Split('|');
            long actor;
            int smiley;
            if (parts.Length < 2 || !long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out actor) ||
                !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out smiley))
            {
                Debug("Smiley illisible ignoré : " + packet);
                return;
            }
            Raise(SmileyReceived, handler => handler(actor, smiley));
        }

        /// <summary><c>cs&lt;texte&gt;</c> : message du serveur (StarLoco l'entoure de <c>&lt;font color='#RRGGBB'&gt;</c>).</summary>
        internal void OnServerNoticePacket(string packet)
        {
            string html = packet.Substring(2);
            if (html.Length == 0) return;
            Publish(new ChatMessage(ChatMessageKind.Server, null, null, null, null, html, null, ChatMessage.FontColorOf(html)));
        }

        /// <summary><c>M1&lt;id&gt;[|&lt;a;b&gt;[|&lt;nom&gt;]]</c> : message serveur en fenêtre (<c>M10</c> = anti-flood du canal général).</summary>
        internal void OnServerPopupPacket(string packet)
        {
            string[] parts = packet.Substring(2).Split('|');
            int id;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out id))
            {
                Debug("Message serveur M1 illisible ignoré : " + packet);
                return;
            }
            string[] args = parts.Length > 1 && parts[1].Length > 0 ? parts[1].Split(';') : new string[0];
            string name = parts.Length > 2 ? parts[2] : string.Empty;
            string resolved = ChatTexts.ResolveServerPopup(id, args);
            string text = !string.IsNullOrEmpty(resolved) ? ChatMessage.PlainText(resolved)
                : id == 0 ? "Anti-flood du serveur : message non diffusé (moins de 500 ms depuis le précédent)."
                : "Message du serveur n° " + id + (args.Length > 0 ? " : " + string.Join(" ; ", args) : string.Empty);
            var popup = new ChatServerPopup(id, args, name, text, !string.IsNullOrEmpty(resolved));
            lock (sync) lastPopup = popup;
            AddLocal(ChatMessageKind.Info, text);
            Raise(ServerPopupReceived, handler => handler(popup));
        }

        /// <summary><c>eUK&lt;acteur&gt;|&lt;émote&gt;[|&lt;durée&gt;]</c> : StarLoco envoie deux champs.</summary>
        internal void OnEmotePacket(string packet)
        {
            string[] parts = packet.Substring(3).Split('|');
            long actor;
            int emote;
            if (parts.Length < 2 || !long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out actor) ||
                !int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out emote))
            {
                Debug("Émote illisible ignorée : " + packet);
                return;
            }
            var character = account?.Game?.character;
            if (character != null && character.id != 0 && character.id == actor)
            {
                lock (sync) selfEmote = emote;
                // Émotes assises (1, 19 et 20 dans useEmote de StarLoco) : délai de régénération divisé par deux.
                if (IsSittingEmote(emote))
                {
                    if (!account.IsFighting() && account.AccountStates != AccountStates.REGENERATION) account.AccountStates = AccountStates.REGENERATION;
                }
                else if (account.AccountStates == AccountStates.REGENERATION) account.AccountStates = AccountStates.CONNECTED_INACTIVE;
            }
            Raise(EmoteReceived, handler => handler(actor, emote));
        }

        /// <summary><c>eUE</c> : refus d'émote du client (jamais envoyé par StarLoco).</summary>
        internal void OnEmoteErrorPacket() => AddLocal(ChatMessageKind.Error, "Le serveur a refusé l'émote.");

        /// <summary><c>eL&lt;masque&gt;|&lt;masque&gt;</c> : bit n → émote n + 1, pour les deux masques (<c>Emotes.onList</c>).</summary>
        internal void OnEmoteListPacket(string packet)
        {
            string[] parts = packet.Substring(2).Split('|');
            var list = new SortedSet<int>();
            bool readable = false;
            foreach (string part in parts.Take(2))
            {
                long mask;
                if (!long.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out mask)) continue;
                readable = true;
                int bits = unchecked((int)mask);
                for (int bit = 0; bit < 32; bit++)
                    if (((bits >> bit) & 1) == 1 && EmoteKnown(bit + 1)) list.Add(bit + 1);
            }
            if (!readable)
            {
                Debug("Liste d'émotes illisible ignorée : " + packet);
                return;
            }
            lock (sync)
            {
                emotes.Clear();
                emotes.UnionWith(list);
            }
            Raise(EmotesChanged, handler => handler());
        }

        /// <summary><c>eA&lt;id&gt;[|0]</c> (émote apprise) et <c>eR&lt;id&gt;[|0]</c> (retirée) ; « 0 » en second champ = sans annonce.</summary>
        internal void OnEmoteChangePacket(string packet, bool added)
        {
            string[] parts = packet.Substring(2).Split('|');
            int id;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 1)
            {
                Debug("Émote illisible ignorée : " + packet);
                return;
            }
            lock (sync)
            {
                if (added) emotes.Add(id);
                else emotes.Remove(id);
            }
            Raise(EmotesChanged, handler => handler());
            if (parts.Length > 1 && parts[1] == "0") return;
            string name = ChatTexts.ResolveEmoteName(id);
            string label = string.IsNullOrEmpty(name) ? "n° " + id : "« " + name + " »";
            AddLocal(ChatMessageKind.Info, (added ? "Nouvelle émote disponible : " : "Émote retirée : ") + label + ".");
        }

        private static bool EmoteKnown(int id)
        {
            // Comme le client, une émote absente des fichiers de langue est écartée ; sans eux, chaque bit est gardé.
            if (ChatTexts.EmoteName == null) return true;
            return !string.IsNullOrEmpty(ChatTexts.ResolveEmoteName(id));
        }

        private void Publish(ChatMessage message)
        {
            lock (sync)
            {
                messages.AddLast(message);
                while (messages.Count > MaxMessages) messages.RemoveFirst();
            }
            try { account?.Logger?.LogChat(null, message.ToString(), message.Color); }
            catch { /* A closed journal must not stop the chat. */ }
            Raise(MessageReceived, handler => handler(message));
        }

        private void Debug(string text)
        {
            try { account?.Logger?.LogDebug("CHAT", text); }
            catch { /* Diagnostics only. */ }
        }

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError("CHAT", "Un abonné aux événements du chat a échoué : " + error.Message); }
                    catch { /* Journal fermé : le protocole continue. */ }
                }
            }
        }
    }
}
