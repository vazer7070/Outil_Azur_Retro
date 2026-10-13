using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Perso;
using Tool_BotProtocol.Utils.Crypto;

namespace Tool_BotProtocol.Game.Maps
{
    public enum GmOperation { Add, Update, Remove }

    /// <summary>Une entrée de <c>GM</c> : <c>+</c> ajout, <c>~</c> remplacement, <c>-</c> retrait.</summary>
    public sealed class GmEntry
    {
        public GmEntry(GmOperation operation, long id, MapActor actor, string raw, string[] fields)
        {
            Operation = operation; Id = id; Actor = actor; Raw = raw; Fields = fields ?? new string[0];
        }
        public GmOperation Operation { get; }
        public long Id { get; }
        /// <summary>Acteur lu (null pour un retrait).</summary>
        public MapActor Actor { get; }
        public string Raw { get; }
        /// <summary>Champs séparés par <c>;</c> (après l'opérateur), transmis tels quels au suivi du combat.</summary>
        public string[] Fields { get; }
    }

    public sealed class GmParseResult
    {
        public List<GmEntry> Entries { get; } = new List<GmEntry>();
        /// <summary>Entrées ignorées et raison (paquet mal formé) : jamais d'exception.</summary>
        public List<string> Rejected { get; } = new List<string>();
    }

    /// <summary>Chemin reçu dans <c>GA&lt;id&gt;;1;&lt;acteur&gt;;a&lt;cellule de départ&gt;&lt;chemin&gt;</c>.</summary>
    public sealed class ServerMovePath
    {
        public string Encoded { get; set; }
        /// <summary>Cellule de départ annoncée par le premier triplet (<c>a</c> + cellule chez StarLoco).</summary>
        public int StartCellId { get; set; }
        /// <summary>Chemin compressé sans le préfixe de départ : un triplet direction + cellule par inflexion.</summary>
        public string Path { get; set; }
        /// <summary>Orientation du dernier pas (celle que garde l'acteur à l'arrivée).</summary>
        public int FinalOrientation { get; set; }
    }

    public sealed class GroundObject
    {
        public GroundObject(int cellId, int itemTemplateId, int type, int? durability, int? durabilityMax)
        {
            CellId = cellId; ItemTemplateId = itemTemplateId; Type = type; Durability = durability; DurabilityMax = durabilityMax;
        }
        public int CellId { get; }
        public int ItemTemplateId { get; }
        /// <summary>0 objet posé ; 1 objet avec durabilité (champs 4 et 5).</summary>
        public int Type { get; }
        public int? Durability { get; }
        public int? DurabilityMax { get; }
    }

    /// <summary>État d'un objet interactif reçu dans <c>GDF</c>.</summary>
    public sealed class InteractiveObjectState
    {
        public InteractiveObjectState(int cellId, int state, bool? interactive) { CellId = cellId; State = state; Interactive = interactive; }
        public int CellId { get; }
        /// <summary>Image de l'objet : 1 plein, 2 en cours de vidage (porte : ouverture), 3 vide (porte ouverte), 4 vide 2 (porte : fermeture), 5 repousse.</summary>
        public int State { get; }
        /// <summary>Troisième champ : objet utilisable (1) ou non (0) ; null lorsqu'il est absent (portes).</summary>
        public bool? Interactive { get; }
    }

    /// <summary>Mise à jour d'une cellule reçue dans <c>GDC&lt;cellule&gt;;&lt;données&gt;;&lt;permanent&gt;</c>.</summary>
    public sealed class CellUpdate
    {
        public CellUpdate(int cellId, string data, string mask, bool permanent) { CellId = cellId; Data = data; Mask = mask; Permanent = permanent; }
        public int CellId { get; }
        /// <summary>Dix caractères de cellule ; null pour « revenir à l'état initial » (<c>GDC&lt;cellule&gt;</c> seul).</summary>
        public string Data { get; }
        /// <summary>Masque hexadécimal des champs à remplacer (<c>801</c> : déplacement + interactif).</summary>
        public string Mask { get; }
        public bool Permanent { get; }
    }

    /// <summary>
    /// Lecture des paquets d'acteurs et d'objets de la carte, d'après <c>Game.onMovement</c>, <c>onChallenge</c>,
    /// <c>onCellObject</c>, <c>onCellData</c>, <c>onFrameObject2</c>, <c>Emotes.onDirection</c> et <c>Items.onAccessories</c>
    /// du client 1.34, et les formats envoyés par StarLoco. Aucune méthode ne lève d'exception sur un paquet mal formé.
    /// </summary>
    public static class GmParser
    {
        private const int MaxScale = 500;

