using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Interactions;

namespace Tool_BotProtocol.Game.Social
{
    /// <summary>
    /// Amis, ennemis et conjoint du compte selon <c>dofus.aks.Friends</c> / <c>dofus.aks.Enemies</c> du client 1.34 et
    /// <c>GameClient.parseFrienDDacket</c> / <c>parseEnemyPacket</c> de StarLoco.
    /// Envois : <c>FL</c>, <c>FA[%]&lt;nom&gt;</c>, <c>FD&lt;nom|*compte&gt;</c>, <c>FO±</c>, <c>FJS</c>, <c>FJC±</c>, <c>iL</c>,
    /// <c>iA[%]&lt;nom&gt;</c>, <c>iD&lt;nom|*compte&gt;</c>. Réceptions (via <c>FriendsFrame</c>) : <c>FL</c>, <c>FA K|E</c>,
    /// <c>FD K|E</c>, <c>FS</c>, <c>FO±</c>, <c>iL</c>, <c>iA K|E</c>, <c>iD K|E</c>. <c>FJF</c> (rejoindre un ami) n'est pas proposé :
    /// StarLoco l'ignore. Les listes ne changent qu'à la réception des paquets du serveur ; seuls l'avertissement de connexion
    /// (<c>FO±</c>, auquel StarLoco ne répond que <c>BN</c>) et le suivi du conjoint (<c>FJC±</c>, bascule locale du client)
    /// sont posés à l'envoi. Les événements sont levés sur le fil de réception réseau : l'interface repasse sur son fil.
    /// </summary>
    public sealed class FriendsActions
    {
        private const string Reference = "AMIS";
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private FriendEntry[] friends = new FriendEntry[0], enemies = new FriendEntry[0];
        private bool friendsReceived, enemiesReceived;
        private SpouseInfo spouse;
        private bool? warnOnLogin;
        private string lastMessage = string.Empty;
        private volatile bool windowOpen;

        internal FriendsActions(Accounts.Accounts owner) { account = owner; }

        /// <summary>Dernière liste d'amis reçue (<c>FL</c>), complétée par les ajouts <c>FAK</c>.</summary>
        public IReadOnlyList<FriendEntry> Friends { get { lock (sync) return friends; } }
        /// <summary>Dernière liste d'ennemis reçue (<c>iL</c>), complétée par les ajouts <c>iAK</c>.</summary>
        public IReadOnlyList<FriendEntry> Enemies { get { lock (sync) return enemies; } }
        public IReadOnlyList<FriendEntry> List(FriendListKind kind) => kind == FriendListKind.Enemies ? Enemies : Friends;
        /// <summary>Vrai dès qu'une liste <c>FL</c> (ou <c>iL</c>) a été reçue pendant la session.</summary>
        public bool HasReceived(FriendListKind kind) { lock (sync) return kind == FriendListKind.Enemies ? enemiesReceived : friendsReceived; }
        /// <summary>Conjoint annoncé par <c>FS</c>, ou <c>null</c>.</summary>
        public SpouseInfo Spouse { get { lock (sync) return spouse; } }
        /// <summary>Avertissement à la connexion d'un ami (<c>FO+</c> / <c>FO-</c> reçu à l'entrée en jeu ou envoyé), <c>null</c> si inconnu.</summary>
        public bool? WarnOnFriendLogin { get { lock (sync) return warnOnLogin; } }
        /// <summary>Dernier message (confirmation, erreur du serveur traduite, refus local).</summary>
        public string LastMessage { get { lock (sync) return lastMessage; } }

        /// <summary>
        /// Fenêtre des amis ouverte dans l'interface. Comme le client (<c>getUIComponent("Friends")</c>), une liste reçue
        /// fenêtre fermée (<c>/f L</c>, retrait) est aussi écrite dans le chat.
        /// </summary>
        public bool WindowOpen { get => windowOpen; set => windowOpen = value; }

