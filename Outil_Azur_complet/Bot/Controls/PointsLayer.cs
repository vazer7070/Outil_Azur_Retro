using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Chiffre au-dessus d'un acteur, tel que <see cref="PointsLayer"/> le joue (lecture seule hors de la couche).</summary>
    public sealed class PointsNumber
    {
        internal PointsNumber(long actorId, string text, Color color, double added)
        {
            ActorId = actorId; Text = text; Color = color; Added = added;
        }

        public long ActorId { get; }
        /// <summary>Texte affiché : <c>String(v)</c> du client (<c>-12</c>, <c>3</c>, sans « + »).</summary>
        public string Text { get; }
        public Color Color { get; }
        /// <summary>Heure de l'ajout (horloge de la carte, ms).</summary>
        public double Added { get; }
        /// <summary>Point de départ figé (pied de l'acteur − 50, repère de la carte), ou <c>null</c> tant que l'acteur n'est pas trouvé.</summary>
        public PointF? World { get; internal set; }
        /// <summary>Heure de la première image (le chiffre est en tête de la file de son acteur), ou <c>null</c>.</summary>
        public double? Start { get; internal set; }

        /// <summary>Image du clip à <paramref name="now"/> : floor((maintenant − début) × 40 / 1000) ; -1 avant le départ.</summary>
        public int FrameAt(double now) =>
            Start.HasValue && now >= Start.Value ? (int)Math.Floor((now - Start.Value) * PointsLayer.FramesPerSecond / 1000.0) : -1;

        internal Bitmap Image;
        internal float RenderScale;
    }

    /// <summary>
    /// Chiffres au-dessus des têtes (lot AN2) : PV perdus ou gagnés, PA, PM et quantité récoltée, d'après le clip
    /// <c>ank.battlefield.mc.Points</c> du client 1.34 et son gestionnaire (branche <c>DOUBLEFRAMERATE</c>, 40 images/s).
    /// <list type="bullet">
    /// <item>Texte <c>String(v)</c> en Font2 18 px (<c>SPRITE_POINTS_TEXTFORMAT</c>), rendu ici en Tahoma gras de 18 px, centré
    /// sur le point de départ (marge de 2 px des champs de texte Flash comprise), opacité constante.</item>
    /// <item>Départ à 50 px au-dessus du pied (<c>DEFAULT_SPRITE_HEIGHT</c>), position figée à l'ajout : le chiffre ne suit pas
    /// l'acteur. Un acteur invisible n'en reçoit pas.</item>
    /// <item>Une file par acteur : un chiffre dure 16 images (400 ms, fin au test <c>image &gt; 15</c>) puis laisse partir
    /// le suivant.</item>
    /// <item>Image k : montée <see cref="RiseAt"/> (vitesse −20 multipliée par 0,7 à chaque image, vers 46,7 px) et échelle
    /// <see cref="ScaleAt"/> (taille 200 multipliée par 0,95, angle + 0,25, appliquée avec une image de retard).</item>
    /// </list>
    /// L'image courante vaut floor((maintenant − début) × 40 / 1000) : le tick de 33 ms de la minuterie n'y change rien.
    /// Couche <see cref="EffectLayer.Screen"/>, à l'échelle de la carte (le conteneur du client est dans le champ de bataille).
    /// Le texte est rendu une fois par chiffre dans un Bitmap (refait seulement si l'échelle de la vue change).
    /// Fil de l'interface seulement.
    /// </summary>
    public sealed class PointsLayer : IMapEffect, IDisposable
    {
        public const int FramesPerSecond = 40;
        /// <summary>Images d'un chiffre (le client le retire à la 16e image).</summary>
        public const int Frames = 16;
        public const double DurationMs = Frames * 1000.0 / FramesPerSecond;
        /// <summary><c>DEFAULT_SPRITE_HEIGHT</c> : hauteur du départ au-dessus du pied.</summary>
        public const float SpriteHeight = 50f;
        /// <summary>Taille de <c>SPRITE_POINTS_TEXTFORMAT</c> (Font2, 18).</summary>
        public const float FontPixels = 18f;
        /// <summary>Marge des champs de texte Flash entre le cadre et le texte.</summary>
        public const float TextGutter = 2f;
        /// <summary>
        /// Attente de l'acteur (choix du bot) : les événements arrivent sur le fil de l'interface après la mise à jour du modèle,
        /// l'acteur peut donc n'être dessiné qu'au tick suivant (fantôme de la mort). Au-delà, le chiffre est abandonné.
        /// </summary>
        public const double MaxAnchorWait = 300;
        /// <summary>Chiffres en attente par acteur au plus (garde-fou contre un flot de paquets ; au-delà, ils sont ignorés).</summary>
        public const int MaxQueuedPerActor = 32;
        /// <summary>Longueur maximale du texte (un entier signé en compte au plus 11).</summary>
        public const int MaxTextLength = 16;

        /// <summary>Couleurs du client : PV 0xFF0000, PA 0x0000FF, PM 0x006600, quantité récoltée 0xB04600.</summary>
        public static readonly Color LifeColor = Color.FromArgb(0xFF, 0x00, 0x00);
        public static readonly Color ActionPointsColor = Color.FromArgb(0x00, 0x00, 0xFF);
        public static readonly Color MovementPointsColor = Color.FromArgb(0x00, 0x66, 0x00);
        public static readonly Color QuantityColor = Color.FromArgb(0xB0, 0x46, 0x00);

        /// <summary>Échelle maximale atteinte (254,6 % à l'image 6) arrondie : le Bitmap est rendu à cette taille puis réduit.</summary>
        private const float MaxScale = 2.6f;
        private const float MaxRenderScale = 8f;

        private readonly Dictionary<long, List<PointsNumber>> queues = new Dictionary<long, List<PointsNumber>>();
        private readonly Dictionary<long, double> lastEnd = new Dictionary<long, double>();
        private bool disposed;

        public EffectLayer Layer => EffectLayer.Screen;
        public int Depth => 0;
        public int Order => 0;
        public bool IsDisposed => disposed;
        /// <summary>Chiffres en cours ou en attente, toutes files confondues.</summary>
        public int Count => queues.Values.Sum(queue => queue.Count);
        /// <summary>Copie des chiffres en cours ou en attente, dans l'ordre des files (tests, diagnostic).</summary>
        public IReadOnlyList<PointsNumber> Numbers => queues.Values.SelectMany(queue => queue).ToArray();

        /// <summary>Montée (px, négative vers le haut) à l'image <paramref name="frame"/> : <c>vy *= 0,7 ; y += vy</c>, vy partant de −20.</summary>
        public static float RiseAt(int frame)
        {
            double vy = -20, y = 0;
            for (int k = 1; k <= Math.Min(frame, Frames); k++) { vy *= 0.7; y += vy; }
            return (float)y;
        }

        /// <summary>
        /// Échelle (%) à l'image <paramref name="frame"/> : à chaque image, l'échelle prend la valeur calculée à l'image
        /// précédente (indéfinie à la première : 100 % reste), puis <c>i += 0,25 ; t = 100 + sz·sin(i) ; sz *= 0,95</c>.
        /// </summary>
        public static float ScaleAt(int frame)
        {
            double size = 200, angle = 0, shown = 100, computed = double.NaN;
            for (int k = 1; k <= Math.Min(frame, Frames); k++)
            {
                if (!double.IsNaN(computed)) shown = computed;
                angle += 0.25;
                computed = 100 + size * Math.Sin(angle);
                size *= 0.95;
            }
            return (float)shown;
        }

        /// <summary>
        /// Ajoute un chiffre à la file de l'acteur ; il part quand le précédent a fini (ou tout de suite). Sa position est lue au
        /// prochain <see cref="Update"/>. Faux si la couche est libérée, le texte vide ou la file pleine.
        /// </summary>
        public bool Add(long actorId, string text, Color color, double now)
        {
            if (disposed || string.IsNullOrEmpty(text)) return false;
            if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength);
            if (!queues.TryGetValue(actorId, out List<PointsNumber> queue)) queues[actorId] = queue = new List<PointsNumber>();
            if (queue.Count >= MaxQueuedPerActor) return false;
            queue.Add(new PointsNumber(actorId, text, color, now));
            return true;
        }

        public bool Update(double now, MapView view)
        {
            if (disposed) return false;
            foreach (long actorId in queues.Keys.ToArray())
            {
                List<PointsNumber> queue = queues[actorId];
                // Position figée à l'ajout : acteur dessiné (ou son fantôme) ; invisible, il n'en reçoit pas.
                foreach (PointsNumber number in queue.ToArray())
                {
                    if (number.World.HasValue) continue;
                    if (view != null && view.TryGetActorAnchor(actorId, out ActorAnchor anchor))
                    {
                        if (anchor.IsVisible) number.World = new PointF(anchor.WorldFoot.X, anchor.WorldFoot.Y - SpriteHeight);
                        else Remove(queue, number);
                    }
                    else if (now - number.Added > MaxAnchorWait) Remove(queue, number);
                }
                while (queue.Count > 0)
                {
                    PointsNumber head = queue[0];
                    if (!head.Start.HasValue)
                    {
                        if (!head.World.HasValue) break;
                        head.Start = lastEnd.TryGetValue(actorId, out double end) ? Math.Max(head.Added, end) : head.Added;
                    }
                    if (now - head.Start.Value < DurationMs) break;
                    lastEnd[actorId] = head.Start.Value + DurationMs;
                    Remove(queue, head);
                }
                if (queue.Count == 0) { queues.Remove(actorId); lastEnd.Remove(actorId); }
            }
            return queues.Count > 0;
        }

        public void Draw(Graphics graphics, MapView view)
        {
            if (disposed || graphics == null || view == null) return;
            double now = view.Now;
            float viewScale = view.Scale > 0 ? view.Scale : 1f;
            foreach (List<PointsNumber> queue in queues.Values)
            {
                PointsNumber head = queue.Count > 0 ? queue[0] : null;
                if (head?.World == null) continue;
                int frame = head.FrameAt(now);
                if (frame < 0 || frame >= Frames) continue;
                Bitmap image = Render(head, viewScale);
                if (image == null) continue;
                float scale = ScaleAt(frame) / 100f;
                PointF world = head.World.Value;
                PointF center = view.ToScreen(new PointF(world.X + TextGutter * scale, world.Y + RiseAt(frame) + TextGutter * scale));
                float k = scale * viewScale / head.RenderScale;
                var target = new RectangleF(center.X - image.Width * k / 2, center.Y - image.Height * k / 2, image.Width * k, image.Height * k);
                if (target.Width < 1 || target.Height < 1) continue;
                GraphicsState saved = graphics.Save();
                try
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(image, target);
                }
                finally { graphics.Restore(saved); }
            }
        }

        /// <summary>Texte rendu une fois à l'échelle maximale du clip (réduit ensuite), refait si l'échelle de la vue change.</summary>
        private static Bitmap Render(PointsNumber number, float viewScale)
        {
            float renderScale = Math.Min(MaxRenderScale, Math.Max(0.1f, MaxScale * viewScale));
            if (number.Image != null && Math.Abs(number.RenderScale - renderScale) <= renderScale * 0.01f) return number.Image;
            number.Image?.Dispose();
            number.Image = null;
            using (var font = new Font("Tahoma", FontPixels * renderScale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
                SizeF size;
                using (var probe = new Bitmap(1, 1))
                using (Graphics measure = Graphics.FromImage(probe))
                {
                    measure.TextRenderingHint = TextRenderingHint.AntiAlias;
                    size = measure.MeasureString(number.Text, font, PointF.Empty, format);
                }
                float pad = TextGutter * renderScale;
                int width = Math.Max(1, (int)Math.Ceiling(size.Width + 2 * pad)), height = Math.Max(1, (int)Math.Ceiling(size.Height + 2 * pad));
                var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(image))
                    using (var brush = new SolidBrush(number.Color))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                        graphics.DrawString(number.Text, font, brush, new PointF((width - size.Width) / 2f, (height - size.Height) / 2f), format);
                    }
                }
                catch
                {
                    image.Dispose();
                    throw;
                }
                number.Image = image;
                number.RenderScale = renderScale;
                return image;
            }
        }

        private void Remove(List<PointsNumber> queue, PointsNumber number)
        {
            queue.Remove(number);
            number.Image?.Dispose();
            number.Image = null;
        }

        /// <summary>Oublie tous les chiffres (changement de carte, carte cachée).</summary>
        public void Clear()
        {
            foreach (List<PointsNumber> queue in queues.Values)
                foreach (PointsNumber number in queue) { number.Image?.Dispose(); number.Image = null; }
            queues.Clear();
            lastEnd.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }
    }
}
