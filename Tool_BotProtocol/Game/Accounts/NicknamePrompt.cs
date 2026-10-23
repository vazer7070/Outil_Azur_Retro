using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Network;

namespace Tool_BotProtocol.Game.Accounts
{
    /// <summary>
    /// Socle commun des deux saisies de nom que le serveur demande par <c>AlEr</c> / <c>AlEs</c> et lit en ligne brute (le nom
    /// lui-même, sans préfixe, comme <c>Account.setNickName</c> du client 1.34) : le pseudo d'un compte neuf pendant la connexion
    /// (<see cref="NicknameChoice"/>, lot X3) et le nom du personnage en jeu après la potion de changement de nom
    /// (<see cref="Session.NameChange"/>, lot X32). Une seule proposition part à la fois : le serveur lirait une deuxième ligne
    /// comme la proposition suivante. Les événements sont levés sur le fil réseau.
    /// </summary>
    public abstract class NicknamePrompt
    {
        private readonly object sync = new object();
        private NicknameRequest pending;
        private string lastSent;
        private int attempts;

        internal NicknamePrompt(Accounts account) { Account = account; }

        protected Accounts Account { get; }

        /// <summary>Levé (fil réseau) à chaque demande du serveur : première demande ou refus du nom envoyé.</summary>
        public event Action<NicknameRequest> Requested;
        /// <summary>Levé (fil réseau) quand le serveur a retenu le nom envoyé.</summary>
        public event Action<string> Accepted;

        /// <summary>Demande en cours, ou <c>null</c>.</summary>
        public NicknameRequest Pending { get { lock (sync) return pending; } }
        public bool IsPending => Pending != null;
        /// <summary>Dernier nom envoyé au serveur, ou <c>null</c>.</summary>
        public string LastSent { get { lock (sync) return lastSent; } }

        /// <summary>Catégorie des lignes du journal.</summary>
        protected abstract string LogCategory { get; }
        /// <summary>Ce qui est saisi, en minuscules (« pseudo », « nom »).</summary>
        protected abstract string Subject { get; }
        /// <summary>Raison affichée sous la saisie quand le serveur redemande après un envoi, ou <c>null</c>.</summary>
        protected abstract string RefusalReason(bool alreadyUsed);
        /// <summary>Ligne du journal à la première demande du serveur.</summary>
        protected abstract string FirstRequestMessage { get; }
        /// <summary>Ligne du journal quand le serveur retient le nom.</summary>
        protected abstract string AcceptedMessage(string name);
        /// <summary>Appelé à chaque demande ouverte, avant les abonnés (état de connexion, par exemple).</summary>
        protected virtual void OnRequestOpened(NicknameRequest request) { }

        /// <summary>« Annuler » : déconnexion pendant la connexion, simple fermeture en jeu.</summary>
        public abstract void Cancel();

        /// <summary>
        /// Envoie le nom saisi, en ligne brute. Refus locaux du client, sans paquet : moins de trois caractères, nom égal au nom du
        /// compte (<c>NICKNAME_EQUALS_LOGIN</c>, sans distinction de casse) ; le bot refuse aussi un nom fait d'espaces et une ligne
        /// qui ne tiendrait pas dans un seul paquet (voir <see cref="Validate"/>).
        /// </summary>
        public async Task<InteractionResult> ChooseAsync(string nickname)
        {
            if (!IsPending) return new InteractionResult(false, "Le serveur ne demande pas de " + Subject + ".");
            string refusal = Validate(nickname);
            if (refusal != null) return new InteractionResult(false, refusal);
            TcpClient connection = Account.Connexion;
            if (connection == null || !connection.IsConnected()) return new InteractionResult(false, "La connexion au serveur est fermée.");
            lock (sync)
            {
                if (lastSent != null) return new InteractionResult(false, Capital(Subject) + " « " + lastSent + " » déjà envoyé : réponse du serveur en attente.");
                lastSent = nickname;
            }
            try { await connection.SendPacket(nickname).ConfigureAwait(false); }
            catch
            {
                lock (sync) if (lastSent == nickname) lastSent = null;
                throw;
            }
            Account.Logger?.LogInfo(LogCategory, Capital(Subject) + " « " + nickname + " » envoyé au serveur.");
            return new InteractionResult(true, Capital(Subject) + " « " + nickname + " » envoyé : réponse du serveur en attente.");
        }

        /// <summary>
        /// Contrôles locaux de <see cref="ChooseAsync"/>, faits par le client au premier « OK » avant la confirmation : texte du
        /// refus, ou <c>null</c> si le nom peut être proposé au serveur (qui garde ses propres règles).
        /// </summary>
        public virtual string Validate(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname) || nickname.Length < 3) return ConnectionTexts.NicknameTooShort;
            string login = Account.accountConfig?.Account ?? string.Empty;
            if (string.Equals(nickname, login, StringComparison.OrdinalIgnoreCase)) return ConnectionTexts.NicknameEqualsLogin;
            string violation = TcpClient.SinglePacketViolation(nickname);
            return violation == null ? null : Capital(Subject) + " refusé : " + violation + ".";
        }

        /// <summary>
        /// <c>AlEr</c> (première demande, ou nouvelle demande après un envoi) et <c>AlEs</c> (nom pris ou interdit). Faux, sans rien
        /// changer, quand personne n'est abonné à <see cref="Requested"/>.
        /// </summary>
        internal bool OnRequested(bool alreadyUsed)
        {
            if (Requested == null) return false;
            NicknameRequest request;
            lock (sync)
            {
                string refused = lastSent;
                request = new NicknameRequest(++attempts, refused, refused == null ? null : RefusalReason(alreadyUsed));
                pending = request;
                lastSent = null;
            }
            OnRequestOpened(request);
            Account.Logger?.LogInfo(LogCategory, request.Refused == null ? FirstRequestMessage
                : request.Error == null ? "Le serveur redemande un " + Subject + " après « " + request.Refused + " »."
                : Capital(Subject) + " « " + request.Refused + " » refusé par le serveur : " + request.Error + ".");
            Raise(Requested, request);
            return true;
        }

        /// <summary>Le serveur a retenu le nom : la demande en cours est close et <see cref="Accepted"/> levé.</summary>
        internal void OnAccepted(string nickname)
        {
            lock (sync)
            {
                if (pending == null) return;
                pending = null;
                lastSent = null;
            }
            Account.Logger?.LogInfo(LogCategory, AcceptedMessage(nickname ?? string.Empty));
            Raise(Accepted, nickname ?? string.Empty);
        }

        /// <summary>Ferme la demande en cours sans rien envoyer ni lever ; vrai si une demande était ouverte.</summary>
        protected bool CloseRequest()
        {
            lock (sync)
            {
                bool wasPending = pending != null;
                pending = null;
                lastSent = null;
                return wasPending;
            }
        }

        /// <summary>Déconnexion ou fin de partie : plus rien n'est attendu.</summary>
        internal void Reset()
        {
            lock (sync) { pending = null; lastSent = null; attempts = 0; }
        }

        protected void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null) return;
            foreach (Action<T> single in handler.GetInvocationList())
            {
                try { single(value); }
                catch (Exception error) { Account.Logger?.LogException(LogCategory, error); }
            }
        }

        private static string Capital(string word) => string.IsNullOrEmpty(word) ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
    }
}