        /// <summary>
        /// Découpe un paquet <c>GM</c> (préfixe facultatif) : plusieurs <c>GM</c> concaténés par NUL, puis les entrées séparées par <c>|</c>.
        /// </summary>
        /// <param name="inFight">Disposition des champs de combat (joueurs, monstres, percepteurs).</param>
        /// <param name="selfId">Identifiant du personnage du compte : son acteur est marqué <see cref="MapActor.IsSelf"/>.</param>
        /// <param name="selfCellId">Cellule utilisée pour <c>-1</c> (cellule du personnage dans le client).</param>
        public static GmParseResult Parse(string packet, bool inFight, long selfId = 0, int selfCellId = -1)
        {
            var result = new GmParseResult();
            if (string.IsNullOrEmpty(packet)) return result;
            foreach (string chunk in packet.Split('\0'))
            {
                string body = chunk.StartsWith("GM", StringComparison.Ordinal) ? chunk.Substring(2) : chunk;
                foreach (string entry in body.Split('|'))
                {
                    // StarLoco double le préfixe des percepteurs (« GM|GM|+… ») : le jeton « GM » n'est pas une entrée.
                    if (entry.Length == 0 || entry == "GM") continue;
                    char operation = entry[0];
                    if (operation == '-')
                    {
                        if (TryLong(entry.Substring(1), out long removed)) result.Entries.Add(new GmEntry(GmOperation.Remove, removed, null, entry, null));
                        else result.Rejected.Add(Describe(entry, "identifiant de retrait illisible"));
                        continue;
                    }
                    if (operation != '+' && operation != '~')
                    {
                        result.Rejected.Add(Describe(entry, "opérateur inconnu"));
                        continue;
                    }
                    string[] fields = entry.Substring(1).Split(';');
                    MapActor actor = ParseActor(fields, inFight, selfId, selfCellId, out string error);
                    if (actor == null) { result.Rejected.Add(Describe(entry, error)); continue; }
                    actor.RawEntry = entry.Substring(1);
                    result.Entries.Add(new GmEntry(operation == '+' ? GmOperation.Add : GmOperation.Update, actor.Id, actor, entry, fields));
                }
            }
            return result;
        }

        /// <summary>Lit une entrée <c>GM</c> sans son opérateur ; null et <paramref name="error"/> renseigné si elle est inexploitable.</summary>
        public static MapActor ParseActor(string[] f, bool inFight, long selfId, int selfCellId, out string error)
        {
            error = null;
            if (f == null || f.Length < 6) { error = "moins de six champs"; return null; }
            if (!TryInt(f[0], out int cell)) { error = "cellule illisible"; return null; }
            if (cell == -1) cell = selfCellId;
            if (!TryLong(f[3], out long id)) { error = "identifiant illisible"; return null; }
            string[] typeField = f[5].Split(',');
            if (!TryInt(typeField[0], out int type)) { error = "type illisible"; return null; }
            int orientation = TryInt(f[1], out int direction) && direction >= 0 && direction <= 7 ? direction : 2;
            GraphicsField graphics = ReadGraphicsField(Field(f, 6));

            MapActor actor;
            switch (type)
            {
                case -1:
                case -2: actor = ReadFightMonster(f, type, out error); break;
                case -3: actor = ReadMonsterGroup(f, graphics, out error); break;
                case -4: actor = ReadNpc(f, out error); break;
                case -5: actor = ReadMerchant(f); break;
                case -6: actor = ReadCollector(f, inFight); break;
                case -9: actor = ReadParkMount(f); break;
                case -10: actor = ReadPrism(f); break;
                default:
                    actor = type >= 0 ? ReadPlayer(f, type, typeField, inFight) : (MapActor)new UnknownActor { Fields = (string[])f.Clone() };
                    break;
            }
            if (actor == null) return null;
            actor.Id = id;
            actor.CellId = cell;
            actor.Orientation = orientation;
            actor.SpriteType = type;
            actor.NoFlip = graphics.NoFlip;
            actor.AllowGhostMode = graphics.AllowGhostMode;
            actor.LinkedShape = graphics.Shape;
            if (graphics.Parts.Count > 0)
            {
                GfxPart main = graphics.Parts[0];
                if (main.Gfx > 0) actor.Gfx = main.Gfx;
                actor.ScaleX = main.ScaleX; actor.ScaleY = main.ScaleY;
                if (!(actor is MonsterGroupActor)) actor.LinkedSprites = graphics.Parts.Skip(1).ToArray();
            }
            if (actor.Name == null) actor.Name = f[4];
            if (actor is Perso.Personnages person && !(actor is MerchantActor) && person.Gfx <= 0) person.Gfx = person.Race_ID * 10 + person.Sexe;
            if (actor is MonsterGroupActor group) group.Members = group.Members; // recalcule les membres historiques avec la position finale
            actor.IsSelf = selfId != 0 && id == selfId;
            return actor;
        }

