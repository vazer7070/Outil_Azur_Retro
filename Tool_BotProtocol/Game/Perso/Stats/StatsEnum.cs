using System;

namespace Tool_BotProtocol.Game.Perso.Stats
{
    public enum StatsEnum
    {
        AUCUN,
        FORCE,
        VITALITE,
        SAGESSE,
        AGILITE,
        INTELLIGENCE,
        CHANCE,
    }

    /// <summary>
    /// Caractéristiques que le capital augmente, avec le code que le client 1.34 envoie dans <c>AB&lt;code&gt;</c>
    /// (boutons de la fiche) et que StarLoco lit dans <c>Player.boostStat</c>.
    /// </summary>
    public enum BoostableStat : byte
    {
        Force = 10,
        Vitalite = 11,
        Sagesse = 12,
        Chance = 13,
        Agilite = 14,
        Intelligence = 15,
    }

    /// <summary>
    /// Champs 9 à 50 du paquet <c>As</c>, dans l'ordre de <c>Player.getAsPacket</c> (StarLoco) et de
    /// <c>Account.onStats</c> (client 1.34). Chaque champ porte « base,équipement,dons,boost » ; StarLoco ajoute
    /// une cinquième valeur à PA, PM et aux champs 29 à 50, que le client ignore.
    /// </summary>
    public enum AsField : byte
    {
        PA = 9,
        PM = 10,
        Force = 11,
        Vitalite = 12,
        Sagesse = 13,
        Chance = 14,
        Agilite = 15,
        Intelligence = 16,
        Portee = 17,
        Invocations = 18,
        Dommages = 19,
        DommagesPhysiques = 20,
        MaitriseArme = 21,
        DommagesPourcent = 22,
        Soins = 23,
        DommagesPieges = 24,
        DommagesPiegesPourcent = 25,
        RenvoiDommages = 26,
        CoupsCritiques = 27,
        EchecsCritiques = 28,
        EsquivePA = 29,
        EsquivePM = 30,
        ResistanceNeutre = 31,
        ResistanceNeutrePourcent = 32,
        ResistanceNeutrePvp = 33,
        ResistanceNeutrePourcentPvp = 34,
        ResistanceTerre = 35,
        ResistanceTerrePourcent = 36,
        ResistanceTerrePvp = 37,
        ResistanceTerrePourcentPvp = 38,
        ResistanceEau = 39,
        ResistanceEauPourcent = 40,
        ResistanceEauPvp = 41,
        ResistanceEauPourcentPvp = 42,
        ResistanceAir = 43,
        ResistanceAirPourcent = 44,
        ResistanceAirPvp = 45,
        ResistanceAirPourcentPvp = 46,
        ResistanceFeu = 47,
        ResistanceFeuPourcent = 48,
        ResistanceFeuPvp = 49,
        ResistanceFeuPourcentPvp = 50,
    }

    public static class StatsCodes
    {
        public const int FirstField = 9;
        public const int LastField = 50;

        /// <summary>Les six caractéristiques dans l'ordre de la fiche du client (vitalité, sagesse, force, intelligence, chance, agilité).</summary>
        public static readonly BoostableStat[] SheetOrder =
        {
            BoostableStat.Vitalite, BoostableStat.Sagesse, BoostableStat.Force,
            BoostableStat.Intelligence, BoostableStat.Chance, BoostableStat.Agilite,
        };

        /// <summary>Champ <c>As</c> d'une caractéristique : le code <c>AB</c> plus un (force 10 → champ 11…).</summary>
        public static AsField FieldOf(BoostableStat stat) => (AsField)((byte)stat + 1);

        public static BoostableStat? FromLegacy(StatsEnum stat)
        {
            switch (stat)
            {
                case StatsEnum.FORCE: return BoostableStat.Force;
                case StatsEnum.VITALITE: return BoostableStat.Vitalite;
                case StatsEnum.SAGESSE: return BoostableStat.Sagesse;
                case StatsEnum.AGILITE: return BoostableStat.Agilite;
                case StatsEnum.INTELLIGENCE: return BoostableStat.Intelligence;
                case StatsEnum.CHANCE: return BoostableStat.Chance;
                default: return null;
            }
        }

        public static StatsEnum ToLegacy(BoostableStat stat)
        {
            switch (stat)
            {
                case BoostableStat.Force: return StatsEnum.FORCE;
                case BoostableStat.Vitalite: return StatsEnum.VITALITE;
                case BoostableStat.Sagesse: return StatsEnum.SAGESSE;
                case BoostableStat.Agilite: return StatsEnum.AGILITE;
                case BoostableStat.Intelligence: return StatsEnum.INTELLIGENCE;
                case BoostableStat.Chance: return StatsEnum.CHANCE;
                default: return StatsEnum.AUCUN;
            }
        }

        public static bool IsDefined(BoostableStat stat) => Enum.IsDefined(typeof(BoostableStat), stat);
    }
}
