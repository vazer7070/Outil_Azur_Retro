namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Prisme d'alignement : <c>GM|+cell;dir;0;id;nom;-10;gfx^100;niveau;grade;alignement</c> (<c>Prism.getGMPrisme</c>).
    /// Le client lit le champ nom comme un identifiant de monstre lié et construit l'alignement avec (index 9, index 8).
    /// </summary>
    public sealed class PrismActor : MapActor
    {
        public override ActorKind Kind => ActorKind.Prism;
        public int Level { get; set; }
        /// <summary>Camp (1 Bonta, 2 Brâkmar…).</summary>
        public int AlignmentSide { get; set; }
        /// <summary>Grade / valeur de l'alignement.</summary>
        public int AlignmentValue { get; set; }
        public int? LinkedMonsterId { get; set; }

        public override string DisplayName => "Prisme";
    }
}
