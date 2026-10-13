using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Interactions;

namespace Tool_BotProtocol.Game.Actions
{
    /// <summary>
    /// Demande de mariage adressée au personnage : <c>GA;617;&lt;acteur&gt;;&lt;interrogé&gt;,&lt;conjoint&gt;,&lt;parlant&gt;</c>
    /// (StarLoco <c>World.priestRequest</c> : acteur = interrogé, parlant = -1, envoyé au seul joueur interrogé).
    /// </summary>
    public sealed class MarriageRequest
    {
        internal MarriageRequest(long askedId, string askedName, long partnerId, string partnerName, long reference)
        {
            AskedId = askedId; AskedName = askedName; PartnerId = partnerId; PartnerName = partnerName; Reference = reference;
        }
        public long AskedId { get; }
        public string AskedName { get; }
        public long PartnerId { get; }
        public string PartnerName { get; }
        /// <summary>Acteur du paquet <c>GA;617</c>, renvoyé tel quel dans <c>GA618</c> / <c>GA619</c> (<c>refID</c> du client).</summary>
        public long Reference { get; }
        /// <summary>Texte de la boîte <c>CAUTION_YESNO</c> du client (<c>A_ASK_MARRIAGE_B</c>).</summary>
        public string Text => MapActionTexts.AskMarriage(AskedName, PartnerName);
    }

    /// <summary>
    /// Actions sur son propre personnage (menu <c>getPlayerPopupMenu</c> du client quand l'acteur est soi) et mariage :
    /// « Libérer son âme » (<c>GF</c>, seulement pour une tombe : StarLoco change en fantôme tout personnage qui l'envoie),
    /// « Changer son orientation » (<c>eD&lt;0..7&gt;</c>, hors combat et hors tombe ; quatre diagonales sans le bit 8192 de
    /// <c>AR</c>), consentement au mariage (<c>GA;617</c> → <c>GA618&lt;ref&gt;</c> / <c>GA619&lt;ref&gt;</c>) et annonces
    /// <c>GA;618</c> / <c>GA;619</c> que le client sait lire (StarLoco ne les envoie pas : il répond par <c>cMK</c>).
    /// </summary>
    public sealed partial class MapActions
    {
        public const int AskMarriageAction = 617;
        public const int AcceptMarriageAction = 618;
        public const int RefuseMarriageAction = 619;
        /// <summary>Les quatre diagonales du sélecteur du client sans <c>canMoveInAllDirections</c> (1, 3, 5, 7).</summary>
        private static readonly IReadOnlyList<int> DiagonalDirections = Array.AsReadOnly(new[] { 1, 3, 5, 7 });
        private static readonly IReadOnlyList<int> EveryDirection = Array.AsReadOnly(new[] { 0, 1, 2, 3, 4, 5, 6, 7 });

        private MarriageRequest pendingMarriage;

        /// <summary>Mariage proposé au personnage (<c>GA;617</c> dont il est l'interrogé) : le client ouvre <c>CAUTION_YESNO</c>.</summary>
        public event Action<MarriageRequest> MarriageAsked;
        /// <summary>Demande de mariage close : réponse envoyée, annonce <c>GA;618</c>/<c>GA;619</c>, ou session effacée.</summary>
        public event Action<MarriageRequest> MarriageClosed;

        /// <summary>Tombe (bit 128 du sprite <c>GM</c> du personnage, voir <see cref="Session.GameSession.IsTomb"/>).</summary>
        public bool IsTomb => Account?.Game?.Session?.IsTomb ?? false;
        /// <summary>Fantôme ralenti (bit 32 du sprite), lu comme la commande <c>/release</c> du client.</summary>
        public bool IsGhost => Account?.Game?.Session?.IsGhost ?? false;
        public MarriageRequest PendingMarriage { get { lock (sync) return pendingMarriage; } }

        /// <summary>« Libérer son âme » visible : <c>Player.data.isTomb</c>.</summary>
        public bool CanFreeSoul => IsTomb;