        private static MapActor ReadPlayer(string[] f, int type, string[] typeField, bool inFight)
        {
            var player = new PlayerActor
            {
                ClassId = type,
                Race_ID = type > 0 && type <= byte.MaxValue ? (byte)type : (byte)0,
                InFight = inFight,
                Name = f[4]
            };
            if (typeField.Length > 1 && typeField[1].Length > 0)
            {
                string[] title = typeField[1].Split('*');
                if (TryInt(title[0], out int titleId)) player.TitleId = titleId;
                if (title.Length > 1) player.TitleParameter = title[1];
            }
            if (byte.TryParse(Field(f, 7), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte sex)) player.Sexe = sex;
            int offset;
            if (inFight)
            {
                if (TryInt(Field(f, 8), out int level)) player.Level = level;
                player.Alignment = ActorAlignment.Parse(Field(f, 9));
                offset = 10;
            }
            else
            {
                player.Alignment = ActorAlignment.Parse(Field(f, 8));
                offset = 9;
            }
            player.Color1 = Field(f, offset, "-1"); player.Color2 = Field(f, offset + 1, "-1"); player.Color3 = Field(f, offset + 2, "-1");
            player.Stuff = Field(f, offset + 3);
            player.Accessories = ActorAccessory.ParseList(player.Stuff);
            if (inFight)
            {
                player.Life = OptionalInt(Field(f, 14)); player.ActionPoints = OptionalInt(Field(f, 15)); player.MovementPoints = OptionalInt(Field(f, 16));
                player.Resistances = ReadResistances(f, 17);
                player.Team = OptionalInt(Field(f, 24));
                SetMount(player, Field(f, 25));
            }
            else
            {
                player.Aura = OptionalInt(Field(f, 13)) ?? 0;
                player.Emote = Field(f, 14); player.EmoteTimer = Field(f, 15);
                player.GuildName = Field(f, 16); player.GuildEmblem = Field(f, 17);
                player.RestrictionsRaw = Field(f, 18);
                if (PlayerRestrictionsParser.TryParse(player.RestrictionsRaw, out int restrictions, out PlayerRestrictions known))
                { player.RestrictionsValue = restrictions; player.Restrictions = known; }
                SetMount(player, Field(f, 19));
            }
            return player;
        }

        private static void SetMount(PlayerActor player, string value)
        {
            player.MountRaw = value ?? string.Empty;
            player.MountModelId = OptionalInt(player.MountRaw.Split(',')[0]);
        }

        private static MapActor ReadFightMonster(string[] f, int type, out string error)
        {
            error = null;
            if (!TryInt(f[4], out int template)) { error = "modèle de monstre illisible"; return null; }
            var monster = new FightMonsterActor(0, template) { IsCreature = type == -1 };
            monster.Grade = OptionalInt(Field(f, 7)) ?? 0;
            // Le GM de combat ne porte que le grade : comme le client (Level = monsters_fr[id].g<grade>.l), le niveau vient du grade.
            monster.Level = Tool_BotProtocol.Game.Data.LangData.Monster.Grade(template, monster.Grade)?.Level ?? 0;
            monster.Color1 = Field(f, 8, "-1"); monster.Color2 = Field(f, 9, "-1"); monster.Color3 = Field(f, 10, "-1");
            monster.AccessoriesRaw = Field(f, 11);
            monster.Accessories = ActorAccessory.ParseList(monster.AccessoriesRaw);
            monster.Life = OptionalInt(Field(f, 12)); monster.ActionPoints = OptionalInt(Field(f, 13)); monster.MovementPoints = OptionalInt(Field(f, 14));
            if (f.Length > 18) { monster.Resistances = ReadResistances(f, 15); monster.Team = OptionalInt(Field(f, 22)); }
            else monster.Team = OptionalInt(Field(f, 15));
            return monster;
        }

