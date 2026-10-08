using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls.Chat;

namespace Outil_Azur_complet.Bot.Controls.Banner
{
    /// <summary>
    /// Outils communs du bandeau : images du client lues hors du thread de l'interface, dessin d'images partagées
    /// (verrouillées pendant le dessin, comme <see cref="ClientAssets.Heart"/>), textes du client avec repli français.
    /// </summary>
    internal static class BannerArt
    {
        internal static readonly NumberFormatInfo Spaced = new NumberFormatInfo { NumberGroupSeparator = " ", NumberGroupSizes = new[] { 3 } };

        /// <summary>
        /// Image de la famille <paramref name="family"/> : tout de suite si elle est en cache, sinon lue sur le pool de threads
        /// puis remise à <paramref name="apply"/> sur le thread de l'interface. <paramref name="apply"/> ne doit qu'assigner un
        /// champ et invalider : sans poignée de fenêtre nulle part, il est appelé sur le fil de lecture (rien n'est alors dessiné).
        /// </summary>
        internal static void Request(Control owner, string family, string name, Action<Bitmap> apply)
        {
            if (owner == null || apply == null || owner.IsDisposed) return;
            if (ClientAssets.TryCached(family, name, out Bitmap cached)) { apply(cached); return; }
            ClientAssets.GetAsync(family, name).ContinueWith(task => Deliver(owner, task.Status == TaskStatus.RanToCompletion ? task.Result : null, apply), TaskScheduler.Default);
        }

        internal static void Request(Control owner, string name, Action<Bitmap> apply) => Request(owner, "Client", name, apply);

        /// <summary>
        /// Icône réduite de l'interface (<see cref="ClientAssets.Icon"/>, dossier <c>Resources/Bot/Client</c>) : lue et réduite
        /// sur le pool de threads la première fois, puis gardée ; mêmes règles pour <paramref name="apply"/> que <see cref="Request(Control, string, string, Action{Bitmap})"/>.
        /// </summary>
        internal static void RequestIcon(Control owner, string name, int size, Action<Bitmap> apply)
        {
            if (owner == null || apply == null || owner.IsDisposed || string.IsNullOrEmpty(name)) return;
            string key = name + "@" + size.ToString(CultureInfo.InvariantCulture);
            if (Icons.TryGetValue(key, out Bitmap known)) { apply(known); return; }
            Task.Run(() => ClientAssets.Icon(name, size)).ContinueWith(task =>
            {
                Bitmap image = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                Icons[key] = image;
                Deliver(owner, image, apply);
            }, TaskScheduler.Default);
        }

        private static readonly ConcurrentDictionary<string, Bitmap> Icons = new ConcurrentDictionary<string, Bitmap>(StringComparer.Ordinal);

        private static void Deliver(Control owner, Bitmap image, Action<Bitmap> apply)
        {
            if (owner.IsDisposed) return;
            Control target = owner.IsHandleCreated ? owner : owner.FindForm();
            if (target != null && !target.IsDisposed && target.IsHandleCreated)
                BotUi.OnUi(target, () => { if (!owner.IsDisposed) apply(image); });
            else apply(image);
        }

        /// <summary>Dessine une image partagée (cache de <see cref="ClientAssets"/>) ; grisée et à demi transparente si <paramref name="grey"/>.</summary>
        internal static void Draw(Graphics graphics, Image image, RectangleF target, bool grey = false, float opacity = 1f)
        {
            if (image == null || target.Width <= 0 || target.Height <= 0) return;
            lock (image)
            {
                int width = image.Width, height = image.Height;
                if (!grey && opacity >= 1f) { graphics.DrawImage(image, target); return; }
                using (var attributes = new ImageAttributes())
                {
                    float alpha = grey ? 0.45f * opacity : opacity;
                    var matrix = grey
                        ? new ColorMatrix(new[] {
                            new[] { 0.30f, 0.30f, 0.30f, 0, 0 }, new[] { 0.59f, 0.59f, 0.59f, 0, 0 }, new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                            new[] { 0, 0, 0, alpha, 0f }, new[] { 0, 0, 0, 0, 1f } })
                        : new ColorMatrix { Matrix33 = alpha };
                    attributes.SetColorMatrix(matrix);
                    var points = new[] { target.Location, new PointF(target.Right, target.Top), new PointF(target.Left, target.Bottom) };
                    graphics.DrawImage(image, points, new RectangleF(0, 0, width, height), GraphicsUnit.Pixel, attributes);
                }
            }
        }

        /// <summary>Image tournée de <paramref name="degrees"/> autour du centre de <paramref name="target"/> (aiguilles, boussole).</summary>
        internal static void DrawRotated(Graphics graphics, Image image, RectangleF target, double degrees)
        {
            if (image == null) return;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform(target.X + target.Width / 2f, target.Y + target.Height / 2f);
                graphics.RotateTransform((float)degrees);
                Draw(graphics, image, new RectangleF(-target.Width / 2f, -target.Height / 2f, target.Width, target.Height));
            }
            finally { graphics.Restore(state); }
        }

        /// <summary>Texte blanc cerné de brun, lisible sur les images du client (valeurs du cœur et des PA / PM).</summary>
        internal static void OutlinedText(Graphics graphics, string text, Font font, Rectangle bounds, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Color outline = Color.FromArgb(41, 38, 31);
            foreach (var offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
            {
                Rectangle moved = bounds; moved.Offset(offset);
                TextRenderer.DrawText(graphics, text, font, moved, outline, flags);
            }
            TextRenderer.DrawText(graphics, text, font, bounds, color, flags);
        }

        /// <summary>Texte du client (<c>lang_fr</c>) ou formulation française du bot si le fichier de langue manque.</summary>
        internal static string Text(string key, string fallback, params string[] args) => ChatUiText.Get(key, fallback, args);

        /// <summary>Nombre avec espaces entre les milliers, comme <c>PLAYER_WEIGHT</c> du client (« 1 250 pods sur 2 000 »).</summary>
        internal static string Thousands(long value) => value.ToString("#,0", Spaced);

        internal static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }
}
