using System;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>Résultat d'une demande manuelle : le paquet a-t-il été envoyé, et le message à présenter.</summary>
    public sealed class InteractionResult
    {
        public bool Sent { get; private set; }
        public string Message { get; private set; }
        internal InteractionResult(bool sent, string message) { Sent = sent; Message = message; }
    }

    /// <summary>
    /// Base des fenêtres ouvertes par le serveur (dialogue PNJ, zaaps, boutique). L'état local ne change
    /// qu'à la réception des paquets du serveur ; les envois reprennent les formats du client Dofus 1.34.
    /// </summary>
    public abstract class InteractionWindow
    {
        protected readonly Accounts.Accounts Account;
        public bool IsOpen { get; protected set; }
        /// <summary>Dernier message utile pour l'interface (confirmation, refus, erreur de lecture).</summary>
        public string LastMessage { get; protected set; } = string.Empty;
        public event Action Changed;

        protected InteractionWindow(Accounts.Accounts account) { Account = account; }
        protected abstract string Reference { get; }
        /// <summary>État de compte posé tant que la fenêtre est ouverte, pour bloquer déplacements et autres fenêtres.</summary>
        protected abstract AccountStates OpenState { get; }

        protected void Notify() { try { Changed?.Invoke(); } catch (Exception error) { Account?.Logger?.LogException(Reference, error); } }
        protected void Log(string message) { LastMessage = message; Account?.Logger?.LogInfo(Reference, message); }
        protected void LogError(string message) { LastMessage = message; Account?.Logger?.LogError(Reference, message); }
        protected static InteractionResult Refuse(string message) => new InteractionResult(false, message);

        /// <summary>Vérifie la connexion et l'état du personnage avant l'ouverture d'une fenêtre.</summary>
        protected InteractionResult CheckCanOpen()
        {
            if (Account?.Connexion == null || !Account.Connexion.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            if (Account.IsFighting()) return Refuse("Action impossible pendant un combat.");
            if (Account.IsMoving()) return Refuse("Attendez la fin du déplacement.");
            if (Account.IsGathering()) return Refuse("Attendez la fin de la récolte.");
            if (Account.Is_In_Dialog() || IsOpen) return Refuse("Une fenêtre de dialogue, d'échange ou de zaap est déjà ouverte.");
            return null;
        }

        protected async Task<InteractionResult> SendAsync(string packet, string message)
        {
            var connection = Account?.Connexion;
            if (connection == null || !connection.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            try
            {
                await connection.SendPacket(packet, true).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Account?.Logger?.LogException(Reference, error);
                return Refuse("Envoi impossible : " + error.Message);
            }
            LastMessage = message;
            return new InteractionResult(true, message);
        }

        protected void MarkOpen()
        {
            IsOpen = true;
            if (Account != null && !Account.IsFighting()) Account.AccountStates = OpenState;
        }

        protected void MarkClosed()
        {
            IsOpen = false;
            if (Account != null && Account.AccountStates == OpenState) Account.AccountStates = AccountStates.CONNECTED_INACTIVE;
        }

        /// <summary>Oublie la fenêtre sans envoyer de paquet (déconnexion, changement de personnage).</summary>
        public void Clear()
        {
            Reset();
            MarkClosed();
            LastMessage = string.Empty;
            Notify();
        }
        protected abstract void Reset();
    }

    /// <summary>Fenêtres de jeu du personnage : dialogue PNJ, zaaps, boutique PNJ, échanges, objets interactifs et zaapis.</summary>
    public sealed class InteractionsClass
    {
        /// <summary>Défis, agressions, combats de la carte et « qui est » (lot M4).</summary>
        public Actions.MapActions MapActions { get; private set; }
        public NpcDialog Npc { get; private set; }
        public ZaapDialog Zaap { get; private set; }
        public NpcShop Shop { get; private set; }
        /// <summary>Registre des échanges (<c>ECK</c> par type) ; les fenêtres d'échange y sont créées une fois par compte.</summary>
        public Exchanges.ExchangeRegistry Exchanges { get; private set; }
        /// <summary>Échange avec un joueur ou un PNJ (types 1 et 2), demandes <c>ERK</c> comprises (lot F4).</summary>
        public Exchanges.PlayerExchange Exchange { get; private set; }
        /// <summary>Objets interactifs : <c>GA500</c>, action 501, codes <c>K</c>, documents <c>d</c> (lot M3).</summary>
        public InteractiveActions Interactive { get; private set; }
        /// <summary>Coffre ou banque (type 5) (lot F4).</summary>
        public Exchanges.StorageExchange Storage { get; private set; }
        /// <summary>Zaapis : <c>Wc</c>, <c>Wu</c>, <c>Wv</c> (lot M3).</summary>
        public ZaapiDialog Zaapi { get; private set; }
        /// <summary>Groupe du personnage : invitations, membres, suivi, localisation (lot F1).</summary>
        public Groupes.PartyActions Party { get; private set; }
        /// <summary>Amis, ennemis et conjoint : <c>F…</c> et <c>i…</c> (lot F2).</summary>
        public Social.FriendsActions Friends { get; private set; }
        /// <summary>Maisons : <c>hP</c>, <c>hL</c>, <c>hCK</c>, <c>hSK</c>, <c>hG</c>, <c>hV</c> ; envois <c>hB</c>, <c>hS</c>, <c>hG±</c>, <c>hQ</c>, <c>GA507</c> (lot F8).</summary>
        public Habitat.HouseActions House { get; private set; }
        /// <summary>Magasin d'un marchand hors ligne (type 4), organisation du sien (type 6) et mode marchand <c>Eq</c>/<c>EQ</c> (lot F8).</summary>
        public Exchanges.MerchantExchange Merchant { get; private set; }
        /// <summary>Guilde du personnage : <c>g…</c>, percepteurs et collecte <c>ER8</c> (lot F3).</summary>
        public Guildes.GuildActions Guild { get; private set; }
        /// <summary>Hôtel de vente : achat (type 11, <c>EH…</c>) et vente (type 10, <c>EL</c>, <c>EMO±</c> avec prix) (lot F5).</summary>
        public Exchanges.AuctionHouse Auction { get; private set; }
        /// <summary>Alignement, ailes, conquête et prismes : <c>ZS</c>, <c>al</c>, <c>am</c>, <c>aM</c>, <c>GIP</c>, <c>C…</c>, <c>Wp</c>, <c>Ww</c> (lot F9).</summary>
        public Alignement.AlignmentActions Alignment { get; private set; }
        /// <summary>Métiers, options d'artisan, mode public, atelier (type 3) et livre des artisans (type 14) : <c>J…</c>, <c>Ec</c>, <c>EA</c>, <c>Ea</c>, <c>EJ</c>, <c>Ej</c>, <c>EW</c> (lot F6).</summary>
        public Jobs.JobsActions Jobs { get; private set; }

        internal InteractionsClass(Accounts.Accounts account)
        {
            MapActions = new Actions.MapActions(account);
            Npc = new NpcDialog(account);
            Zaap = new ZaapDialog(account);
            Exchanges = new Exchanges.ExchangeRegistry(account);
            Shop = Exchanges.Get<NpcShop>();
            Exchange = Exchanges.Get<Exchanges.PlayerExchange>();
            Interactive = new InteractiveActions(account);
            Storage = Exchanges.Get<Exchanges.StorageExchange>();
            Zaapi = new ZaapiDialog(account);
            Party = new Groupes.PartyActions(account);
            Friends = new Social.FriendsActions(account);
            House = new Habitat.HouseActions(account);
            Merchant = Exchanges.Get<Exchanges.MerchantExchange>();
            Guild = new Guildes.GuildActions(account);
            Auction = Exchanges.Get<Exchanges.AuctionHouse>();
            Alignment = new Alignement.AlignmentActions(account);
            Jobs = new Jobs.JobsActions(account, Exchanges);
        }

        public void Clear() { MapActions.Clear(); Npc.Clear(); Zaap.Clear(); Exchanges.Clear(); Interactive.Clear(); Zaapi.Clear(); Party.Clear(); Friends.Clear(); House.Clear(); Guild.Clear(); Alignment.Clear(); Jobs.Clear(); }
    }
}