        /// <summary>« Changer son orientation » visible : ni tombe ni combat (<c>getPlayerPopupMenu</c>).</summary>
        public bool CanChangeDirection => !IsTomb && !InFight && Account?.Game?.Map?.Self != null;

        /// <summary>
        /// Orientations du sélecteur (<c>DirectionChooser</c>) : les huit si <c>Player.canMoveInAllDirections</c> (bit 8192 de
        /// <c>AR</c>, posé par <c>AR6bk</c>), sinon les quatre diagonales (après <c>AR3K</c> ou avant tout <c>AR</c>).
        /// </summary>
        public IReadOnlyList<int> AllowedDirections =>
            Account?.Game?.Session?.CanMoveInAllDirections == true ? EveryDirection : DiagonalDirections;

        private bool InFight => (Account?.IsFighting() ?? false) || (Account?.Game?.Fight?.IsInFight ?? false);

        /// <summary>
        /// « Libérer son âme » : <c>GF</c> (<c>Game.freeMySoul</c>). StarLoco téléporte alors le fantôme (gfx 8004) au
        /// cimetière de la zone et envoie les phénix (<c>IH</c>) ; en mode héroïque il répond <c>GO</c>. Refusé localement si le
        /// personnage n'est pas une tombe, avec les messages de la commande <c>/release</c> du client.
        /// </summary>
        public Task<InteractionResult> FreeSoulAsync()
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return Task.FromResult(refused);
            if (!IsTomb) return Task.FromResult(Refuse(IsGhost ? MapActionTexts.AlreadyGhost : MapActionTexts.NotDead));
            return SendAsync("GF", "Âme libérée (GF) : le fantôme renaît au cimetière ; une statue de phénix le ramène à la vie.");
        }

        /// <summary>
        /// Oriente le personnage : <c>eD&lt;direction&gt;</c> (<c>Emotes.setDirection</c>). Le serveur répond à toute la carte
        /// <c>eD&lt;id&gt;|&lt;direction&gt;</c>, que <c>MapFrame</c> applique au sprite. StarLoco ignore la demande en combat,
        /// pour une tombe et hors de 0 à 7 : le bot la refuse avant, ainsi qu'une orientation absente du sélecteur.
        /// </summary>
        public Task<InteractionResult> SetDirectionAsync(int direction)
        {
            InteractionResult refused = CheckReady();
            if (refused != null) return Task.FromResult(refused);
            if (direction < 0 || direction > 7) return Task.FromResult(Refuse("Orientation inconnue : " + direction + " (0 à 7)."));
            if (InFight) return Task.FromResult(Refuse("L'orientation ne se change pas en combat."));
            if (IsTomb) return Task.FromResult(Refuse("Une tombe ne peut pas changer d'orientation."));
            if (Account.Game.Map.Self == null) return Task.FromResult(Refuse("Le personnage n'est pas encore sur la carte."));
            if (!AllowedDirections.Contains(direction))
                return Task.FromResult(Refuse("Orientation " + direction + " indisponible : seules les diagonales sont permises (bit 8192 de AR absent)."));
            string value = direction.ToString(CultureInfo.InvariantCulture);
            return SendAsync("eD" + value, "Orientation demandée : " + DirectionName(direction) + " (eD" + value + ").");
        }

        /// <summary>Nom d'une orientation du client (0 = droite, puis sens horaire).</summary>
        public static string DirectionName(int direction)
        {
            switch (direction)
            {
                case 0: return "droite";
                case 1: return "bas à droite";
                case 2: return "bas";
                case 3: return "bas à gauche";
                case 4: return "gauche";
                case 5: return "haut à gauche";
                case 6: return "haut";
                case 7: return "haut à droite";
                default: return "inconnue";
            }
        }

        /// <summary>« Oui » au mariage : <c>GA618&lt;ref&gt;</c> (<c>GameActions.acceptMarriage</c>).</summary>
        public Task<InteractionResult> AcceptMarriageAsync() => AnswerMarriageAsync(AcceptMarriageAction, "Mariage accepté");

