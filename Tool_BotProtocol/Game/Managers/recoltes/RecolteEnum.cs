namespace Tool_BotProtocol.Game.Managers.recoltes
{
    /// <summary>Issue d'une récolte suivie par <see cref="Harvest"/>.</summary>
    public enum RecolteEnum
    {
        /// <summary><c>IQ</c> reçu pour le personnage : ressource récoltée.</summary>
        RECOLTÉ,
        /// <summary>Déplacement interrompu ou refusé avant l'envoi de <c>GA500</c>.</summary>
        FALL,
        /// <summary>Un autre joueur a commencé l'action 501 sur la même ressource.</summary>
        VOLÉ,
        NO_RECOLTABLE,
        NO_TIME,
        /// <summary>Demande refusée localement (personnage occupé, anti-spam, compétence absente).</summary>
        REFUSÉ,
        /// <summary>Changement de carte, déconnexion ou nouvelle demande avant le résultat.</summary>
        ANNULÉ
    }
}
