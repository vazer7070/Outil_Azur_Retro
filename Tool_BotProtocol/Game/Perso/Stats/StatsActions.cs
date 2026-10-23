using System;
using System.Globalization;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Interactions;

namespace Tool_BotProtocol.Game.Perso.Stats
{
    /// <summary>
    /// Boutons « + » de la fiche des caractéristiques (<c>StatsJob</c> du client 1.34) : un clic envoie <c>AB&lt;code&gt;</c>
    /// tout de suite, sans attente artificielle ; StarLoco répond par un nouveau <c>As</c> (ou ne répond rien si le
    /// capital ne suffit pas). Le client n'envoie jamais la forme <c>AB&lt;code&gt;;&lt;nombre&gt;</c>, le bot non plus.
    /// </summary>
    public sealed class StatsActions
    {
        private const string Reference = "CARACTÉRISTIQUES";
        private readonly Accounts.Accounts account;
        private readonly CharacterClass character;

        /// <summary>Dernière demande envoyée, ou raison du dernier refus local.</summary>
        public string LastMessage { get; private set; } = string.Empty;

        internal StatsActions(Accounts.Accounts account, CharacterClass character)
        {
            this.account = account;
            this.character = character;
        }

        /// <summary>Coût d'un point pour la classe du personnage, d'après la fiche reçue en dernier.</summary>
        public BoostCost CostOf(BoostableStat stat) => (character.stats ?? new CharacterStats()).BoostCost(character.Race_ID, stat);

        /// <summary>Le bouton « + » est montré quand le capital couvre le coût, comme <c>StatsJob.updateBoostButtons</c>.</summary>
        public bool IsAffordable(BoostableStat stat) => StatsCodes.IsDefined(stat) && CostOf(stat).Cost <= character.Carac_Points;

        /// <summary>Raison qui empêche d'augmenter la caractéristique (<c>Player.canBoost</c>), ou null.</summary>
        public string CannotBoost(BoostableStat stat)
        {
            if (!StatsCodes.IsDefined(stat)) return "Caractéristique inconnue.";
            if (account?.Connexion == null || !account.Connexion.IsConnected()) return "Connectez le personnage avant cette action.";
            if (character.id == 0 || character.stats == null || character.stats.FieldCount == 0) return "La fiche du personnage n'a pas encore été reçue.";
            if (account.IsFighting() || account.Game?.Fight?.IsInFight == true) return "Action impossible pendant un combat.";
            BoostCost cost = CostOf(stat);
            if (cost.Cost > character.Carac_Points)
                return "Capital insuffisant : " + cost.Cost.ToString(CultureInfo.InvariantCulture) + " point(s) requis, "
                    + character.Carac_Points.ToString(CultureInfo.InvariantCulture) + " disponible(s).";
            return null;
        }

        public bool CanBoost(BoostableStat stat) => CannotBoost(stat) == null;

        /// <summary>Envoie <c>AB&lt;code&gt;</c> ; la fiche se met à jour à la réception du <c>As</c> du serveur.</summary>
        public async Task<InteractionResult> BoostAsync(BoostableStat stat)
        {
            string refusal = CannotBoost(stat);
            if (refusal != null)
            {
                LastMessage = refusal;
                return new InteractionResult(false, refusal);
            }
            string packet = "AB" + ((byte)stat).ToString(CultureInfo.InvariantCulture);
            try
            {
                await account.Connexion.SendPacket(packet, true).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                account.Logger?.LogException(Reference, error);
                LastMessage = "Envoi impossible : " + error.Message;
                return new InteractionResult(false, LastMessage);
            }
            LastMessage = "Point de capital demandé en " + Name(stat) + ".";
            account.Logger?.LogInfo(Reference, LastMessage);
            return new InteractionResult(true, LastMessage);
        }

        public static string Name(BoostableStat stat)
        {
            switch (stat)
            {
                case BoostableStat.Force: return "force";
                case BoostableStat.Vitalite: return "vitalité";
                case BoostableStat.Sagesse: return "sagesse";
                case BoostableStat.Chance: return "chance";
                case BoostableStat.Agilite: return "agilité";
                case BoostableStat.Intelligence: return "intelligence";
                default: return "caractéristique " + ((byte)stat).ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
