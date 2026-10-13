using System;
using System.Collections.Generic;
using System.Globalization;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Acteur présent sur une carte, construit à partir d'une entrée <c>GM</c> (ou de <c>Gc+</c> pour les épées de combat).
    /// Les champs suivent <c>Game.onMovement</c> du client 1.34 et les formats envoyés par StarLoco ;
    /// l'interface historique <see cref="Entites"/> reste implémentée pour les menus, le rendu et le combat existants.
    /// </summary>
    public abstract class MapActor : Entites
    {
        private Cell cell;
        private int cellId = -1;

        /// <summary>Famille de l'acteur, déduite du champ type de <c>GM</c> (<c>-3</c>, <c>-4</c>… ou classe ≥ 0).</summary>
        public abstract ActorKind Kind { get; }
        /// <summary>Identifiant du sprite sur la carte (négatif pour les PNJ, groupes, percepteurs… chez StarLoco).</summary>
        public long Id { get; set; }
        /// <summary>Identifiant historique sur 32 bits (<see cref="Entites.id"/>).</summary>
        public int id { get => unchecked((int)Id); set => Id = value; }
        /// <summary>Cellule résolue sur la carte chargée ; null si la carte est absente des ressources ou la cellule inconnue.</summary>
        public Cell Cell
        {
            get => cell;
            set { cell = value; cellId = value != null ? value.CellID : -1; OnCellChanged(); }
        }
        /// <summary>Cellule annoncée par le serveur, conservée même lorsque la carte n'est pas chargée.</summary>
        public int CellId
        {
            get => cellId;
            set { cellId = value; if (cell != null && cell.CellID != value) cell = null; OnCellChanged(); }
        }
        /// <summary>Orientation 0 à 7 telle que transmise par le serveur (<c>GM</c>, <c>eD</c>, dernier pas d'un chemin).</summary>
        public int Orientation { get; set; } = 2;
        public int Gfx { get; set; }
        public int ScaleX { get; set; } = 100;
        public int ScaleY { get; set; } = 100;
        public string Name { get; set; }
        /// <summary>Vrai pour le personnage du compte (il n'est pas rangé dans <c>Map.Actors</c> mais dans <c>Map.Self</c>).</summary>
        public bool IsSelf { get; set; }
        /// <summary>Champ type brut de <c>GM</c> (index 5 avant la virgule du titre) : <c>-1</c> à <c>-10</c> ou numéro de classe.</summary>
        public int SpriteType { get; set; }
        /// <summary>Suffixe <c>*</c> du champ graphique : le sprite n'est pas retourné en miroir.</summary>
        public bool NoFlip { get; set; }
        /// <summary>Faux lorsque le champ graphique commence par <c>*</c> (pas de mode fantôme).</summary>
        public bool AllowGhostMode { get; set; } = true;
        /// <summary>Sprites liés (familier suiveur d'un joueur, membres d'un groupe) dans l'ordre du paquet, sans le sprite principal.</summary>
        public IReadOnlyList<GfxPart> LinkedSprites { get; set; } = new GfxPart[0];
        /// <summary>Disposition des sprites liés : <c>circle</c> (séparateur <c>,</c>), <c>line</c> (<c>:</c>) ou <c>none</c>.</summary>
        public string LinkedShape { get; set; } = "none";
        /// <summary>Entrée <c>GM</c> d'origine, sans l'opérateur <c>+</c>/<c>~</c>.</summary>
        public string RawEntry { get; set; }

        public virtual string DisplayName => string.IsNullOrEmpty(Name) ? "Acteur #" + Id.ToString(CultureInfo.InvariantCulture) : Name;

        public override string ToString() => Kind + " " + Id.ToString(CultureInfo.InvariantCulture) + " « " + DisplayName + " » cellule " + CellId;

        /// <summary>Appelé après chaque changement de <see cref="Cell"/> ou de <see cref="CellId"/>.</summary>
        protected virtual void OnCellChanged() { }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) cell = null;
        }
    }

    /// <summary>Un sprite d'un champ graphique <c>gfx^taille</c> ou <c>gfx^largeurxhauteur</c>.</summary>
    public sealed class GfxPart
    {
        public GfxPart(int gfx, int scaleX, int scaleY) { Gfx = gfx; ScaleX = scaleX; ScaleY = scaleY; }
        public int Gfx { get; }
        public int ScaleX { get; }
        public int ScaleY { get; }
    }

    /// <summary>
    /// Accessoire visible (champ « stuff » de <c>GM</c> et paquet <c>Oa</c>) : modèle d'objet en hexadécimal,
    /// suivi pour les objets vivants de <c>~type~image</c> (image ramenée à partir de 0 comme dans <c>Items.onAccessories</c>).
    /// </summary>
    public sealed class ActorAccessory
    {
        public ActorAccessory(int slot, int templateId, int? type, int? frame) { Slot = slot; TemplateId = templateId; Type = type; Frame = frame; }
        /// <summary>Position dans la liste : 0 arme, 1 coiffe, 2 cape, 3 familier, 4 bouclier.</summary>
        public int Slot { get; }
        public int TemplateId { get; }
        public int? Type { get; }
        public int? Frame { get; }

        /// <summary>Lit <c>a,b,c,d,e</c> ; les emplacements vides ou illisibles sont omis.</summary>
        public static IReadOnlyList<ActorAccessory> ParseList(string value)
        {
            var result = new List<ActorAccessory>();
            if (string.IsNullOrEmpty(value)) return result;
            string[] slots = value.Split(',');
            for (int slot = 0; slot < slots.Length; slot++)
            {
                string[] parts = slots[slot].Split('~');
                if (!int.TryParse(parts[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int template) || template <= 0) continue;
                int? type = null, frame = null;
                if (parts.Length > 2)
                {
                    if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedType)) type = parsedType;
                    if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedFrame)) frame = Math.Max(0, parsedFrame - 1);
                }
                result.Add(new ActorAccessory(slot, template, type, frame));
            }
            return result;
        }
    }

    /// <summary>Accès au modèle d'acteur depuis l'interface historique <see cref="Entites"/>.</summary>
    public static class EntitesExtensions
    {
        /// <summary>Famille de l'entité : celle de <see cref="MapActor"/>, <see cref="ActorKind.Player"/> pour le personnage du compte.</summary>
        public static ActorKind GetKind(this Entites entity)
        {
            if (entity is MapActor actor) return actor.Kind;
            return entity is CharacterClass ? ActorKind.Player : ActorKind.Unknown;
        }

        public static MapActor AsActor(this Entites entity) => entity as MapActor;

        public static bool IsSelfActor(this Entites entity) => entity is CharacterClass || (entity as MapActor)?.IsSelf == true;
    }
}
