using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps.Entities;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Clip réservé à un accessoire dans une image d'une bande d'acteur (ligne de <c>Accessoires/emplacements/&lt;gfx&gt;.tsv</c>,
    /// relevée par <c>exporter_sprites.py --emplacements</c> d'après les appels <c>GAC.applyAccessory</c> du client 1.34). La
    /// matrice (<see cref="A"/>…<see cref="Ty"/>) pose le repère de l'accessoire dans celui du sprite (pixels, origine au
    /// pied) ; <see cref="TxMirror"/>/<see cref="TyMirror"/> remplacent la translation quand le sprite est retourné et que
    /// le clip est de ceux dont le client inverse <c>_x</c> (familiers). <see cref="Front"/> : dessiné devant le corps.
    /// </summary>
    public sealed class AccessorySlot
    {
        internal AccessorySlot(int slot, string label, bool mirror, float a, float b, float c, float d, float tx, float ty, float txMirror, float tyMirror, bool front)
        {
            Slot = slot; Label = label; Mirror = mirror; A = a; B = b; C = c; D = d; Tx = tx; Ty = ty; TxMirror = txMirror; TyMirror = tyMirror; Front = front;
        }

        /// <summary>Emplacement : 0 arme, 1 coiffe, 2 cape, 3 familier, 4 bouclier.</summary>
        public int Slot { get; }
        /// <summary>Étiquette sur laquelle le client arrête l'accessoire (<c>R</c>, <c>L</c>, <c>WL</c>, <c>RR</c>…).</summary>
        public string Label { get; }
        /// <summary>Clip dont le client inverse <c>_x</c> quand le sprite est retourné (orientations 3, 4 et 7).</summary>
        public bool Mirror { get; }
        public float A { get; }
        public float B { get; }
        public float C { get; }
        public float D { get; }
        public float Tx { get; }
        public float Ty { get; }
        public float TxMirror { get; }
        public float TyMirror { get; }
        /// <summary>Dessiné devant le corps (sinon derrière).</summary>
        public bool Front { get; }
        /// <summary>Déterminant de la matrice : nul, le clip n'a pas de surface.</summary>
        public float Determinant => A * D - B * C;
    }

    /// <summary>
    /// Table des clips réservés d'un gfx : pour chaque bande (<c>staticR</c>, <c>walkL</c>…) et chaque image de la bande, les
    /// emplacements posés dans cette image. Immuable ; lecture depuis n'importe quel fil.
    /// </summary>
    public sealed class AccessoryPlacements
    {
        private static readonly AccessorySlot[] NoSlots = new AccessorySlot[0];
        /// <summary>Lignes au-delà desquelles un fichier est refusé (table abîmée ou étrangère).</summary>
        public const int MaxRows = 1000000;

        public static readonly AccessoryPlacements Empty = new AccessoryPlacements(new Dictionary<string, AccessorySlot[]>(StringComparer.OrdinalIgnoreCase), 0, null);

        private readonly Dictionary<string, AccessorySlot[]> rows;

        private AccessoryPlacements(Dictionary<string, AccessorySlot[]> rows, int count, string reason) { this.rows = rows; Count = count; Reason = reason; }

        /// <summary>Nombre de clips (lignes retenues).</summary>
        public int Count { get; }
        /// <summary>Cause d'une table vide (fichier illisible), ou <c>null</c>.</summary>
        public string Reason { get; }

        /// <summary>Clips de l'image <paramref name="frame"/> (à partir de 0) de la bande <paramref name="band"/> ; vide si aucun.</summary>
        public IReadOnlyList<AccessorySlot> At(string band, int frame) =>
            band != null && rows.TryGetValue(band + "\t" + frame.ToString(CultureInfo.InvariantCulture), out AccessorySlot[] slots) ? slots : NoSlots;

        /// <summary>Bandes qui ont au moins un clip.</summary>
        public IEnumerable<string> Bands => rows.Keys.Select(key => key.Substring(0, key.IndexOf('\t'))).Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>Lit <c>&lt;gfx&gt;.tsv</c> ; une table vide (avec <see cref="Reason"/>) si le fichier manque ou est illisible. Ne lève jamais.</summary>
        public static AccessoryPlacements Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return new AccessoryPlacements(Empty.rows, 0, "Fichier d'emplacements absent : " + Path.GetFileName(path) + ".");
                return Parse(File.ReadLines(path));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException
                || error is NotSupportedException || error is System.Security.SecurityException)
            {
                return new AccessoryPlacements(Empty.rows, 0, "Emplacements illisibles : " + error.Message);
            }
        }

        /// <summary>
        /// Lit les lignes <c>anim image emplacement etiquette miroir a b c d tx ty tx_miroir ty_miroir devant</c> (tabulations,
        /// en-tête facultatif, nombres invariants) ; une ligne illisible est ignorée.
        /// </summary>
        public static AccessoryPlacements Parse(IEnumerable<string> lines)
        {
            var table = new Dictionary<string, List<AccessorySlot>>(StringComparer.OrdinalIgnoreCase);
            int count = 0, read = 0;
            foreach (string raw in lines ?? new string[0])
            {
                if (++read > MaxRows) return new AccessoryPlacements(Empty.rows, 0, "Table d'emplacements trop longue (plus de " + MaxRows + " lignes).");
                string line = (raw ?? string.Empty).TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('\t');
                if (f.Length != 14 || f[0].Length == 0 || f[0] == "anim" || f[0].IndexOf('\t') >= 0) continue;
                if (!int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out int frame)
                    || !int.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out int slot) || slot < 0 || slot >= ActorAccessories.SlotCount
                    || !ActorAccessories.IsLabel(f[3]) || (f[4] != "0" && f[4] != "1") || (f[13] != "0" && f[13] != "1")) continue;
                var values = new float[8];
                bool ok = true;
                for (int index = 0; index < 8 && ok; index++)
                {
                    ok = float.TryParse(f[5 + index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index])
                        && !float.IsNaN(values[index]) && !float.IsInfinity(values[index]) && Math.Abs(values[index]) < 100000;
                }
                if (!ok) continue;
                string key = f[0] + "\t" + frame.ToString(CultureInfo.InvariantCulture);
                if (!table.TryGetValue(key, out List<AccessorySlot> slots)) { slots = new List<AccessorySlot>(); table[key] = slots; }
                slots.Add(new AccessorySlot(slot, f[3], f[4] == "1", values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], f[13] == "1"));
                count++;
            }
            var frozen = new Dictionary<string, AccessorySlot[]>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, List<AccessorySlot>> pair in table) frozen[pair.Key] = pair.Value.ToArray();
            return new AccessoryPlacements(frozen, count, null);
        }
    }

    /// <summary>
    /// Accessoire porté par un acteur : emplacement du <c>GM</c> et symbole <c>&lt;type&gt;_&lt;gfx&gt;</c> de l'objet
    /// (<c>items.xml</c>), plus l'image d'un objet vivant (<c>id~type~image</c>, ajoutée à l'étiquette : <c>R0</c>, <c>R3</c>…).
    /// </summary>
    public sealed class WornAccessory
    {
        internal WornAccessory(int slot, int templateId, string symbol, string frameSuffix) { Slot = slot; TemplateId = templateId; Symbol = symbol; FrameSuffix = frameSuffix ?? string.Empty; }
        public int Slot { get; }
        public int TemplateId { get; }
        /// <summary><c>16_2</c> : nom d'export du symbole dans <c>a1.swf</c>…<c>a5.swf</c>, et préfixe des PNG du dossier <c>Accessoires</c>.</summary>
        public string Symbol { get; }
        /// <summary>Image de l'objet vivant à partir de 0 (<c>Items.onAccessories</c>), vide sinon.</summary>
        public string FrameSuffix { get; }
        /// <summary>Étiquette complète pour un clip réservé : son étiquette suivie de l'image (<c>gotoAndStop(étiquette + image)</c>).</summary>
        public string LabelFor(AccessorySlot slot) => slot.Label + FrameSuffix;
    }

    /// <summary>Accessoire résolu pour une image d'un acteur : bande, image de la bande et clip qui la pose.</summary>
    internal sealed class AccessoryDraw
    {
        internal AccessoryDraw(SpriteSheet sheet, int frame, AccessorySlot slot, bool mirrorTranslation) { Sheet = sheet; Frame = frame; Slot = slot; MirrorTranslation = mirrorTranslation; }
        public SpriteSheet Sheet { get; }
        public int Frame { get; }
        public AccessorySlot Slot { get; }
        /// <summary>Prendre <see cref="AccessorySlot.TxMirror"/>/<see cref="AccessorySlot.TyMirror"/> (sprite retourné, clip à <c>_x</c> inversé).</summary>
        public bool MirrorTranslation { get; }
    }

    /// <summary>
    /// Accessoires des personnages (lot AN7) : coiffe, cape, familier, bouclier et arme du <c>GM</c> (et de <c>Oa</c>), comme
    /// <c>GAC.applyAccessory</c> du client 1.34. Chaque sprite de classe réserve, dans chaque image, un clip vide par
    /// emplacement ; le client y attache le symbole <c>&lt;type&gt;_&lt;gfx&gt;</c> de l'objet (type et gfx d'<c>items.xml</c>)
    /// et l'arrête sur l'étiquette du clip. Le bot pose la bande <c>Accessoires/&lt;type&gt;_&lt;gfx&gt;_&lt;étiquette&gt;.png</c>
    /// par la matrice relevée dans <c>Accessoires/emplacements/&lt;gfx&gt;.tsv</c>, derrière ou devant le corps.
    /// </summary>
    public static class ActorAccessories
    {
        public const string FolderName = "Accessoires";
        public const string PlacementsFolderName = "emplacements";
        /// <summary>Emplacements du <c>GM</c> : 0 arme, 1 coiffe, 2 cape, 3 familier, 4 bouclier.</summary>
        public const int SlotCount = 5;
        private static readonly Regex SymbolName = new Regex(@"^\d{1,6}_\d{1,6}$", RegexOptions.CultureInvariant);
        private static readonly Regex LabelName = new Regex(@"^[A-Za-z][A-Za-z0-9]{0,15}$", RegexOptions.CultureInvariant);
        private static readonly WornAccessory[] NoneWorn = new WornAccessory[0];
        private static readonly object cacheSync = new object();
        private static readonly Dictionary<int, string> symbolCache = new Dictionary<int, string>();
        private static int cacheCount = -1;

        public static bool IsSymbol(string symbol) => !string.IsNullOrEmpty(symbol) && SymbolName.IsMatch(symbol);
        public static bool IsLabel(string label) => !string.IsNullOrEmpty(label) && LabelName.IsMatch(label);

        /// <summary>Accessoires visibles d'un acteur de la carte (joueur, PNJ, monstre en combat, marchand) ; vide pour les autres.</summary>
        public static IReadOnlyList<ActorAccessory> Of(MapActor actor)
        {
            switch (actor)
            {
                case PlayerActor player: return player.Accessories;
                case NpcActor npc: return npc.Accessories;
                case FightMonsterActor monster: return monster.Accessories;
                case MerchantActor merchant: return merchant.Accessories;
                default: return null;
            }
        }

        /// <summary>
        /// Symboles des accessoires d'après <c>items.xml</c> (<see cref="LangData"/>, famille <c>items</c>, table <c>objet</c>,
        /// attributs <c>type</c> et <c>gfx</c>) : un par emplacement 0 à 4 dont l'objet est connu ; le type donné par le <c>GM</c>
        /// (<c>id~type~image</c>) l'emporte sur celui de l'objet. Sans textes chargés, aucun accessoire. Lecture depuis
        /// n'importe quel fil, résultats gardés par objet tant que la table ne change pas.
        /// </summary>
        public static IReadOnlyList<WornAccessory> Worn(IReadOnlyList<ActorAccessory> accessories)
        {
            if (accessories == null || accessories.Count == 0) return NoneWorn;
            List<WornAccessory> worn = null;
            foreach (ActorAccessory accessory in accessories)
            {
                if (accessory == null || accessory.Slot < 0 || accessory.Slot >= SlotCount || accessory.TemplateId <= 0) continue;
                string symbol = Symbol(accessory.TemplateId, accessory.Type);
                if (symbol == null) continue;
                if (worn == null) worn = new List<WornAccessory>(SlotCount);
                if (worn.Any(other => other.Slot == accessory.Slot)) continue;
                worn.Add(new WornAccessory(accessory.Slot, accessory.TemplateId, symbol,
                    accessory.Frame.HasValue ? accessory.Frame.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));
            }
            return worn == null ? NoneWorn : (IReadOnlyList<WornAccessory>)worn;
        }

        /// <summary>Symbole <c>&lt;type&gt;_&lt;gfx&gt;</c> de l'objet <paramref name="templateId"/>, ou <c>null</c> s'il est inconnu.</summary>
        public static string Symbol(int templateId, int? typeOverride = null)
        {
            int count = LangData.Count("items", "objet");
            if (count <= 0) return null;
            string key;
            lock (cacheSync)
            {
                if (count != cacheCount) { symbolCache.Clear(); cacheCount = count; }
                if (symbolCache.TryGetValue(templateId, out key)) return Combine(key, typeOverride);
            }
            IReadOnlyDictionary<string, string> item = LangData.Raw("items", "objet", templateId.ToString(CultureInfo.InvariantCulture));
            key = null;
            if (item != null && item.TryGetValue("type", out string type) && item.TryGetValue("gfx", out string gfx)
                && int.TryParse(type, NumberStyles.None, CultureInfo.InvariantCulture, out int typeId)
                && int.TryParse(gfx, NumberStyles.None, CultureInfo.InvariantCulture, out int gfxId) && gfxId > 0)
                key = typeId.ToString(CultureInfo.InvariantCulture) + "_" + gfxId.ToString(CultureInfo.InvariantCulture);
            lock (cacheSync) { if (count == cacheCount && symbolCache.Count < 20000) symbolCache[templateId] = key; }
            return Combine(key, typeOverride);
        }

        private static string Combine(string symbol, int? typeOverride)
        {
            if (symbol == null || !typeOverride.HasValue || typeOverride.Value < 0) return symbol;
            return typeOverride.Value.ToString(CultureInfo.InvariantCulture) + symbol.Substring(symbol.IndexOf('_'));
        }

        /// <summary>Nom de la bande d'un PNG lu (<c>10_staticR.png</c> → <c>staticR</c>), ou <c>null</c> pour un ancien nom.</summary>
        public static string BandName(string file, int gfx)
        {
            if (string.IsNullOrEmpty(file)) return null;
            string name = Path.GetFileNameWithoutExtension(file) ?? string.Empty;
            string prefix = gfx.ToString(CultureInfo.InvariantCulture) + "_";
            return name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name.Substring(prefix.Length) : null;
        }

        /// <summary>
        /// Dessine un accessoire : pixels de sa bande dans le repère de l'accessoire (<see cref="SpriteSheet.XMin"/>,
        /// <see cref="SpriteSheet.YMin"/>), posés par la matrice du clip dans celui du sprite, puis à l'échelle de l'acteur
        /// (retourné pour le miroir) sur son pied. Même dessin que <see cref="SpritePainter"/> (rectangle, clip de la case).
        /// </summary>
        internal static void Draw(Graphics graphics, AccessoryDraw draw, PointF foot, float scaleX, float scaleY, bool mirrored, ImageAttributes attributes)
        {
            SpriteSheet sheet = draw?.Sheet;
            AccessorySlot slot = draw?.Slot;
            if (sheet?.Image == null || slot == null || scaleX <= 0 || scaleY <= 0 || Math.Abs(slot.Determinant) < 1e-6f) return;
            Rectangle source = sheet.Source(draw.Frame);
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.TranslateTransform(foot.X, foot.Y);
                graphics.ScaleTransform(mirrored ? -scaleX : scaleX, scaleY);
                using (var matrix = new Matrix(slot.A, slot.B, slot.C, slot.D, draw.MirrorTranslation ? slot.TxMirror : slot.Tx, draw.MirrorTranslation ? slot.TyMirror : slot.Ty))
                    graphics.MultiplyTransform(matrix);
                graphics.TranslateTransform(sheet.XMin, sheet.YMin);
                graphics.IntersectClip(new RectangleF(0, 0, source.Width, source.Height));
                graphics.DrawImage(sheet.Image, new Rectangle(0, 0, source.Width, source.Height), source.X, source.Y, source.Width, source.Height,
                    GraphicsUnit.Pixel, attributes);
            }
            catch (Exception error) when (error is ArgumentException || error is System.Runtime.InteropServices.ExternalException)
            {
                // Matrice refusée par GDI+ (non inversible après arrondi) : l'accessoire est sauté, le corps reste dessiné.
            }
            finally { graphics.Restore(saved); }
        }
    }
}
