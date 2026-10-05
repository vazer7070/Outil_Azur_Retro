using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>
    /// Instantané de l'ordre des tours d'un combat (<c>Timeline</c> du client 1.34) : ordre reçu par <c>GTL</c>,
    /// combattant dont c'est le tour (<c>GTS</c>), dernier tour terminé (<c>GTF</c>) et dernier <c>GTR</c>.
    /// Les valeurs sont copiées : l'instantané ne change plus après sa lecture.
    /// </summary>
    public sealed class FightTimeline
    {
        internal FightTimeline(IEnumerable<int> order, int currentActorId, int lastActorId, int readyActorId,
            int turnDurationMilliseconds, DateTime turnStartedUtc, int? tableTurn)
        {
            Order = (order ?? Enumerable.Empty<int>()).ToArray();
            CurrentActorId = currentActorId;
            LastActorId = lastActorId;
            ReadyActorId = readyActorId;
            TurnDurationMilliseconds = turnDurationMilliseconds;
            TurnStartedUtc = turnStartedUtc;
            TableTurn = tableTurn;
        }

        /// <summary>Identifiants des combattants vivants dans l'ordre de jeu (<c>GTL|id|id…</c>, morts exclus par StarLoco).</summary>
        public IReadOnlyList<int> Order { get; }
        /// <summary>Combattant dont c'est le tour (<c>GTS</c>) ; 0 entre <c>GTF</c> et le <c>GTS</c> suivant.</summary>
        public int CurrentActorId { get; }
        /// <summary>Combattant dont le tour vient de se terminer (<c>GTF</c>) ; ses effets décroissent au <c>GTS</c> suivant.</summary>
        public int LastActorId { get; }
        /// <summary>Dernier combattant annoncé par <c>GTR</c> (le client répond <c>GT</c>).</summary>
        public int ReadyActorId { get; }
        /// <summary>Durée du tour annoncée par <c>GTS</c>, en millisecondes.</summary>
        public int TurnDurationMilliseconds { get; }
        /// <summary>Heure locale (UTC) de réception du <c>GTS</c> courant ; <see cref="DateTime.MinValue"/> hors tour.</summary>
        public DateTime TurnStartedUtc { get; }
        /// <summary>Troisième champ de <c>GTS</c> lu par le client (<c>currentTableTurn</c>) ; null avec StarLoco, qui n'en envoie que deux.</summary>
        public int? TableTurn { get; }

        /// <summary>Position d'un combattant dans <see cref="Order"/>, ou -1.</summary>
        public int IndexOf(int fighterId)
        {
            for (int index = 0; index < Order.Count; index++) if (Order[index] == fighterId) return index;
            return -1;
        }

        /// <summary>Temps restant du tour courant à l'instant donné (0 hors tour ou une fois écoulé).</summary>
        public int RemainingMilliseconds(DateTime nowUtc)
        {
            if (CurrentActorId == 0 || TurnStartedUtc == DateTime.MinValue || TurnDurationMilliseconds <= 0) return 0;
            double left = TurnDurationMilliseconds - (nowUtc - TurnStartedUtc).TotalMilliseconds;
            return left <= 0 ? 0 : (int)Math.Ceiling(left);
        }
    }

    /// <summary>
    /// Lecture des paquets de tour, d'après <c>Game.onTurnlist/onTurnStart/onTurnFinish/onTurnReady</c> du client 1.34
    /// et <c>SocketManager</c>/<c>Fight.getGTL</c> de StarLoco. Aucune méthode ne lève d'exception sur un paquet mal formé.
    /// </summary>
    public static class FightTurnPackets
    {
        /// <summary>
        /// <c>GTL|id|id…</c> (StarLoco : un <c>|</c> avant chaque identifiant ; le client lit à partir du 5ᵉ caractère).
        /// La forme sans premier <c>|</c> (<c>GTL1|2</c>) est aussi acceptée. <paramref name="payload"/> est le paquet sans « GTL ».
        /// Renvoie faux si une entrée n'est pas un identifiant ; une liste vide est valide (tous les combattants morts).
        /// </summary>
        public static bool TryParseTurnList(string payload, out List<int> order)
        {
            order = new List<int>();
            if (payload == null) return false;
            string body = payload.StartsWith("|", StringComparison.Ordinal) ? payload.Substring(1) : payload;
            if (body.Length == 0) return true;
            foreach (string entry in body.Split('|'))
            {
                if (entry.Length == 0) continue;
                if (!int.TryParse(entry, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) { order.Clear(); return false; }
                if (!order.Contains(id)) order.Add(id);
            }
            return true;
        }

        /// <summary>
        /// <c>GTS&lt;id&gt;|&lt;durée ms&gt;[|&lt;tour&gt;]</c> : StarLoco n'envoie que deux champs (matrice §2 n° 16) ;
        /// le troisième, lu par le client, est facultatif. <paramref name="payload"/> est le paquet sans « GTS ».
        /// </summary>
        public static bool TryParseTurnStart(string payload, out int actorId, out int durationMilliseconds, out int? tableTurn)
        {
            actorId = 0; durationMilliseconds = 0; tableTurn = null;
            if (string.IsNullOrEmpty(payload)) return false;
            string[] fields = payload.Split('|');
            if (fields.Length < 2 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out actorId)
                || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out durationMilliseconds) || durationMilliseconds < 0)
            {
                actorId = 0; durationMilliseconds = 0;
                return false;
            }
            if (fields.Length > 2 && int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int turn)) tableTurn = turn;
            return true;
        }

        /// <summary><c>GTF&lt;id&gt;</c> et <c>GTR&lt;id&gt;</c> : un seul identifiant après le préfixe.</summary>
        public static bool TryParseActor(string payload, out int actorId)
        {
            actorId = 0;
            return !string.IsNullOrEmpty(payload) && int.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out actorId);
        }
    }
}
