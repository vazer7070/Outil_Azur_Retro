using System;

namespace Tool_BotProtocol.Game.Perso
{
    /// <summary>
    /// Restrictions d'un joueur, masques lus par le client 1.34 (<c>Account.onRestrictions</c> : <c>parseInt(p, 36)</c>,
    /// accesseurs <c>canBeAssault</c>… <c>isAdminSonicSpeed</c>). Un bit posé signifie que la restriction s'applique.
    /// StarLoco envoie <c>AR6bk</c> (8192, aucun de ces bits : aucune restriction visible) et <c>AR3K</c> (128, tombe).
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
        /// Lit une valeur en base 36 sans tenir compte de la casse (StarLoco envoie <c>6bk</c> ou <c>6bK</c>).
        /// Renvoie false, sans exception, pour une valeur vide, un caractère hors base 36 ou un dépassement.
        /// </summary>
        public static bool TryParse(string base36, out int raw, out PlayerRestrictions restrictions)
        {
            raw = 0;
            restrictions = PlayerRestrictions.None;
            if (string.IsNullOrEmpty(base36) || base36.Length > 7) return false;
            long value = 0;
            foreach (char character in base36)
            {
                int digit = Digits.IndexOf(char.ToLowerInvariant(character));
                if (digit < 0) return false;
                value = value * 36 + digit;
            }
            if (value > int.MaxValue) return false;
            raw = (int)value;
            restrictions = (PlayerRestrictions)(raw & KnownMask);
            return true;
        }
    }
}
