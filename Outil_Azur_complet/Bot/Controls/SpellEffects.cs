using System;
using System.Drawing;
using System.Globalization;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Effets de sorts du client 1.34 (lot AN4) : règles de <c>SpriteHandler.launchVisualEffect</c> et de
    /// <c>VisualEffectHandler.addEffect</c>, et fabrique des bandes <c>Effets/sorts/&lt;gfx&gt;_scene.png</c> des types 10,
    /// 11 et 12 (couche <see cref="EffectLayer.Depth"/>).
    /// <list type="bullet">
    /// <item>Types : 0 et types inconnus sans effet ; 10 et 12 posés au lanceur, 11 à la cellule visée ; 12, 30, 31, 40, 41
    /// et 51 retiennent la file (au plus 1 000 ms) ; les projectiles (20 à 51) relèvent du lot AN5 : rien n'est dessiné ici.</item>
    /// <item>Profondeur : cellule visée × 100 + 50 ± (index + 51), index 1 à 21 puis 0 ; devant le sprite, + ; derrière, −.
    /// Un nouvel effet qui reprend l'index d'un effet encore affiché le remplace (<c>removeMovieClip</c> du client).</item>
    /// <item>Direction du lanceur vers la cellule (<c>autoCalculateSpriteDirection</c>) : 1, 3, 5 ou 7 d'après les
    /// coordonnées à plat des deux cellules (<c>x</c>, <c>rootY</c>), rien sur la même cellule.</item>
    /// </list>
    /// Chaque scène est jouée une fois puis retirée, au plus 20 s (<c>VISUAL_EFFECT_MAX_TIMER</c>), quelle que soit sa
    /// colonne <c>fin</c>. Fil de l'interface ; aucune image n'est lue sur ce fil.
    /// </summary>
    public sealed class SpellEffects
    {
        /// <summary>Famille des clips <c>clips/spells</c> dans <c>Effets/</c>.</summary>
        public const string SpellFamily = "sorts";
        /// <summary>Famille des clips <c>clips/extra</c> dans <c>Effets/</c>.</summary>
        public const string ExtraFamily = "extra";
        /// <summary>Bande de la scène d'un clip d'effet.</summary>
        public const string SceneAnimation = "scene";
        /// <summary><c>CRITICAL_HIT_XTRA_FILE</c> : <c>clips/extra/5.swf</c>, au-dessus du lanceur d'un coup critique.</summary>
        public const int CriticalHitClip = 5;
        /// <summary><c>CRITICAL_HIT_DURATION</c> (ms).</summary>
        public const double CriticalHitDuration = 5000;
        /// <summary><c>VisualEffectHandler.MAX_INDEX</c> : l'index des effets va de 1 à 21, puis repart de 0.</summary>
        public const int MaxIndex = 21;
        /// <summary><c>MAX_SPRITES_ON_CELL</c> / 2 (100 / 2).</summary>
        public const int HalfSpritesOnCell = 50;
        /// <summary><c>CELL_WIDTH</c> et <c>CELL_HALF_HEIGHT</c> du client (coordonnées à plat des cellules).</summary>
        public const float CellWidth = 53f, CellHalfHeight = 13.5f;

        private readonly IMapEffect[] slots = new IMapEffect[MaxIndex + 1];
        private int index;

        /// <summary>Dernier index attribué (0 avant le premier effet, comme <c>_incIndex</c> du client).</summary>
        public int LastIndex => index;

        /// <summary>Le type a un effet dans <c>launchVisualEffect</c> (10, 11, 12, 20, 21, 30, 31, 40, 41, 50, 51).</summary>
        public static bool HasEffect(int type)
        {
            switch (type)
            {
                case 10: case 11: case 12: case 20: case 21: case 30: case 31: case 40: case 41: case 50: case 51: return true;
                default: return false;
            }
        }

        /// <summary>Type dessiné par ce lot : 10, 11 et 12 (une scène posée).</summary>
        public static bool IsDrawn(int type) => type == 10 || type == 11 || type == 12;

        /// <summary>L'effet retient la file de son lanceur jusqu'à sa fin, au plus 1 000 ms : 12, 30, 31, 40, 41, 51.</summary>
        public static bool IsBlocking(int type) => type == 12 || type == 30 || type == 31 || type == 40 || type == 41 || type == 51;

        /// <summary>Types 10 et 12 : la scène est posée au lanceur ; 11 : à la cellule visée.</summary>
        public static bool AtCaster(int type) => type == 10 || type == 12;

        /// <summary><c>getNextIndex</c> : 1, 2… 21, puis 0, 1…</summary>
        public int NextIndex()
        {
            index++;
            if (index > MaxIndex) index = 0;
            return index;
        }

        /// <summary>Profondeur du client : cellule × 100 + 50 ± (index + 51) (+ devant le sprite, − derrière).</summary>
        public static int Depth(int targetCell, int effectIndex, bool inFront) =>
            targetCell * 100 + 50 + (inFront ? 1 : -1) * (effectIndex + HalfSpritesOnCell + 1);

        /// <summary>
        /// Animation du lanceur d'après le champ « anim » du paquet. <paramref name="spellCodes"/> (GA300) : <c>-1</c>, rien du
        /// tout ; <c>-2</c>, direction et effet sans animation (<paramref name="animation"/> null). Un nombre donne
        /// <c>anim</c> suivi du texte reçu ; un bond <c>a~b~c~d</c> (moins de trois parties : rien) se réduit à l'animation
        /// <c>b</c> sur place, sans les déplacements. Champ vide ou absent : rien (le client échoue sur <c>undefined</c>).
        /// Faux : aucun lancement.
        /// </summary>
        public static bool TryParseAnimation(string field, bool spellCodes, out string animation)
        {
            animation = null;
            if (string.IsNullOrWhiteSpace(field)) return false;
            if (spellCodes && field == "-1") return false;
            if (spellCodes && field == "-2") return true;
            if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsNaN(number))
            {
                animation = "anim" + field;
                return true;
            }
            string[] parts = field.Split('~');
            if (parts.Length < 3) return false;
            animation = parts[1];
            return true;
        }

        /// <summary>
        /// Coordonnées à plat d'une cellule (<c>x</c>, <c>rootY</c> du client, sans le relief) sur une carte de
        /// <paramref name="mapWidth"/> cellules de large ; faux si la cellule ou la largeur sont invalides.
        /// </summary>
        public static bool TryFlatPosition(int cellId, int mapWidth, out PointF position)
        {
            position = PointF.Empty;
            if (cellId < 0 || mapWidth < 2) return false;
            int period = 2 * mapWidth - 1, within = cellId % period;
            bool odd = within >= mapWidth;
            int row = 2 * (cellId / period) + (odd ? 1 : 0), column = odd ? within - mapWidth : within;
            position = new PointF((column + (odd ? 1f : 0.5f)) * CellWidth, (row + 1) * CellHalfHeight);
            return true;
        }

        /// <summary>
        /// <c>getDirectionFromCoordinates(…, false)</c> de la cellule <paramref name="fromCell"/> vers <paramref name="toCell"/> :
        /// 1, 3, 5 ou 7 ; -1 si les cellules sont les mêmes ou illisibles (le client ne touche alors pas à la direction).
        /// </summary>
        public static int DirectionTo(int fromCell, int toCell, int mapWidth)
        {
            if (fromCell == toCell) return -1;
            if (!TryFlatPosition(fromCell, mapWidth, out PointF from) || !TryFlatPosition(toCell, mapWidth, out PointF to)) return -1;
            double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
            if (angle >= 0 && angle < Math.PI / 2) return 1;
            if (angle >= Math.PI / 2 && angle <= Math.PI) return 3;
            if (angle >= -Math.PI && angle < -Math.PI / 2) return 5;
            if (angle >= -Math.PI / 2 && angle < 0) return 7;
            return 1;
        }

        /// <summary>
        /// Pose la scène <c>Effets/sorts/&lt;gfx&gt;_scene.png</c> en <paramref name="world"/> (pied du lanceur ou centre de la
        /// cellule), à la profondeur du client pour <paramref name="targetCell"/>, et l'ajoute à <paramref name="set"/> par
        /// <paramref name="add"/> (qui peut la refuser et la libérer). L'effet qui occupait le même index est retiré. La bande est
        /// demandée au pool ; absente ou pas prête dans les 300 ms, l'effet est sauté sans rien dessiner. Null si le gfx est
        /// invalide.
        /// </summary>
        public StripEffect Add(ActorSprites sprites, MapEffectSet set, Func<IMapEffect, bool> add, int gfx, PointF world, int targetCell,
            bool inFront, double now)
        {
            if (sprites == null || set == null || add == null || gfx < 0 || targetCell < 0) return null;
            int slot = NextIndex();
            IMapEffect previous = slots[slot];
            slots[slot] = null;
            if (previous != null) set.Remove(previous);
            var effect = new StripEffect(sprites.ResolveFixed(SpellFamily, gfx, SceneAnimation), world, now, StripPlay.Once,
                layer: EffectLayer.Depth, depth: Depth(targetCell, slot, inFront), order: inFront ? 4 : 2);
            if (!add(effect)) return null;
            slots[slot] = effect;
            return effect;
        }

        /// <summary>Oublie les index et les effets suivis (les effets eux-mêmes appartiennent à <see cref="MapEffectSet"/>).</summary>
        public void Reset()
        {
            Array.Clear(slots, 0, slots.Length);
            index = 0;
        }
    }
}