        private static MapActor ReadMonsterGroup(string[] f, GraphicsField graphics, out string error)
        {
            error = null;
            string[] templates = f[4].Split(','), levels = Field(f, 7).Split(',');
            if (f.Length < 8 || templates.Length != levels.Length) { error = "modèles et niveaux du groupe incohérents"; return null; }
            var members = new List<MonsterGroupMember>();
            for (int i = 0; i < templates.Length; i++)
            {
                if (!TryInt(templates[i], out int template) || !TryInt(levels[i], out int level)) { error = "membre du groupe illisible"; return null; }
                var member = new MonsterGroupMember { TemplateId = template, Level = level,
                    Colors = Field(f, 8 + 2 * i), Accessories = Field(f, 9 + 2 * i) };
                if (i < graphics.Parts.Count)
                {
                    member.Gfx = graphics.Parts[i].Gfx; member.ScaleX = graphics.Parts[i].ScaleX; member.ScaleY = graphics.Parts[i].ScaleY;
                }
                members.Add(member);
            }
            var group = new MonsterGroupActor(0) { Stars = OptionalInt(f[2]) ?? 0 };
            group.Members = members;
            return group;
        }

        private static MapActor ReadNpc(string[] f, out string error)
        {
            error = null;
            if (!TryInt(f[4], out int template)) { error = "modèle de PNJ illisible"; return null; }
            var npc = new NpcActor(0, template);
            if (TryInt(Field(f, 7), out int sex)) npc.Sexe = sex;
            npc.Color1 = Field(f, 8, "-1"); npc.Color2 = Field(f, 9, "-1"); npc.Color3 = Field(f, 10, "-1");
            npc.AccessoriesRaw = Field(f, 11);
            npc.Accessories = ActorAccessory.ParseList(npc.AccessoriesRaw);
            npc.ExtraClip = OptionalInt(Field(f, 12)) ?? -1;
            npc.Artwork = OptionalInt(Field(f, 13)) ?? 0;
            if (NPC.PNJ.AllPNJ.TryGetValue(template, out NPC.PNJ model) && model != null && model.GFX > 0) npc.GFX = model.GFX;
            return npc;
        }

        private static MapActor ReadMerchant(string[] f)
        {
            var merchant = new MerchantActor { Name = f[4] };
            merchant.Color1 = Field(f, 7, "-1"); merchant.Color2 = Field(f, 8, "-1"); merchant.Color3 = Field(f, 9, "-1");
            merchant.Stuff = Field(f, 10);
            merchant.Accessories = ActorAccessory.ParseList(merchant.Stuff);
            merchant.GuildName = Field(f, 11); merchant.GuildEmblem = Field(f, 12);
            merchant.OfflineType = OptionalInt(Field(f, 13)) ?? 0;
            return merchant;
        }

        private static MapActor ReadCollector(string[] f, bool inFight)
        {
            var collector = new CollectorActor { Level = OptionalInt(Field(f, 7)) ?? 0 };
            collector.SetNameIds(f[4]);
            if (inFight)
            {
                collector.Life = OptionalInt(Field(f, 8)); collector.ActionPoints = OptionalInt(Field(f, 9)); collector.MovementPoints = OptionalInt(Field(f, 10));
                collector.Team = OptionalInt(Field(f, 18));
            }
            else
            {
                collector.GuildName = Field(f, 8); collector.GuildEmblem = Field(f, 9);
            }
            collector.Name = collector.DisplayName;
            return collector;
        }

        private static MapActor ReadParkMount(string[] f) => new ParkMountActor
        {
            Name = f[4], OwnerName = Field(f, 7), Level = OptionalInt(Field(f, 8)) ?? 0, ModelId = OptionalInt(Field(f, 9)) ?? 0
        };

        private static MapActor ReadPrism(string[] f) => new PrismActor
        {
            Name = f[4], LinkedMonsterId = OptionalInt(f[4]), Level = OptionalInt(Field(f, 7)) ?? 0,
            AlignmentValue = OptionalInt(Field(f, 8)) ?? 0, AlignmentSide = OptionalInt(Field(f, 9)) ?? 0
        };

        private static int[] ReadResistances(string[] f, int start)
        {
            if (f.Length < start + 7) return null;
            var values = new int[7];
            for (int i = 0; i < 7; i++) values[i] = OptionalInt(f[start + i]) ?? 0;
            return values;
        }

