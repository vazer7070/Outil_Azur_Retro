using System.Collections.Generic;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Marchand hors ligne : <c>GM|~cell;dir;0;id;nom;-5;gfx^taille;c1;c2;c3;stuff;guilde;emblème;type</c>
    /// (<c>Player.parseToMerchant</c>, <c>createOfflineCharacter</c>). L'index 7 est une couleur, pas le sexe.
    /// </summary>
    public sealed class MerchantActor : Personnages
    {
        public override ActorKind Kind => ActorKind.Merchant;
        public string Color1 { get; set; } = "-1";
        public string Color2 { get; set; } = "-1";
        public string Color3 { get; set; } = "-1";
        public string Stuff { get; set; } = string.Empty;
        public IReadOnlyList<ActorAccessory> Accessories { get; set; } = new ActorAccessory[0];
        public string GuildName { get; set; } = string.Empty;
        public string GuildEmblem { get; set; } = string.Empty;
        /// <summary>Type de boutique hors ligne (StarLoco envoie toujours <c>0</c>).</summary>
        public int OfflineType { get; set; }
    }
}