        /// <summary>Listes, conjoint, avertissement ou message changés (fil réseau ou appelant).</summary>
        public event Action Changed;
        /// <summary>Erreur que le client montre dans une boîte (<c>ERROR_BOX</c> : liste pleine) : titre, texte.</summary>
        public event Action<string, string> ErrorBox;

        // ----- Envois (formats de dofus.aks.Friends et dofus.aks.Enemies) -----

        /// <summary><c>FL</c> ou <c>iL</c> ; le serveur répond par la liste (et <c>FS</c> si le personnage est marié).</summary>
        public Task<InteractionResult> RefreshAsync(FriendListKind kind) =>
            SendAsync(kind == FriendListKind.Enemies ? "iL" : "FL", "Liste des " + FriendsTexts.Title(kind).ToLowerInvariant() + " demandée.");

        /// <summary><c>FL</c> puis <c>iL</c> (un paquet par envoi).</summary>
        public async Task<InteractionResult> RefreshAllAsync()
        {
            InteractionResult first = await RefreshAsync(FriendListKind.Friends).ConfigureAwait(false);
            if (!first.Sent) return first;
            return await RefreshAsync(FriendListKind.Enemies).ConfigureAwait(false);
        }

        /// <summary>« Ajouter à mes amis » du menu d'un joueur et <c>/f A &lt;nom&gt;</c> : <c>FA&lt;nom&gt;</c>.</summary>
        public Task<InteractionResult> AddFriendAsync(string name) => AddAsync(FriendListKind.Friends, name, false);
        /// <summary>« Ajouter à mes ennemis » du menu d'un joueur : <c>iA&lt;nom&gt;</c>.</summary>
        public Task<InteractionResult> AddEnemyAsync(string name) => AddAsync(FriendListKind.Enemies, name, false);

        /// <summary>
        /// Ajout : <c>FA&lt;nom&gt;</c> / <c>iA&lt;nom&gt;</c> ; depuis la fenêtre (<paramref name="fromWindow"/>), comme le bouton
        /// « Ajouter » du client : <c>FA%&lt;nom&gt;</c> / <c>iA%&lt;nom&gt;</c> puis aussitôt <c>FL</c> / <c>iL</c>. StarLoco répond
        /// <c>FAK…</c> / <c>iAK…</c> ou une erreur (<c>FAEf/y/a</c>, <c>iAEA.</c>, <c>FDEf</c> pour un ennemi introuvable).
        /// Comme le client, seuls un nom vide et « * » sont refusés localement.
        /// </summary>
        public async Task<InteractionResult> AddAsync(FriendListKind kind, string name, bool fromWindow)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name == "*") return Refuse("Indiquez le nom du personnage à ajouter.");
            if (!IsSendable(name)) return Refuse("Nom de personnage invalide : « " + name + " ».");
            string prefix = kind == FriendListKind.Enemies ? "iA" : "FA";
            InteractionResult result = await SendAsync(prefix + (fromWindow ? "%" : string.Empty) + name,
                "Ajout de " + name + " à la liste des " + FriendsTexts.Title(kind).ToLowerInvariant() + " demandé.").ConfigureAwait(false);
            if (result.Sent && fromWindow) await SendRawAsync(kind == FriendListKind.Enemies ? "iL" : "FL").ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Retrait d'une ligne (bouton × de la ligne du client) : <c>FD*&lt;compte&gt;</c> / <c>iD*&lt;compte&gt;</c> si le compte est
        /// connu, sinon <c>FD&lt;nom&gt;</c> / <c>iD&lt;nom&gt;</c>. Le serveur répond <c>FDK</c> / <c>iDK</c> (puis le bot redemande la
        /// liste, comme le client) ou <c>FDEf</c>.
        /// </summary>
        public Task<InteractionResult> RemoveAsync(FriendListKind kind, FriendEntry entry)
        {
            if (entry == null) return Refused("Aucune ligne choisie.");
            return RemoveAsync(kind, entry.RemoveTarget, entry.Name);
        }

