using System;
using System.Collections.Generic;
using System.Drawing;

namespace Outil_Azur_complet.Bot
{
    /// <summary>
    /// Polices Tahoma partagées par le tiroir, les menus contextuels, le bandeau et les boîtes de dialogue.
    /// Une police par taille et style pour toute la durée du processus : les contrôles ne doivent jamais les libérer.
    /// </summary>
    internal static class BotFonts
    {
        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>(StringComparer.Ordinal);
        private static readonly object sync = new object();

        internal static Font Get(float size, FontStyle style = FontStyle.Regular)
        {
            string key = size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + (int)style;
            lock (sync) {
                Font font;
                if (!fonts.TryGetValue(key, out font)) { font = new Font("Tahoma", size, style); fonts[key] = font; }
                return font;
            }
        }

        /// <summary>Vrai pour une police de ce cache (qui ne doit jamais être libérée).</summary>
        internal static bool IsShared(Font font)
        {
            if (font == null) return false;
            lock (sync) { foreach (Font shared in fonts.Values) if (ReferenceEquals(shared, font)) return true; }
            return false;
        }
    }
}
