using System.Collections.Generic;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Monstre (<c>-2</c>) ou créature (<c>-1</c>) en combat : <c>GM|+cell;dir;0;id;modèle;-2;gfx^taille;grade;c1;c2;c3;
    /// accessoires;PV;PA;PM[;résistances×7];équipe</c> (<c>Fighter.getGmPacket</c>, <c>createMonster</c>).
    /// L'index 7 est le grade du monstre, pas son niveau.
    /// </summary>
    public sealed class FightMonsterActor : Monstres.Monstres
    {
        public FightMonsterActor(long actorId, int templateId)
        {
            Id = actorId;
            TemplateID = templateId;
            Name = ResolveName(templateId);
            if (AllMonstersTemplate.TryGetValue(templateId, out Monstres.Monstres template) && template != null) Gfx = template.GFX;
            GroupeLeader = this;
            MobsInGroupe.Add(this);
        }

        public override ActorKind Kind => ActorKind.FightMonster;
        public int TemplateId => TemplateID;
        /// <summary>Grade du monstre (<c>powerLevel</c> du client).</summary>
        public int Grade { get; set; }
        /// <summary>Vrai pour le type <c>-1</c> (créature invoquée).</summary>
        public bool IsCreature { get; set; }
        public string Color1 { get; set; } = "-1";
        public string Color2 { get; set; } = "-1";
        public string Color3 { get; set; } = "-1";
        public string AccessoriesRaw { get; set; } = string.Empty;
        public IReadOnlyList<ActorAccessory> Accessories { get; set; } = new ActorAccessory[0];
        public int? Life { get; set; }
        public int? ActionPoints { get; set; }
        public int? MovementPoints { get; set; }
        public int[] Resistances { get; set; }
        public int? Team { get; set; }
    }
}
