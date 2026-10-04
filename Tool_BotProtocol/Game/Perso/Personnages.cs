using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Perso
{
    /// <summary>
    /// Personnage d'un autre joueur sur la carte (vue historique). Les acteurs lus dans <c>GM</c> sont des
    /// <see cref="PlayerActor"/> ou <see cref="MerchantActor"/>, qui dérivent de cette classe.
    /// </summary>
    public class Personnages : MapActor
    {
        public override ActorKind Kind => ActorKind.Player;
        public string name { get => Name; set => Name = value; }
        public byte Sexe { get; set; } = 0;
        public byte Race_ID { get; set; }
        public int GFX { get => Gfx; set => Gfx = value; }
        public int GraphicsScaleX { get => ScaleX; set => ScaleX = value; }
        public int GraphicsScaleY { get => ScaleY; set => ScaleY = value; }

        public Personnages(int Id, string N, byte S, Cell C)
        {
            id = Id;
            Name = N;
            Sexe = S;
            Cell = C;
        }

        /// <summary>Construction depuis un acteur lu dans <c>GM</c> (les champs sont remplis par le parseur).</summary>
        protected Personnages() { }
    }
}