        /// <summary>Retrait par nom (<c>/f D &lt;nom&gt;</c>) ou par <c>*&lt;compte&gt;</c>.</summary>
        public Task<InteractionResult> RemoveAsync(FriendListKind kind, string target) => RemoveAsync(kind, target, target);

        private Task<InteractionResult> RemoveAsync(FriendListKind kind, string target, string shown)
        {
            target = (target ?? string.Empty).Trim();
            if (target.Length == 0 || target == "*") return Refused("Indiquez le nom du personnage à retirer.");
            if (!IsSendable(target)) return Refused("Nom de personnage invalide : « " + target + " ».");
            return SendAsync((kind == FriendListKind.Enemies ? "iD" : "FD") + target,
                "Retrait de " + shown + " de la liste des " + FriendsTexts.Title(kind).ToLowerInvariant() + " demandé.");
        }

        /// <summary>
        /// Case « M'avertir lors de la connexion de l'un de mes amis » : <c>FO+</c> / <c>FO-</c>. StarLoco ne répond que <c>BN</c> :
        /// l'état est posé dès l'envoi, comme le client (<c>aks_notify_on_friend_connexion</c>).
        /// </summary>
        public async Task<InteractionResult> SetWarnOnFriendLoginAsync(bool warn)
        {
            InteractionResult result = await SendAsync(warn ? "FO+" : "FO-", warn
                ? "Avertissement à la connexion des amis activé."
                : "Avertissement à la connexion des amis désactivé.").ConfigureAwait(false);
            if (result.Sent) { lock (sync) warnOnLogin = warn; RaiseChanged(); }
            return result;
        }

        /// <summary>« Rejoindre » de la fiche du conjoint : <c>FJS</c> (téléportation ; refusée en combat, comme le bouton du client).</summary>
        public Task<InteractionResult> JoinSpouseAsync()
        {
            SpouseInfo current = Spouse;
            if (current == null) return Refused("Le personnage n'a pas de conjoint connu (liste d'amis à recharger).");
            if (!current.IsConnected) return Refused(FriendsTexts.Gendered("SPOUSE_NOT_CONNECTED", "Non connecté", current.Sex));
            if (account?.IsFighting() == true) return Refused("Action impossible pendant un combat.");
            return SendAsync("FJS", "Téléportation auprès de " + current.Name + " demandée.");
        }

        /// <summary>
        /// « Suivre le déplacement… » / « Ne plus suivre le déplacement » de la fiche du conjoint : <c>FJC+</c> / <c>FJC-</c>.
        /// Le serveur pose la boussole (<c>IC&lt;x&gt;|&lt;y&gt;</c> / <c>IC|</c>, lue par le groupe) ; l'état « suivi » bascule
        /// à l'envoi comme dans le client.
        /// </summary>
        public async Task<InteractionResult> FollowSpouseAsync(bool follow)
        {
            SpouseInfo current = Spouse;
            if (current == null) return Refuse("Le personnage n'a pas de conjoint connu (liste d'amis à recharger).");
            if (!current.IsConnected) return Refuse(FriendsTexts.Gendered("SPOUSE_NOT_CONNECTED", "Non connecté", current.Sex));
            InteractionResult result = await SendAsync(follow ? "FJC+" : "FJC-", follow
                ? "Suivi des déplacements de " + current.Name + " demandé."
                : "Fin du suivi des déplacements de " + current.Name + " demandée.").ConfigureAwait(false);
            if (result.Sent)
            {
                lock (sync) if (spouse != null && spouse.Name == current.Name) spouse = spouse.WithFollow(follow);
                RaiseChanged();
            }
            return result;
        }

        // ----- Réceptions (FriendsFrame, fil réseau) -----

        /// <summary><c>FL|&lt;ligne&gt;|…</c> (<c>Friends.onFriendsList</c>) ; <c>FL</c> seul = liste vide.</summary>
        internal void OnFriendsListPacket(string message) => OnListPacket(FriendListKind.Friends, message);

