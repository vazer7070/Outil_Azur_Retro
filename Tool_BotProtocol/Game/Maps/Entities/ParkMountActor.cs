namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Monture d'enclos : <c>GM|+cell;dir;0;id;nom;-9;gfx^taille;propriétaire;niveau;modèle</c> (<c>Mount.parseToGM</c>,
    /// <c>createParkMount</c>). Un nom vide est affiché « Sans nom » comme le texte <c>NO_NAME</c> du client.
    /// </summary>
    public sealed class ParkMountActor : MapActor
    {
        public override ActorKind Kind => ActorKind.ParkMount;
        public string OwnerName { get; set; } = string.Empty;
        public int Level { get; set; }
        /// <summary>Modèle (couleur) de la dragodinde.</summary>
        public int ModelId { get; set; }

        public override string DisplayName => string.IsNullOrEmpty(Name) ? "Sans nom" : Name;
    }
}
