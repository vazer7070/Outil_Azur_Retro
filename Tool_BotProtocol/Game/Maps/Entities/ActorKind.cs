namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Types d'acteurs distingués par <c>Game.onMovement</c> du client 1.34 (champ type de <c>GM</c>).
    /// </summary>
    public enum ActorKind
    {
        /// <summary>Type non pris en charge (mutants <c>-7</c>/<c>-8</c>, valeur inconnue).</summary>
        Unknown = 0,
        /// <summary>Joueur : type = numéro de classe (≥ 0).</summary>
        Player,
        /// <summary>PNJ : type <c>-4</c>.</summary>
        Npc,
        /// <summary>Groupe de monstres sur la carte : type <c>-3</c>.</summary>
        MonsterGroup,
        /// <summary>Monstre ou créature en combat : types <c>-2</c> et <c>-1</c>.</summary>
        FightMonster,
        /// <summary>Marchand hors ligne : type <c>-5</c>.</summary>
        Merchant,
        /// <summary>Percepteur : type <c>-6</c>.</summary>
        Collector,
        /// <summary>Prisme d'alignement : type <c>-10</c>.</summary>
        Prism,
        /// <summary>Monture d'enclos : type <c>-9</c>.</summary>
        ParkMount,
        /// <summary>Épées d'un combat en cours sur la carte : paquet <c>Gc+</c>.</summary>
        FightSwords
    }
}