        /// <summary><c>iL|&lt;ligne&gt;|…</c> (<c>Enemies.onEnemiesList</c>).</summary>
        internal void OnEnemiesListPacket(string message) => OnListPacket(FriendListKind.Enemies, message);

        private void OnListPacket(FriendListKind kind, string message)
        {
            // Le client lit substr(3) : le préfixe et le « | » qui précède la première ligne (Account.parseFriendList).
            string body = message.Length > 2 ? message.Substring(2) : string.Empty;
            if (body.StartsWith("|", StringComparison.Ordinal)) body = body.Substring(1);
            var parsed = new List<FriendEntry>();
            if (body.Length > 0)
                foreach (string line in body.Split('|'))
                {
                    if (FriendEntry.TryParse(line, out FriendEntry entry)) parsed.Add(entry);
                    else if (line.Length > 0) Debug("Ligne de liste illisible ignorée : " + Preview(line));
                }
            FriendEntry[] snapshot = parsed.ToArray();
            lock (sync)
            {
                if (kind == FriendListKind.Enemies) { enemies = snapshot; enemiesReceived = true; }
                else { friends = snapshot; friendsReceived = true; }
            }
            if (!WindowOpen) WriteListToChat(kind, snapshot);
            RaiseChanged();
        }

        /// <summary><c>FAK&lt;ligne&gt;</c> (ami ajouté) ou <c>FAE&lt;f|y|a|m&gt;</c> (<c>Friends.onAddFriend</c>).</summary>
        internal void OnFriendAddPacket(string message) => OnAddPacket(FriendListKind.Friends, message);

        /// <summary><c>iAK&lt;ligne&gt;</c> (ennemi ajouté, format partiel toléré) ou <c>iAE…</c> (<c>Enemies.onAddEnemy</c>).</summary>
        internal void OnEnemyAddPacket(string message) => OnAddPacket(FriendListKind.Enemies, message);

        private void OnAddPacket(FriendListKind kind, string message)
        {
            bool error = message.Length > 2 && message[2] == 'E';
            string body = message.Length > 3 ? message.Substring(3) : string.Empty;
            if (error) { AddError(kind, body); return; }
            bool parsed = kind == FriendListKind.Enemies ? FriendEntry.TryParseAddedEnemy(body, out FriendEntry entry) : FriendEntry.TryParse(body, out entry);
            if (!parsed)
            {
                Debug("Ajout illisible ignoré : " + Preview(message));
                return;
            }
            lock (sync)
            {
                FriendEntry[] current = kind == FriendListKind.Enemies ? enemies : friends;
                FriendEntry[] updated = current.Where(existing => !existing.SamePlayer(entry)).Concat(new[] { entry }).ToArray();
                if (kind == FriendListKind.Enemies) enemies = updated; else friends = updated;
            }
            Inform(kind == FriendListKind.Enemies
                ? FriendsTexts.Get("ADD_TO_ENEMY_LIST", "{0} a été ajouté à ta liste d'ennemis.", entry.Name)
                : FriendsTexts.Get("ADD_TO_FRIEND_LIST", "{0} a été ajouté à ta liste d'amis.", entry.Name));
        }

        private void AddError(FriendListKind kind, string code)
        {
            // StarLoco écrit « iAEA. » pour un ennemi déjà connu : la lettre est lue sans tenir compte de la casse (le client,
            // qui attend « a », n'afficherait rien).
            char reason = code.Length > 0 ? char.ToLowerInvariant(code[0]) : '\0';
            bool enemy = kind == FriendListKind.Enemies;
            switch (reason)
            {
                case 'f': Fail(FriendsTexts.Get("CANT_ADD_FRIEND_NOT_FOUND", "Impossible, ce perso ou compte n'existe pas ou n'est pas connecté.")); break;
                case 'y': Fail(enemy ? FriendsTexts.Get("CANT_ADD_YOU_AS_ENEMY", "Impossible de t'ajouter en ennemi.") : FriendsTexts.Get("CANT_ADD_YOU", "Impossible de t'ajouter en ami.")); break;
                case 'a': Fail(enemy ? FriendsTexts.Get("ALREADY_YOUR_ENEMY", "Déjà dans ta liste d'ennemis.") : FriendsTexts.Get("ALREADY_YOUR_FRIEND", "Déjà dans ta liste d'amis.")); break;
                case 'm':
                    string text = enemy ? FriendsTexts.Get("ENEMIES_LIST_FULL", "Ta liste d'ennemis est pleine.") : FriendsTexts.Get("FRIENDS_LIST_FULL", "Ta liste d'amis est pleine.");
                    Fail(text);
                    RaiseErrorBox(FriendsTexts.Title(kind), text);
                    break;
                default: Debug("Erreur d'ajout inconnue : " + Preview(code)); break;
            }
        }

