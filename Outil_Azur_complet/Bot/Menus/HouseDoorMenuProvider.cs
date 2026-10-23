using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Habitat;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Maps.Interactives;

namespace Outil_Azur_complet.Bot.Menus
{
    /// <summary>
    /// Données des maisons pour le menu des portes (type 5) et des coffres (type 6) d'<see cref="InteractiveMenuProvider"/>,
    /// comme <c>onObjectRelease</c> du client 1.34 : titre « &lt;objet&gt; &lt;maison&gt; », ligne « Chez moi ! » / « Chez X » /
    /// « Maison abandonnée » pour une porte, puis les paramètres O/S/L du critère des compétences (porte : propriétaire,
    /// à vendre, verrouillée ; coffre : chez soi, S vrai, verrouillée). La maison vient de <c>H.d[carte].c&lt;cellule&gt;</c>
    /// (porte) ou <c>H.m[carte]</c> (coffre, intérieur de la maison) et de ce que StarLoco en a dit (<c>hP</c>, <c>hL</c>).
    /// Ce n'est pas un second menu : le menu reste celui des objets interactifs (lot M3).
    /// </summary>
    public static class HouseDoorMenuProvider
    {
        public const int DoorType = 5, ChestType = 6;

        /// <summary>Maison de la porte ou du coffre, ou <c>null</c> (autre type, textes <c>houses</c> absents, pas de session).</summary>
        public static HouseInfo HouseOf(Interactives interactive, GameClass game)
        {
            HouseActions houses = game?.Interactions?.House;
            if (houses == null || interactive?.Cell == null) return null;
            switch (interactive.ClientType)
            {
                case DoorType: return houses.ForDoor(game.Map?.MapID ?? 0, interactive.Cell.CellID);
                case ChestType: return houses.CurrentHouse;
                default: return null;
            }
        }

        /// <summary>Titre du menu : nom de l'objet suivi du nom de la maison pour une porte connue.</summary>
        public static string Title(Interactives interactive, HouseInfo house)
        {
            string name = interactive?.Name ?? string.Empty;
            return house == null || interactive?.ClientType != DoorType ? name : (name + " " + house.Name).Trim();
        }

        /// <summary>Ligne d'information sous le titre d'une porte (<c>MY_HOME</c>, <c>HOME_OF</c>, <c>HOUSE_WITH_NO_OWNER</c>) ; <c>null</c> sinon.</summary>
        public static string OwnerLine(Interactives interactive, HouseInfo house) =>
            interactive?.ClientType == DoorType && house != null ? house.OwnerLine() : null;

        /// <summary>État d'une compétence de porte ou de coffre (<c>Skill.getState</c>) avec les données de la maison ; faux partout sans maison.</summary>
        public static string StateOf(int type, string criterion, HouseInfo house)
        {
            bool owner = house?.LocalOwner ?? false, locked = house?.IsLocked ?? false;
            switch (type)
            {
                case DoorType: return SkillState.Evaluate(criterion, true, owner, house?.IsForSale ?? false, locked);
                case ChestType: return SkillState.Evaluate(criterion, true, owner, true, locked);
                default: return SkillState.Evaluate(criterion, true);
            }
        }
    }
}
