using System.Collections.Generic;
using Tool_BotProtocol.Game.NPC;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// PNJ d'une carte : <c>GM|+cell;dir;0;id;modèle;-4;gfx^taille|largeurxhauteur;sexe;c1;c2;c3;accessoires;extraClip|-1;illustration</c>
    /// (<c>Npc.parse</c>, <c>createNonPlayableCharacter</c>). L'identifiant (négatif chez StarLoco) n'est jamais le modèle.
    /// </summary>
    public sealed class NpcActor : PNJ
    {
        public NpcActor(long actorId, int templateId)
        {
            Id = actorId;
            NPc_ID = templateId;
            Name = ResolveName(templateId);
            if (AllPNJ.TryGetValue(templateId, out PNJ template) && template != null) MapId = template.MapId;
        }

        public override ActorKind Kind => ActorKind.Npc;
        public int TemplateId => NPc_ID;
        public string Color1 { get; set; } = "-1";
        public string Color2 { get; set; } = "-1";
        public string Color3 { get; set; } = "-1";
        public string AccessoriesRaw { get; set; } = string.Empty;
        public IReadOnlyList<ActorAccessory> Accessories { get; set; } = new ActorAccessory[0];
        /// <summary>Clip affiché au-dessus du PNJ (point d'exclamation de quête…) ; -1 si aucun.</summary>
        public int ExtraClip { get; set; } = -1;
        /// <summary>Illustration personnalisée du dialogue (<c>customArtwork</c>) ; 0 si aucune.</summary>
        public int Artwork { get; set; }
    }
}