        /// <summary><c>FDK</c> (retiré : le client redemande <c>FL</c>) ou <c>FDEf</c> (<c>Friends.onRemoveFriend</c>).</summary>
        internal Task OnFriendRemovePacket(string message) => OnRemovePacket(FriendListKind.Friends, message);

        /// <summary><c>iDK</c> (retiré : le client redemande <c>iL</c>) ou <c>iDE…</c> (<c>Enemies.onRemoveEnemy</c>).</summary>
        internal Task OnEnemyRemovePacket(string message) => OnRemovePacket(FriendListKind.Enemies, message);

        private Task OnRemovePacket(FriendListKind kind, string message)
        {
            bool error = message.Length > 2 && message[2] == 'E';
            if (error)
            {
                string code = message.Length > 3 ? message.Substring(3) : string.Empty;
                if (code.StartsWith("f", StringComparison.OrdinalIgnoreCase))
                    Fail(FriendsTexts.Get("CANT_ADD_FRIEND_NOT_FOUND", "Impossible, ce perso ou compte n'existe pas ou n'est pas connecté."));
                else Debug("Erreur de retrait inconnue : " + Preview(message));
                return Task.CompletedTask;
            }
            Inform(kind == FriendListKind.Enemies
                ? FriendsTexts.Get("REMOVE_ENEMY_OK", "L'ennemi a été effacé.")
                : FriendsTexts.Get("REMOVE_FRIEND_OK", "Tu viens de perdre un ami."));
            // StarLoco ne dit pas qui a été retiré : la liste est redemandée, comme getFriendsList / getEnemiesList du client.
            return SendRawAsync(kind == FriendListKind.Enemies ? "iL" : "FL");
        }

        /// <summary><c>FS&lt;nom|gfx|c1|c2|c3|carte|niveau|combat|suivi&gt;</c> (<c>Friends.onSpouse</c>).</summary>
        internal void OnSpousePacket(string message)
        {
            string body = message.Length > 2 ? message.Substring(2) : string.Empty;
            int sex = account?.Game?.character?.Sex ?? 0;
            SpouseInfo parsed = SpouseInfo.TryParse(body, sex, out SpouseInfo value) ? value : null;
            if (parsed == null) Debug("Conjoint introuvable (" + Preview(message) + ").");
            lock (sync)
            {
                // StarLoco n'envoie pas le suivi : celui demandé par FJC+ est gardé pour le même conjoint.
                if (parsed != null && spouse != null && spouse.IsFollowed && !parsed.IsFollowed && spouse.Name == parsed.Name && parsed.IsConnected)
                    parsed = parsed.WithFollow(true);
                spouse = parsed;
            }
            RaiseChanged();
        }

        /// <summary><c>FO+</c> / <c>FO-</c> : avertissement à la connexion d'un ami (envoyé par StarLoco à l'entrée en jeu).</summary>
        internal void OnNotifyPacket(string message)
        {
            char value = message.Length > 2 ? message[2] : '\0';
            if (value != '+' && value != '-') { Debug("Avertissement de connexion illisible : " + Preview(message)); return; }
            lock (sync) warnOnLogin = value == '+';
            RaiseChanged();
        }

