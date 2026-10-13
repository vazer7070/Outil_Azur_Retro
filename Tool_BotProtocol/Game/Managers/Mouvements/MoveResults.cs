using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Game.Managers.Mouvements
{
    public enum MoveResults
    {
        /// <summary><c>GA001</c> envoyé.</summary>
        EXIT,
        SAMECELL,
        FALL,
        PathfindingError,
        CellRangeError,
        /// <summary>Personnage occupé (combat, dialogue, réponse du serveur attendue…). Les pods ne sont plus vérifiés : au-delà
        /// du maximum, le serveur refuse lui-même (<c>GA;0</c> et <c>Im112</c>).</summary>
        CharacterBusyOrFull,
        CellNotWalkable,
        CellIsTypeOfInteractiveObject,
        /// <summary>Plus renvoyé : la cellule d'un groupe de monstres est une destination valide, le serveur lance le combat à l'arrivée.</summary>
        MONSTER,
        PathfindingErrorCount

    }
}