        private sealed class GraphicsField
        {
            public bool NoFlip;
            public bool AllowGhostMode = true;
            public string Shape = "none";
            public List<GfxPart> Parts = new List<GfxPart>();
        }

        /// <summary>
        /// Champ graphique : <c>*</c> final = pas de miroir, <c>*</c> initial = pas de mode fantôme, puis
        /// <c>sliptGfxData</c> (<c>,</c> cercle, <c>:</c> ligne) et <c>splitGfxForScale</c> pour chaque sprite.
        /// </summary>
        private static GraphicsField ReadGraphicsField(string value)
        {
            var field = new GraphicsField();
            if (string.IsNullOrEmpty(value)) return field;
            if (value.EndsWith("*", StringComparison.Ordinal)) { value = value.Substring(0, value.Length - 1); field.NoFlip = true; }
            if (value.StartsWith("*", StringComparison.Ordinal)) { value = value.Substring(1); field.AllowGhostMode = false; }
            string[] parts;
            if (value.IndexOf(',') >= 0) { field.Shape = "circle"; parts = value.Split(','); }
            else if (value.IndexOf(':') >= 0) { field.Shape = "line"; parts = value.Split(':'); }
            else parts = new[] { value };
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0 && parts[i].Length == 0) continue;
                field.Parts.Add(ReadGfx(parts[i]));
            }
            return field;
        }

        /// <summary><c>gfx</c>, <c>gfx^taille</c> ou <c>gfx^largeurxhauteur</c> (tailles bornées à 0-500).</summary>
        public static GfxPart ReadGfx(string value)
        {
            string[] graphic = (value ?? string.Empty).Split('^');
            int gfx = graphic.Length == 2 || graphic.Length == 1 ? (OptionalInt(graphic[0]) ?? 0) : 0;
            if (gfx < 0) gfx = 0;
            int scaleX = 100, scaleY = 100;
            if (graphic.Length == 2)
            {
                if (TryInt(graphic[1], out int both)) scaleX = scaleY = both;
                else
                {
                    string[] scale = graphic[1].Split('x');
                    if (scale.Length == 2)
                    {
                        scaleX = OptionalInt(scale[0]) ?? 100;
                        scaleY = OptionalInt(scale[1]) ?? 100;
                    }
                }
            }
            return new GfxPart(gfx, Math.Max(0, Math.Min(MaxScale, scaleX)), Math.Max(0, Math.Min(MaxScale, scaleY)));
        }

        /// <summary>
        /// Chemin d'un déplacement reçu : <c>a&lt;cellule&gt;</c> puis un triplet direction + cellule par inflexion.
        /// Faux si la longueur n'est pas un multiple de trois ou si un caractère sort de l'alphabet du client.
        /// </summary>
        public static bool TryParseMovePath(string value, out ServerMovePath path)
        {
            path = null;
            if (string.IsNullOrEmpty(value) || value.Length < 3 || value.Length % 3 != 0) return false;
            if (value.Any(character => Array.IndexOf(Hash.caracteres_array, character) < 0)) return false;
            int last = value.Length - 3;
            int direction = Array.IndexOf(Hash.caracteres_array, value[last]);
            if (direction > 7) return false;
            path = new ServerMovePath
            {
                Encoded = value,
                StartCellId = CellFromCode(value, 1),
                Path = value.Substring(3),
                FinalOrientation = direction
            };
            return true;
        }

        /// <summary><c>Gc+&lt;combat&gt;;&lt;type&gt;|&lt;équipe&gt;;&lt;cellule&gt;;&lt;typeÉquipe&gt;;&lt;alignement&gt;|…</c> ; null si illisible.</summary>
        public static FightSwordsActor ParseFightSwords(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return null;
            string[] parts = payload.Split('|');
            string[] header = parts[0].Split(';');
            if (!TryLong(header[0], out long fightId)) return null;
            int fightType = OptionalInt(Field(header, 1)) ?? 0;
            var teams = new List<FightTeamFlag>();
            for (int i = 1; i < parts.Length; i++)
            {
                string[] team = parts[i].Split(';');
                if (team.Length < 2 || !TryLong(team[0], out long teamId) || !TryInt(team[1], out int cell)) continue;
                teams.Add(new FightTeamFlag(teamId, cell, OptionalInt(Field(team, 2)) ?? 0, OptionalInt(Field(team, 3)) ?? -1));
            }
            return new FightSwordsActor(fightId, fightType, teams);
        }

        /// <summary><c>GDO+&lt;cellule&gt;;&lt;modèle&gt;;&lt;type&gt;[;&lt;durabilité&gt;;&lt;max&gt;]|…</c> ou <c>GDO-&lt;cellule&gt;…</c> (sans le préfixe <c>GDO</c>).</summary>
        public static bool TryParseGroundObjects(string payload, out bool added, out List<GroundObject> objects)
        {
            objects = new List<GroundObject>();
            added = payload != null && payload.Length > 0 && payload[0] == '+';
            if (string.IsNullOrEmpty(payload) || (payload[0] != '+' && payload[0] != '-')) return false;
            foreach (string entry in payload.Substring(1).Split('|'))
            {
                string[] fields = entry.Split(';');
                if (!TryInt(fields[0], out int cell)) continue;
                int item = OptionalInt(Field(fields, 1)) ?? 0;
                int type = OptionalInt(Field(fields, 2)) ?? 0;
                int? durability = type == 1 ? OptionalInt(Field(fields, 3)) : null;
                int? maximum = type == 1 ? OptionalInt(Field(fields, 4)) : null;
                objects.Add(new GroundObject(cell, item, type, durability, maximum));
            }
            return true;
        }

        /// <summary><c>GDC&lt;cellule&gt;;&lt;données&gt;;&lt;0|1&gt;|…</c> (sans le préfixe) ; les données font dix caractères suivis du masque.</summary>
        public static List<CellUpdate> ParseCellUpdates(string payload)
        {
            var updates = new List<CellUpdate>();
            if (string.IsNullOrEmpty(payload)) return updates;
            foreach (string entry in payload.Split('|'))
            {
                string[] fields = entry.Split(';');
                if (!TryInt(fields[0], out int cell) || cell < 0) continue;
                string data = Field(fields, 1);
                if (data.Length == 0) { updates.Add(new CellUpdate(cell, null, null, false)); continue; }
                if (data.Length < 10 || data.Substring(0, 10).Any(character => Array.IndexOf(Hash.caracteres_array, character) < 0)) continue;
                updates.Add(new CellUpdate(cell, data.Substring(0, 10), data.Substring(10), Field(fields, 2) != "0"));
            }
            return updates;
        }

        /// <summary><c>GDF|&lt;cellule&gt;;&lt;état&gt;[;&lt;1|0&gt;]|…</c> (sans <c>GDF</c>) : tous les triplets, troisième champ facultatif.</summary>
        public static List<InteractiveObjectState> ParseObjectStates(string payload)
        {
            var states = new List<InteractiveObjectState>();
            if (string.IsNullOrEmpty(payload)) return states;
            foreach (string entry in payload.TrimStart('|').Split('|'))
            {
                string[] fields = entry.Split(';');
                if (fields.Length < 2 || !TryInt(fields[0], out int cell) || !TryInt(fields[1], out int state)) continue;
                bool? interactive = fields.Length > 2 && fields[2].Length > 0 ? fields[2] == "1" : (bool?)null;
                states.Add(new InteractiveObjectState(cell, state, interactive));
            }
            return states;
        }

        /// <summary><c>eD&lt;acteur&gt;|&lt;direction&gt;</c> et <c>Oa&lt;acteur&gt;|&lt;stuff&gt;</c> (sans le préfixe).</summary>
        public static bool TrySplitActorPayload(string payload, out long actorId, out string value)
        {
            actorId = 0; value = null;
            if (string.IsNullOrEmpty(payload)) return false;
            int separator = payload.IndexOf('|');
            if (separator <= 0 || !TryLong(payload.Substring(0, separator), out actorId)) return false;
            value = payload.Substring(separator + 1);
            return true;
        }

        internal static int CellFromCode(string value, int index)
        {
            int high = Array.IndexOf(Hash.caracteres_array, value[index]);
            int low = Array.IndexOf(Hash.caracteres_array, value[index + 1]);
            return high < 0 || low < 0 ? -1 : high * 64 + low;
        }

        private static string Field(string[] fields, int index, string fallback = "") =>
            fields != null && index >= 0 && index < fields.Length && fields[index] != null ? fields[index] : fallback;

        private static bool TryInt(string value, out int result) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        private static bool TryLong(string value, out long result) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

        private static int? OptionalInt(string value) => TryInt(value, out int result) ? result : (int?)null;

        private static string Describe(string entry, string reason) =>
            (entry.Length > 60 ? entry.Substring(0, 60) + "…" : entry) + " (" + reason + ")";
    }
}
