using System;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Monstres
{
    /// <summary>Capture d'un groupe de monstres d'après StarLoco, telle qu'affichée au survol du groupe.</summary>
    public enum GroupCapture
    {
        /// <summary>Un membre au moins n'a pas de fiche <c>BotMonsters</c> avec la colonne <c>capturable</c> : rien n'est affiché.</summary>
        Unknown,
        /// <summary>Tous les membres sont capturables et la carte n'est pas une arène.</summary>
        Capturable,
        /// <summary>Un membre au moins a <c>capturable = 0</c> dans la base du serveur.</summary>
        NotCapturable,
        /// <summary>Carte d'arène : StarLoco n'y crée jamais de pierre d'âme pleine.</summary>
        Arena,
    }

    /// <summary>
    /// Pierres d'âme et arènes de StarLoco. Fin de combat (<c>Fight.java</c>, « Capture d'âmes ») : la pierre pleine n'est
    /// créée que si tous les monstres vaincus ont <c>capturable = 1</c> (table <c>monsters</c>) et que la carte n'est pas
    /// une arène ; un vainqueur sous l'état « capture d'âme » armé d'une pierre vide (type 83) de niveau suffisant la gagne.
    /// Une pierre pleine (type 85) s'utilise par <c>OU&lt;objet&gt;|</c> seulement dans une arène
    /// (<c>ObjectTemplate.applyAction</c>, <c>SoulStone.isInArenaMap</c>) : le groupe apparaît, le serveur répond
    /// <c>Im022;1~&lt;modèle&gt;</c> et retire la pierre ; ailleurs, il ignore la demande sans rien répondre.
    /// </summary>
    public static class SoulStones
    {
        /// <summary><c>ITEM_TYPE_PIERRE_AME</c> : pierre vide, équipée comme arme pour capturer.</summary>
        public const int EmptyStoneType = 83;
        /// <summary><c>ITEM_TYPE_PIERRE_AME_PLEINE</c> : pierre pleine, invoquée dans une arène.</summary>
        public const int FullStoneType = 85;
        /// <summary>Liste de <c>SoulStone.isInArenaMap</c>, telle quelle.</summary>
        public const string ArenaMapList = "10131,10132,10133,10134,10135,10136,10137,10138";

        /// <summary>Cartes d'arène 10131 à 10138.</summary>
        public static readonly int[] ArenaMaps = ArenaMapList.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        /// <summary>
        /// Même test que StarLoco : <c>"10131,…,10138".contains(String.valueOf(id))</c>. La comparaison porte sur une
        /// sous-chaîne, donc quelques identifiants courts (101, 1013, 31…) passent aussi pour des arènes ; le bot le reproduit
        /// pour annoncer ce que le serveur fera vraiment.
        /// </summary>
        public static bool IsArenaMap(int mapId) => ArenaMapList.Contains(mapId.ToString(CultureInfo.InvariantCulture));

        /// <summary>
        /// Capture possible du groupe sur la carte <paramref name="mapId"/> (0 ou moins : carte inconnue, test d'arène omis).
        /// Les modèles viennent de <see cref="Monstres.AllMonstersTemplate"/> (export <c>BotMonsters</c>, élément <c>CAPTURABLE</c>).
        /// </summary>
        public static GroupCapture Evaluate(MonsterGroupActor group, int mapId)
        {
            if (group == null) return GroupCapture.Unknown;
            if (mapId > 0 && IsArenaMap(mapId)) return GroupCapture.Arena;
            var members = (group.Members ?? new MonsterGroupMember[0]).Where(member => member != null).ToList();
            if (members.Count == 0) return GroupCapture.Unknown;
            bool unknown = false;
            foreach (MonsterGroupMember member in members)
            {
                bool? capturable = Monstres.ReturnMonsters(member.TemplateId)?.Capturable;
                if (capturable == false) return GroupCapture.NotCapturable;
                if (capturable == null) unknown = true;
            }
            return unknown ? GroupCapture.Unknown : GroupCapture.Capturable;
        }

        /// <summary>Ligne ajoutée sous les membres du groupe au survol, ou <c>null</c> quand la base ne le dit pas.</summary>
        public static string Describe(GroupCapture capture)
        {
            switch (capture)
            {
                case GroupCapture.Capturable: return "Capturable (pierre d'âme)";
                case GroupCapture.NotCapturable: return "Non capturable";
                case GroupCapture.Arena: return "Arène : pas de capture";
                default: return null;
            }
        }
    }
}
