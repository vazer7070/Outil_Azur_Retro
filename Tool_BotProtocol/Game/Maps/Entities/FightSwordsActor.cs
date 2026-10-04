using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Épées d'un combat en cours sur la carte : <c>Gc+&lt;combat&gt;;&lt;type&gt;|&lt;équipe&gt;;&lt;cellule&gt;;&lt;typeÉquipe&gt;;&lt;alignement&gt;|…</c>
    /// (<c>SocketManager.GAME_SEND_GAME_ADDFLAG_PACKET_TO_MAP</c>, <c>Game.onChallenge</c>), retirées par <c>Gc-&lt;combat&gt;</c>.
    /// Rangées dans <c>Map.FightSwords</c> par identifiant de combat (celui de l'initiateur chez StarLoco).
    /// </summary>
    public sealed class FightSwordsActor : MapActor
    {
        public FightSwordsActor(long fightId, int fightType, IReadOnlyList<FightTeamFlag> teams)
        {
            Id = fightId;
            FightType = fightType;
            Teams = teams ?? new FightTeamFlag[0];
            FightTeamFlag first = Teams.FirstOrDefault();
            if (first != null) CellId = first.CellId;
        }

        public override ActorKind Kind => ActorKind.FightSwords;
        public long FightId => Id;
        /// <summary>Type de combat : 0 défi, 4 contre des monstres, 5 percepteur… (premier champ après l'identifiant).</summary>
        public int FightType { get; }
        public IReadOnlyList<FightTeamFlag> Teams { get; }

        public override string DisplayName => "Combat #" + Id.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Une équipe d'un combat de la carte : épée posée sur sa cellule.</summary>
    public sealed class FightTeamFlag
    {
        public FightTeamFlag(long teamId, int cellId, int teamType, int alignment) { TeamId = teamId; CellId = cellId; TeamType = teamType; Alignment = alignment; }
        public long TeamId { get; }
        public int CellId { get; }
        /// <summary>0 joueurs, 1 monstres, 3 percepteur…</summary>
        public int TeamType { get; }
        /// <summary>-1 neutre, sinon camp de l'alignement.</summary>
        public int Alignment { get; }
        public Cell Cell { get; internal set; }
    }
}