        /// <summary>« Non » au mariage : <c>GA619&lt;ref&gt;</c> (<c>GameActions.refuseMarriage</c>).</summary>
        public Task<InteractionResult> RefuseMarriageAsync() => AnswerMarriageAsync(RefuseMarriageAction, "Mariage refusé");

        private async Task<InteractionResult> AnswerMarriageAsync(int action, string label)
        {
            MarriageRequest current = PendingMarriage;
            if (current == null) return Refuse("Aucune demande de mariage n'attend de réponse.");
            InteractionResult refused = CheckConnected();
            if (refused != null) return refused;
            string packet = "GA" + action.ToString(CultureInfo.InvariantCulture) + Id(current.Reference);
            InteractionResult result = await SendAsync(packet, label + " (" + packet + ") : le prêtre répond dans la discussion.").ConfigureAwait(false);
            if (result.Sent) CloseMarriage(current);
            return result;
        }

        /// <summary><c>GA;617;&lt;acteur&gt;;&lt;interrogé&gt;,&lt;conjoint&gt;,&lt;parlant&gt;</c> : bulle du prêtre, boîte pour l'interrogé.</summary>
        internal void OnMarriageAsked(GameActionPacket packet)
        {
            if (!TryMarriagePair(packet, out long asked, out long partner)) return;
            string askedName = NameOf(asked) ?? "#" + Id(asked), partnerName = NameOf(partner) ?? "#" + Id(partner);
            Announce(MapActionNoticeKind.Info, MapActionTexts.AskMarriage(askedName, partnerName));
            if (!IsSelf(asked)) return;
            if (!packet.ActorId.HasValue)
            {
                LogError("Demande de mariage sans acteur ignorée : " + Shorten(packet.Raw));
                return;
            }
            var request = new MarriageRequest(asked, askedName, partner, partnerName, packet.ActorId.Value);
            MarriageRequest previous;
            lock (sync) { previous = pendingMarriage; pendingMarriage = request; }
            if (previous != null) Raise(() => MarriageClosed?.Invoke(previous));
            Raise(() => MarriageAsked?.Invoke(request));
            Notify();
        }

        /// <summary><c>GA;618</c> / <c>GA;619</c> : bulles <c>A_MARRIED_B</c> / <c>A_NOT_MARRIED_B</c> du client.</summary>
        internal void OnMarriageResult(GameActionPacket packet, bool married)
        {
            if (!TryMarriagePair(packet, out long first, out long second)) return;
            string a = NameOf(first) ?? "#" + Id(first), b = NameOf(second) ?? "#" + Id(second);
            Announce(MapActionNoticeKind.Info, married ? MapActionTexts.Married(a, b) : MapActionTexts.NotMarried(a, b));
            MarriageRequest current = PendingMarriage;
            if (current != null && (IsSelf(first) || IsSelf(second))) CloseMarriage(current);
        }

        private void CloseMarriage(MarriageRequest request)
        {
            bool closed;
            lock (sync)
            {
                closed = ReferenceEquals(pendingMarriage, request);
                if (closed) pendingMarriage = null;
            }
            if (!closed) return;
            Raise(() => MarriageClosed?.Invoke(request));
            Notify();
        }

        private bool TryMarriagePair(GameActionPacket packet, out long first, out long second)
        {
            first = second = 0;
            string[] fields = (packet?.Parameters ?? string.Empty).Split(',');
            if (fields.Length >= 2
                && long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out first)
                && long.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out second)) return true;
            LogError("Action de mariage illisible ignorée : " + Shorten(packet?.Raw));
            return false;
        }

        [GameActionHandler(AskMarriageAction)]
        private static void MarriageAskedAction(GameActionContext context) => Of(context)?.OnMarriageAsked(context.Packet);

        [GameActionHandler(AcceptMarriageAction)]
        private static void MarriedAction(GameActionContext context) => Of(context)?.OnMarriageResult(context.Packet, true);

        [GameActionHandler(RefuseMarriageAction)]
        private static void NotMarriedAction(GameActionContext context) => Of(context)?.OnMarriageResult(context.Packet, false);

        private static MapActions Of(GameActionContext context) => context?.Account?.Game?.Interactions?.MapActions;
    }
}