        /// <summary>Oublie l'état de session (déconnexion, changement de personnage) sans rien envoyer.</summary>
        public void Clear()
        {
            lock (sync)
            {
                friends = new FriendEntry[0]; enemies = new FriendEntry[0];
                friendsReceived = enemiesReceived = false;
                spouse = null; warnOnLogin = null; lastMessage = string.Empty;
            }
            RaiseChanged();
        }

        // ----- Outils -----

        /// <summary>Liste écrite dans le chat fenêtre fermée, comme <c>onFriendsList</c> / <c>onEnemiesList</c> (<c>INFO_CHAT</c>).</summary>
        private void WriteListToChat(FriendListKind kind, IReadOnlyList<FriendEntry> list)
        {
            var lines = new List<string>();
            bool enemy = kind == FriendListKind.Enemies;
            if (list.Count == 0) lines.Add(enemy ? FriendsTexts.Get("EMPTY_ENEMY_LIST", "Ta liste d'ennemis est vide.") : FriendsTexts.Get("EMPTY_FRIEND_LIST", "Ta liste d'amis est vide."));
            else
            {
                lines.Add((enemy ? FriendsTexts.Get("YOUR_ENEMY_LIST", "Ta liste d'ennemis") : FriendsTexts.Get("YOUR_FRIEND_LIST", "Ta liste d'amis")) + " :");
                string level = FriendsTexts.Get("LEVEL", "Niveau");
                foreach (FriendEntry entry in list)
                    lines.Add(" - " + (entry.Account ?? entry.Name) + (entry.IsOnline
                        ? " (" + entry.Name + ") " + level + ":" + (entry.LevelText.Length > 0 ? entry.LevelText : "?") + ", " + FriendsTexts.State(entry.State)
                        : string.Empty));
            }
            foreach (string line in lines)
            {
                account?.Logger?.LogInfo(Reference, line);
                try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Info, line); }
                catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            }
        }

        private static bool IsSendable(string text) => text.IndexOf('|') < 0 && !text.Any(char.IsControl);

        private async Task<InteractionResult> SendAsync(string packet, string message)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error)
            {
                account?.Logger?.LogException(Reference, error);
                return Refuse("Envoi impossible : " + error.Message);
            }
            SetMessage(message);
            account?.Logger?.LogInfo(Reference, message);
            RaiseChanged();
            return new InteractionResult(true, message);
        }

        private async Task SendRawAsync(string packet)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return;
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
        }

        private InteractionResult Refuse(string message) { SetMessage(message); RaiseChanged(); return new InteractionResult(false, message); }
        private Task<InteractionResult> Refused(string message) => Task.FromResult(Refuse(message));
        private void SetMessage(string message) { lock (sync) lastMessage = message ?? string.Empty; }

        /// <summary>Information (<c>INFO_CHAT</c> du client) : journal, ligne locale du chat et <see cref="LastMessage"/>.</summary>
        private void Inform(string text)
        {
            SetMessage(text);
            account?.Logger?.LogInfo(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Info, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            RaiseChanged();
        }

        /// <summary>Erreur (<c>ERROR_CHAT</c> du client).</summary>
        private void Fail(string text)
        {
            SetMessage(text);
            account?.Logger?.LogError(Reference, text);
            try { account?.Game?.Chat?.AddLocal(ChatMessageKind.Error, text); }
            catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            RaiseChanged();
        }

        private void Debug(string text) => account?.Logger?.LogDebug(Reference, text);

        private static string Preview(string text) => text == null ? string.Empty : text.Length > 80 ? text.Substring(0, 80) + "…" : text;

        private void RaiseChanged() => Raise(Changed, handler => handler());
        private void RaiseErrorBox(string title, string text) => Raise(ErrorBox, handler => handler(title, text));

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError(Reference, "Un abonné des amis a échoué : " + error.Message); }
                    catch (Exception) { /* Journal fermé : la lecture des paquets continue. */ }
                }
            }
        }
    }
}
