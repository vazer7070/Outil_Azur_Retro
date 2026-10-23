using System;

namespace Tool_BotProtocol.Game.Perso
{
    /// <summary>
    /// Restrictions du sprite d'un joueur (champ 18 de <c>GM</c>), masques lus par le client 1.34 (<c>parseInt(p, 36)</c>,
    /// accesseurs <c>canBeAssault</c>… <c>isTomb</c>, <c>isAdminSonicSpeed</c> du sprite). Un bit posé signifie que la
    /// restriction s'applique. StarLoco écrit dans ce champ <c>(tombe ? "-1" : "") + vitesse</c> : « -10 » pour une tombe
    /// (-36 : bit 128 posé), « -40 » pour un fantôme (-144 : bit 32, ralenti), « 0 » pour un personnage vivant.
    /// <c>AR</c> (restrictions du joueur, <c>dofus.datacenter.Player</c>) utilise d'autres sens pour les mêmes bits
    /// (128 = <c>cantInteractWithTaxCollector</c>, 8192 = <c>canMoveInAllDirections</c>) : <c>AR3K</c> n'annonce pas une tombe.
    /// </summary>
    [Flags]
    public enum PlayerRestrictions
    {
        None = 0,
        CannotBeAssaulted = 1,
        CannotBeChallenged = 2,
        CannotExchange = 4,
        CannotBeAttacked = 8,
        ForceWalk = 16,
        Slow = 32,
        CannotSwitchToCreatureMode = 64,
        Tomb = 128,
        AdminSonicSpeed = 256,
    }

    public static class PlayerRestrictionsParser
    {
        /// <summary>Les neuf bits que le client sait lire ; les autres (8192 chez StarLoco) n'ont aucun effet visible.</summary>
        public const int KnownMask = 511;
        private const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";

        /// <summary>
        /// Lit une valeur en base 36 sans tenir compte de la casse (StarLoco envoie <c>6bk</c> ou <c>6bK</c>), avec un signe de
        /// tête facultatif comme <c>parseInt</c> du client (StarLoco écrit « -10 » dans le <c>GM</c> d'une tombe) ; une valeur
        /// négative garde les bits de son complément à deux, comme l'opérateur <c>&amp;</c> du client.
        /// Renvoie false, sans exception, pour une valeur vide, un caractère hors base 36 ou un dépassement.
        /// </summary>
        public static bool TryParse(string base36, out int raw, out PlayerRestrictions restrictions)
        {
            raw = 0;
            restrictions = PlayerRestrictions.None;
            if (string.IsNullOrEmpty(base36)) return false;
            bool negative = base36[0] == '-';
            int start = negative || base36[0] == '+' ? 1 : 0;
            if (base36.Length == start || base36.Length - start > 7) return false;
            long value = 0;
            for (int i = start; i < base36.Length; i++)
            {
                int digit = Digits.IndexOf(char.ToLowerInvariant(base36[i]));
                if (digit < 0) return false;
                value = value * 36 + digit;
            }
            if (negative) value = -value;
            if (value > int.MaxValue || value < int.MinValue) return false;
            raw = (int)value;
            restrictions = (PlayerRestrictions)(raw & KnownMask);
            return true;
        }
    }
}
