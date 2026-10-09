using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Combats
{
    /// <summary>Origine d'un <see cref="VisualEvent"/>.</summary>
    public enum VisualSource
    {
        /// <summary>Action <c>GA&lt;id&gt;;&lt;type&gt;;&lt;acteur&gt;;&lt;paramètres&gt;</c> (combat, ou 208/228/501 hors combat).</summary>
        GameAction,
        /// <summary><c>IQ&lt;sprite&gt;|&lt;quantité&gt;</c> : quantité récoltée (<c>Infos.onQuantity</c> du client).</summary>
        Quantity,
        /// <summary><c>eUK&lt;acteur&gt;|&lt;émote&gt;[|&lt;durée&gt;]</c> : émote jouée (<c>Emotes.onUse</c> du client).</summary>
        Emote
    }

    /// <summary>
    /// Apparence d'un acteur figée à un instant (lot AN1) : de quoi le dessiner alors qu'il a quitté le modèle de la carte,
    /// par exemple le combattant mort (<c>GA;103</c>), dont la cellule passe à -1 aussitôt le paquet reçu. Immuable.
    /// </summary>
    public sealed class ActorSnapshot
    {
        private static readonly string[] NoColors = { "-1", "-1", "-1" };

        public ActorSnapshot(long id, ActorKind kind, int gfx, short cellId, int direction, int scaleX, int scaleY, bool noFlip,
            IReadOnlyList<string> colors, IReadOnlyList<ActorAccessory> accessories, string name, bool isSelf)
        {
            Id = id; Kind = kind; Gfx = gfx; CellId = cellId; Direction = direction; ScaleX = scaleX; ScaleY = scaleY; NoFlip = noFlip;
            Colors = (colors ?? NoColors).Take(3).Concat(Enumerable.Repeat("-1", Math.Max(0, 3 - (colors?.Count ?? 3)))).ToArray();
            Accessories = (accessories ?? new ActorAccessory[0]).ToArray();
            Name = name ?? string.Empty; IsSelf = isSelf;
        }

        public long Id { get; }
        public ActorKind Kind { get; }
        public int Gfx { get; }
        /// <summary>Cellule où se trouvait l'acteur (-1 si elle n'était pas connue).</summary>
        public short CellId { get; }
        /// <summary>Orientation 0 à 7.</summary>
        public int Direction { get; }
        public int ScaleX { get; }
        public int ScaleY { get; }
        public bool NoFlip { get; }
        /// <summary>Couleurs 1 à 3 du <c>GM</c>, en hexadécimal, <c>-1</c> pour la couleur dessinée dans le SWF.</summary>
        public IReadOnlyList<string> Colors { get; }
        public IReadOnlyList<ActorAccessory> Accessories { get; }
        public string Name { get; }
        /// <summary>Personnage du compte.</summary>
        public bool IsSelf { get; }

        /// <summary>
        /// Instantané de l'acteur <paramref name="id"/> d'après la carte (acteur de <c>Map</c>, personnage du compte) et,
        /// à défaut, d'après le combattant connu (<paramref name="fighter"/>). Null si rien ne le décrit.
        /// </summary>
        public static ActorSnapshot Capture(Map map, CharacterClass self, long id, CombatFighter fighter = null)
        {
            MapActor actor = map?.GetActor(id);
            bool isSelf = self != null && self.id == id;
            if (actor == null && fighter == null && !isSelf) return null;
            int gfx = actor?.Gfx ?? 0;
            if (gfx <= 0 && isSelf) gfx = self.GFX > 0 ? self.GFX : self.Race_ID * 10 + self.Sex;
            if (gfx <= 0 && fighter != null) gfx = fighter.Gfx;
            int cell = actor?.Cell?.CellID ?? actor?.CellId ?? -1;
            if (cell < 0 && isSelf && self.Cell != null) cell = self.Cell.CellID;
            if (cell < 0 && fighter != null) cell = fighter.CellId;
            int direction = actor?.Orientation ?? (isSelf ? self.Orientation : fighter?.Orientation ?? 1);
            string[] colors = NoColors;
            IReadOnlyList<ActorAccessory> accessories = null;
            switch (actor)
            {
                case PlayerActor player: colors = new[] { player.Color1, player.Color2, player.Color3 }; accessories = player.Accessories; break;
                case NpcActor npc: colors = new[] { npc.Color1, npc.Color2, npc.Color3 }; accessories = npc.Accessories; break;
                case FightMonsterActor monster: colors = new[] { monster.Color1, monster.Color2, monster.Color3 }; accessories = monster.Accessories; break;
            }
            string name = actor?.DisplayName ?? (isSelf ? self.Name : fighter?.Name);
            ActorKind kind = actor?.Kind ?? (isSelf ? ActorKind.Player : ActorKind.Unknown);
            int scaleX = actor?.ScaleX ?? (isSelf ? self.GraphicsScaleX : 100), scaleY = actor?.ScaleY ?? (isSelf ? self.GraphicsScaleY : 100);
            return new ActorSnapshot(id, kind, gfx, (short)Math.Max(-1, Math.Min(short.MaxValue, cell)), ((direction % 8) + 8) % 8,
                scaleX, scaleY, actor?.NoFlip == true, colors, accessories, name, isSelf);
        }
    }

    /// <summary>
    /// Événement visuel (lot AN1) levé par le protocole sur le fil réseau, hors de tout verrou, après la mise à jour du modèle :
    /// la carte (<c>MapControl.Effects</c>) le rejoue à l'écran sans jamais retarder ni déclencher de paquet. Le même type sert
    /// au combat (<see cref="Fights.VisualEvent"/>), aux objets interactifs (<c>IQ</c>, 501), aux émotes (<c>eUK</c>) et aux
    /// effets de carte hors combat (<c>GA;208</c>, <c>GA;228</c>). Immuable.
    /// </summary>
    public sealed class VisualEvent
    {
        private static readonly string[] NoFields = new string[0];

        public VisualEvent(VisualSource source, int actionId, long actorId, long targetId, short cellId, IReadOnlyList<string> fields,
            int value = 0, int? durationMs = null, ActorSnapshot snapshot = null, bool inFight = false, long? queueId = null, string gameActionId = null)
        {
            Source = source; ActionId = actionId; ActorId = actorId; TargetId = targetId; CellId = cellId;
            Fields = (fields ?? NoFields).ToArray(); Value = value; DurationMs = durationMs; Snapshot = snapshot; InFight = inFight;
            QueueId = queueId ?? actorId; GameActionId = gameActionId ?? string.Empty;
        }

        public VisualSource Source { get; }
        /// <summary>Type de l'action <c>GA</c> (100, 103, 300…) ; 0 pour <c>IQ</c> et <c>eUK</c>.</summary>
        public int ActionId { get; }
        /// <summary>Acteur du <c>GA</c> (le personnage du compte si le champ est vide), sprite de <c>IQ</c>, acteur de <c>eUK</c>.</summary>
        public long ActorId { get; }
        /// <summary>Cible : premier champ des actions de vie, PA, PM et de la mort, acteur pour 104, cible d'un piège ; 0 sinon.</summary>
        public long TargetId { get; }
        /// <summary>Cellule visée (sort, corps à corps, 208, 228, 501, piège) ; -1 sinon.</summary>
        public short CellId { get; }
        /// <summary>Paramètres bruts : champs du <c>GA</c> découpés sur <c>,</c>, ou de <c>IQ</c>/<c>eUK</c> découpés sur <c>|</c>.</summary>
        public IReadOnlyList<string> Fields { get; }
        /// <summary>Variation de PV, PA ou PM (signée), quantité de <c>IQ</c>, numéro de l'émote ; 0 sinon.</summary>
        public int Value { get; }
        /// <summary>Durée annoncée : 501 (<c>cellule,durée</c>) et troisième champ facultatif de <c>eUK</c> (StarLoco n'en envoie pas).</summary>
        public int? DurationMs { get; }
        /// <summary>Instantané de la cible pris avant son retrait du modèle (mort) ; null sinon.</summary>
        public ActorSnapshot Snapshot { get; }
        public bool InFight { get; }
        /// <summary>
        /// Séquenceur du client qui reçoit l'action : en combat celui du joueur dont c'est le tour (<c>Game.currentPlayerID</c>),
        /// hors combat ou sans tour en cours celui de l'acteur.
        /// </summary>
        public long QueueId { get; }
        /// <summary>Identifiant d'action de jeu (premier champ du <c>GA</c>), vide sinon.</summary>
        public string GameActionId { get; }

        /// <summary>Champ <paramref name="index"/> lu comme entier ; faux s'il manque ou n'est pas un nombre.</summary>
        public bool TryField(int index, out int value)
        {
            value = 0;
            return index >= 0 && index < Fields.Count && int.TryParse(Fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public override string ToString() =>
            (Source == VisualSource.GameAction ? "GA " + ActionId.ToString(CultureInfo.InvariantCulture) : Source.ToString())
            + " acteur " + ActorId.ToString(CultureInfo.InvariantCulture) + " cible " + TargetId.ToString(CultureInfo.InvariantCulture)
            + " cellule " + CellId.ToString(CultureInfo.InvariantCulture) + " [" + string.Join(",", Fields) + "]";

    }

    /// <summary>
    /// Effet de sort ou de carte lu dans les champs d'un <c>GA</c> (lot AN1) : <c>GA;300</c>
    /// <c>sort,cellule,fichier,niveau,type,animation,devant</c> (StarLoco : <c>getSpriteInfos()</c> = <c>type,animation,devant</c>),
    /// <c>GA;208</c>/<c>GA;228</c> <c>cellule,fichier,type,animation,niveau</c> (toujours devant le sprite), <c>GA;303</c>
    /// <c>cellule[,fichier,type,devant]</c>. <see cref="Animation"/> reste brut : <c>-1</c> aucun effet, <c>-2</c> effet sans
    /// animation du lanceur, <c>a~b~c~d</c> bond aller-retour, sinon le numéro de <c>anim&lt;n&gt;</c>.
    /// </summary>
    public sealed class SpellLaunch
    {
        private SpellLaunch() { }
        public int SpellId { get; private set; }
        public short CellId { get; private set; } = -1;
        /// <summary>Fichier du clip (<c>clips/spells/&lt;fichier&gt;.swf</c>) ; vide si absent.</summary>
        public string File { get; private set; } = string.Empty;
        public int Level { get; private set; } = 1;
        /// <summary>Type d'effet (10 à 51), -1 s'il manque.</summary>
        public int Type { get; private set; } = -1;
        public string Animation { get; private set; } = string.Empty;
        public bool InFront { get; private set; }

        private static bool Int(IReadOnlyList<string> fields, int index, out int value)
        {
            value = 0;
            return fields != null && index < fields.Count && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string Text(IReadOnlyList<string> fields, int index) => fields != null && index < fields.Count ? fields[index] ?? string.Empty : string.Empty;

        private static bool Cell(IReadOnlyList<string> fields, int index, out short cell)
        {
            cell = -1;
            if (!Int(fields, index, out int value) || value < 0 || value > short.MaxValue) return false;
            cell = (short)value;
            return true;
        }

        /// <summary><c>GA;300</c> : faux si le sort ou la cellule sont illisibles (les autres champs gardent leur valeur par défaut).</summary>
        public static bool TryParseSpell(IReadOnlyList<string> fields, out SpellLaunch launch)
        {
            launch = null;
            if (!Int(fields, 0, out int spell) || !Cell(fields, 1, out short cell)) return false;
            launch = new SpellLaunch { SpellId = spell, CellId = cell, File = Text(fields, 2), Animation = Text(fields, 5), InFront = Text(fields, 6) == "1" };
            if (Int(fields, 3, out int level)) launch.Level = level;
            if (Int(fields, 4, out int type)) launch.Type = type;
            return true;
        }

        /// <summary><c>GA;208</c>, <c>GA;228</c> : faux si la cellule est illisible ; niveau 1 sans cinquième champ, comme le client.</summary>
        public static bool TryParseMapEffect(IReadOnlyList<string> fields, out SpellLaunch launch)
        {
            launch = null;
            if (!Cell(fields, 0, out short cell)) return false;
            launch = new SpellLaunch { CellId = cell, File = Text(fields, 1), Animation = Text(fields, 3), InFront = true };
            if (Int(fields, 2, out int type)) launch.Type = type;
            if (Int(fields, 4, out int level)) launch.Level = level;
            return true;
        }

        /// <summary><c>GA;303</c> : cellule, puis fichier, type et devant facultatifs (StarLoco n'envoie que la cellule).</summary>
        public static bool TryParseWeapon(IReadOnlyList<string> fields, out SpellLaunch launch)
        {
            launch = null;
            if (!Cell(fields, 0, out short cell)) return false;
            launch = new SpellLaunch { CellId = cell, File = Text(fields, 1), InFront = Text(fields, 3) == "1" };
            if (Int(fields, 2, out int type)) launch.Type = type;
            return true;
        }
    }

    /// <summary>Levée des <see cref="VisualEvent"/> : chaque abonné est appelé isolément.</summary>
    internal static class VisualEvents
    {
        /// <summary>Un abonné défaillant est journalisé et n'empêche ni les autres abonnés ni la lecture des paquets suivants.</summary>
        internal static void Raise(Action<VisualEvent> handlers, VisualEvent visual, Accounts.Accounts account, string reference)
        {
            if (handlers == null || visual == null) return;
            foreach (Action<VisualEvent> handler in handlers.GetInvocationList())
            {
                try { handler(visual); }
                catch (Exception error) when (!(error is OutOfMemoryException)) { account?.Logger?.LogException(reference, error); }
            }
        }
    }

    public sealed partial class Fights
    {
        /// <summary>
        /// Effet visuel d'une action de combat (lot AN1), levé sur le fil réseau après la mise à jour de l'état et le journal,
        /// jamais sous le verrou ; uniquement pendant un combat.
        /// </summary>
        public event Action<VisualEvent> VisualEvent;

        /// <summary>
        /// Lève <see cref="VisualEvent"/> pour l'action <paramref name="context"/> : la file est celle du joueur dont c'est le
        /// tour (sinon celle de l'acteur), comme le séquenceur <c>r11</c> de <c>GameActions.onActions</c>.
        /// </summary>
        internal void Visual(FightActionContext context, long targetId = 0, short cellId = -1, int value = 0, int? durationMs = null, ActorSnapshot snapshot = null)
        {
            if (context?.Packet == null) return;
            long queue;
            lock (sync)
            {
                if (!InFight || disposed) return;
                queue = actor != 0 ? actor : context.ActorId;
            }
            var visual = new VisualEvent(VisualSource.GameAction, context.ActionId, context.ActorId, targetId, cellId, context.Arguments,
                value, durationMs, snapshot, true, queue, context.Packet.GameActionId);
            VisualEvents.Raise(VisualEvent, visual, account, "COMBAT");
        }

        /// <summary>Instantané d'un combattant (carte puis modèle du combat), pris avant de le retirer de sa cellule.</summary>
        private ActorSnapshot Snapshot(int id)
        {
            CombatFighter fighter;
            lock (sync) { CombatFighter known; fighter = fighters.TryGetValue(id, out known) ? known.Copy() : null; }
            return ActorSnapshot.Capture(account.Game.Map, account.Game.character, id, fighter);
        }
    }
}
