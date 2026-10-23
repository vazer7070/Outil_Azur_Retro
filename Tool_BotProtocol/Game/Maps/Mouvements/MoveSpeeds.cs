using System;
using System.Globalization;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Tool_BotProtocol.Game.Maps.Mouvements
{
    /// <summary>Allure d'un déplacement, choisie comme <c>SpriteHandler.moveSprite</c> du client 1.34.</summary>
    public enum MoveMode
    {
        Walk,
        Run,
        /// <summary>Course sur monture (<c>MOUNT_SPEEDS</c>) ; une marche montée garde la vitesse de marche.</summary>
        Mount
    }

    /// <summary>
    /// Ce qui règle la vitesse d'un sprite dans le client : sa classe (joueur ou créature), le combat, la monture et les
    /// bits de restriction du sprite lus dans <c>GM</c> (16 marche forcée, 32 lent, 256 vitesse d'administrateur).
    /// </summary>
    public sealed class MoveProfile
    {
        /// <summary>Sprite de joueur (<c>Character</c>) : course dès 3 cases, contre 6 (4 en combat) pour les autres sprites.</summary>
        public bool IsPlayer { get; set; } = true;
        public bool InFight { get; set; }
        /// <summary>Personnage sur sa monture : la course utilise <c>MOUNT_SPEEDS</c>.</summary>
        public bool Mounted { get; set; }
        /// <summary>Restriction 16 ou groupe de monstres : toujours au pas.</summary>
        public bool ForceWalk { get; set; }
        public bool ForceRun { get; set; }
        /// <summary>Restriction 32 : vitesse divisée par deux.</summary>
        public bool Slow { get; set; }
        /// <summary>Restriction 256 : vitesse multipliée par cinq (sans effet si <see cref="Slow"/>).</summary>
        public bool SonicSpeed { get; set; }
        /// <summary>Modérateur de base du sprite (1 par défaut dans le client).</summary>
        public double SpeedModerator { get; set; } = 1;

        /// <summary>Modérateur appliqué, comme l'accesseur <c>speedModerator</c> de <c>Character</c>.</summary>
        public double EffectiveModerator => Slow ? SpeedModerator / 2 : SonicSpeed ? SpeedModerator * 5 : SpeedModerator;

        /// <summary>Nombre de cellules du chemin (départ compris) au-delà duquel le sprite court.</summary>
        public int RunLimit => IsPlayer ? MoveSpeeds.PlayerRunLimit : InFight ? MoveSpeeds.FightCreatureRunLimit : MoveSpeeds.CreatureRunLimit;

        /// <summary>Personnage du compte ou autre joueur hors combat, sans restriction.</summary>
        public static MoveProfile Player(bool mounted = false) => new MoveProfile { Mounted = mounted };

        /// <summary>Joueur dont les restrictions du sprite sont données en base 36 (champ 18 de <c>GM</c>, signe accepté).</summary>
        public static MoveProfile Player(string restrictionsBase36, bool mounted, bool inFight)
        {
            var profile = new MoveProfile { Mounted = mounted, InFight = inFight };
            if (MoveSpeeds.TryParseBase36(restrictionsBase36, out long restrictions)) profile.ApplySpriteRestrictions(restrictions);
            return profile;
        }

        /// <summary>Groupe de monstres : le client le fait toujours marcher (<c>_bForceWalk</c> de la classe du groupe).</summary>
        public static MoveProfile MonsterGroup() => new MoveProfile { IsPlayer = false, ForceWalk = true };

        /// <summary>PNJ, monstre, percepteur, prisme… : course au-delà de 6 cellules (4 en combat).</summary>
        public static MoveProfile Creature(bool inFight = false) => new MoveProfile { IsPlayer = false, InFight = inFight };

        /// <summary>Profil d'un acteur de la carte, d'après sa classe côté client.</summary>
        public static MoveProfile ForActor(MapActor actor, bool inFight)
        {
            switch (actor)
            {
                case PlayerActor player: return Player(player.RestrictionsRaw, player.HasMount, inFight || player.InFight);
                case MonsterGroupActor _: return MonsterGroup();
                case null: return Player();
                default: return Creature(inFight);
            }
        }

        /// <summary>Applique les bits du sprite (<c>forceWalk</c> 16, <c>isSlow</c> 32, <c>isAdminSonicSpeed</c> 256).</summary>
        public void ApplySpriteRestrictions(long restrictions)
        {
            ForceWalk = (restrictions & 16) == 16;
            Slow = (restrictions & 32) == 32;
            SonicSpeed = (restrictions & 256) == 256;
        }
    }

    /// <summary>
    /// Vitesses de déplacement du client 1.34 (<c>ank.battlefield.mc.Sprite</c>, loader : <c>WALK_SPEEDS</c>, <c>RUN_SPEEDS</c>,
    /// <c>MOUNT_SPEEDS</c>) en pixels par milliseconde et par direction : <c>basicMove</c> avance de <c>vitesse × temps écoulé</c>
    /// vers le centre de la cellule suivante. Une case horizontale mesure 53 px, une demi-case 26,5 × 13,5 px, un niveau 20 px.
    /// </summary>
    public static class MoveSpeeds
    {
        /// <summary>Marche (indice = direction 0 à 7, 0 = est puis sens horaire).</summary>
        public static readonly double[] WalkSpeeds = { 0.07, 0.06, 0.06, 0.06, 0.07, 0.06, 0.06, 0.06 };
        public static readonly double[] RunSpeeds = { 0.17, 0.15, 0.15, 0.15, 0.17, 0.15, 0.15, 0.15 };
        public static readonly double[] MountSpeeds = { 0.23, 0.2, 0.2, 0.2, 0.23, 0.2, 0.2, 0.2 };
        /// <summary>Correction de vitesse en descente (+) ou en montée (−) d'un niveau ou d'une pente.</summary>
        public const double LevelSpeedDelta = 0.01;
        /// <summary>Course d'un joueur si le chemin compte plus de 3 cellules, départ compris (<c>GameActions.onActions</c>).</summary>
        public const int PlayerRunLimit = 3;
        /// <summary>Autres sprites hors combat (<c>SpriteHandler.DEFAULT_RUNLINIT</c>).</summary>
        public const int CreatureRunLimit = 6;
        public const int FightCreatureRunLimit = 4;
        public const double CellHalfWidth = 26.5;
        public const double CellHalfHeight = 13.5;
        public const double LevelHeight = 20;
        public const double HalfLevelHeight = 10;

        // Décalages (X, Y) des huit directions dans le repère du bot (Cell.X / Cell.Y), identiques à ceux du client.
        private static readonly int[] DirectionX = { 1, 1, 1, 0, -1, -1, -1, 0 };
        private static readonly int[] DirectionY = { -1, 0, 1, 1, 1, 0, -1, -1 };

        /// <summary>Allure du chemin (départ compris), comme <c>moveSprite</c> : marche forcée, course forcée, sinon course
        /// si le chemin dépasse <see cref="MoveProfile.RunLimit"/> ; la course montée prend les vitesses de monture.</summary>
        public static MoveMode ChooseMode(int pathCellCount, MoveProfile profile)
        {
            profile = profile ?? MoveProfile.Player();
            bool run = !profile.ForceWalk && (profile.ForceRun || pathCellCount > profile.RunLimit);
            if (!run) return MoveMode.Walk;
            return profile.Mounted ? MoveMode.Mount : MoveMode.Run;
        }

        public static double BaseSpeed(MoveMode mode, int direction)
        {
            int index = direction & 7;
            switch (mode)
            {
                case MoveMode.Run: return RunSpeeds[index];
                case MoveMode.Mount: return MountSpeeds[index];
                default: return WalkSpeeds[index];
            }
        }

        /// <summary>Direction 0 à 7 du pas <paramref name="from"/> → <paramref name="to"/>, -1 si les cellules sont identiques
        /// ou ne sont pas voisines.</summary>
        public static int DirectionBetween(Cell from, Cell to)
        {
            if (from == null || to == null) return -1;
            int dx = to.X - from.X, dy = to.Y - from.Y;
            for (int direction = 0; direction < 8; direction++)
                if (DirectionX[direction] == dx && DirectionY[direction] == dy) return direction;
            return -1;
        }

        /// <summary>Décalage (X, Y) d'une direction.</summary>
        public static void Offset(int direction, out int dx, out int dy)
        {
            dx = DirectionX[direction & 7];
            dy = DirectionY[direction & 7];
        }

        /// <summary>Position du sprite posé sur la cellule, en pixels de la carte (origine arbitraire) : x = (X − Y) × 26,5,
        /// y = (X + Y) × 13,5 − 20 × (niveau − 7), moins 10 px sur une pente (<c>groundSlope</c> ≠ 1).</summary>
        public static void SpritePosition(Cell cell, out double x, out double y)
        {
            x = (cell.X - cell.Y) * CellHalfWidth;
            y = (cell.X + cell.Y) * CellHalfHeight - LevelHeight * (cell.layer_ground_Level - 7);
            if (cell.layer_ground_slope != 1) y -= HalfLevelHeight;
        }

        /// <summary>
        /// Durée d'un pas en millisecondes (<c>moveToCell</c> puis <c>basicMove</c>) : distance en pixels entre les deux
        /// positions du sprite divisée par la vitesse de la direction × modérateur, corrigée de ±0,01 px/ms en descente,
        /// en montée ou en changement de pente. Le client s'arrête à une image près ; la valeur est arrondie au-dessus.
        /// </summary>
        public static int StepDuration(Cell from, Cell to, MoveMode mode, double moderator)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return 0;
            int direction = DirectionBetween(from, to);
            if (direction < 0) return 0;
            double speed = BaseSpeed(mode, direction) * (moderator > 0 ? moderator : 1);
            if (to.layer_ground_Level < from.layer_ground_Level) speed += LevelSpeedDelta;
            else if (to.layer_ground_Level > from.layer_ground_Level) speed -= LevelSpeedDelta;
            else if (from.layer_ground_slope != to.layer_ground_slope)
            {
                if (to.layer_ground_slope == 1) speed += LevelSpeedDelta;
                else if (from.layer_ground_slope == 1) speed -= LevelSpeedDelta;
            }
            if (speed < 0.001) speed = 0.001;
            SpritePosition(from, out double x1, out double y1);
            SpritePosition(to, out double x2, out double y2);
            double distance = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            return (int)Math.Ceiling(distance / speed);
        }

        /// <summary>
        /// Lit un entier en base 36 comme <c>parseInt(valeur, 36)</c> : signe accepté, casse ignorée, faux sans exception
        /// pour une valeur vide, illisible ou hors des bornes d'un <c>int</c>.
        /// </summary>
        public static bool TryParseBase36(string value, out long result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;
            string text = value.Trim();
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative || text.StartsWith("+", StringComparison.Ordinal)) text = text.Substring(1);
            if (text.Length == 0 || text.Length > 7) return false;
            long parsed = 0;
            foreach (char character in text.ToLower(CultureInfo.InvariantCulture))
            {
                int digit = character >= '0' && character <= '9' ? character - '0'
                    : character >= 'a' && character <= 'z' ? character - 'a' + 10 : -1;
                if (digit < 0) return false;
                parsed = parsed * 36 + digit;
            }
            if (parsed > int.MaxValue) return false;
            result = negative ? -parsed : parsed;
            return true;
        }
    }
}
