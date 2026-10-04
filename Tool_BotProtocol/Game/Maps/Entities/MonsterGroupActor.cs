using System.Collections.Generic;
using System.Linq;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Groupe de monstres : <c>GM|+cell;dir;bonus;id;modèle1,modèle2;-3;gfx1^taille1,gfx2^taille2;niv1,niv2;
    /// couleurs1;accessoires1;couleurs2;accessoires2…</c> (<c>MobGroup.parseGM</c>, <c>createMonsterGroup</c>).
    /// Le premier membre est le chef ; <see cref="Monstres.Monstres.MobsInGroupe"/> reste rempli pour l'ancien code.
    /// </summary>
    public sealed class MonsterGroupActor : Monstres.Monstres
    {
        private IReadOnlyList<MonsterGroupMember> members = new MonsterGroupMember[0];

        public MonsterGroupActor(long actorId) { Id = actorId; GroupeLeader = this; }

        public override ActorKind Kind => ActorKind.MonsterGroup;
        /// <summary>
        /// Champ [2] brut (<c>bonusValue</c> du client, <c>getStarBonus()</c> de StarLoco, multiples de 15).
        /// Son affichage en étoiles (couleur et nombre) est laissé au rendu.
        /// </summary>
        public int Stars { get => Star; set => Star = value; }
        public MonsterGroupMember Leader => members.Count > 0 ? members[0] : null;
        public int TotalLevel => members.Sum(member => member.Level);

        public IReadOnlyList<MonsterGroupMember> Members
        {
            get => members;
            set
            {
                members = value ?? new MonsterGroupMember[0];
                MobsInGroupe = members.Select(member => new Monstres.Monstres(member, this)).ToList();
                MonsterGroupMember leader = Leader;
                if (leader == null) return;
                TemplateID = leader.TemplateId;
                Level = leader.Level;
                Name = ResolveName(leader.TemplateId);
                if (leader.Gfx > 0) Gfx = leader.Gfx;
                else if (AllMonstersTemplate.TryGetValue(leader.TemplateId, out Monstres.Monstres template) && template != null) Gfx = template.GFX;
                ScaleX = leader.ScaleX; ScaleY = leader.ScaleY;
            }
        }

        /// <summary>Les membres suivent la cellule du groupe (résolue après la lecture de <c>GM</c>).</summary>
        protected override void OnCellChanged()
        {
            List<Monstres.Monstres> mobs = MobsInGroupe;
            if (mobs == null) return;
            foreach (Monstres.Monstres member in mobs)
            {
                if (ReferenceEquals(member, this)) continue;
                member.Cell = Cell;
                if (Cell == null) member.CellId = CellId;
            }
        }
    }

    /// <summary>Membre d'un groupe de monstres tel qu'annoncé par <c>GM</c>.</summary>
    public sealed class MonsterGroupMember
    {
        public int TemplateId { get; set; }
        public int Level { get; set; }
        public int Gfx { get; set; }
        public int ScaleX { get; set; } = 100;
        public int ScaleY { get; set; } = 100;
        /// <summary>Couleurs <c>c1,c2,c3</c> brutes (hexadécimal, <c>-1</c> = défaut).</summary>
        public string Colors { get; set; } = string.Empty;
        public string Accessories { get; set; } = string.Empty;
        public string Name => Monstres.Monstres.ResolveName(TemplateId);
    }
}
