using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Tool_BotProtocol.Utils.Pics
{
    public class PicturesManager
    {
        static string SpritesPath => Path.Combine(".", "ressources", "Bot", "sprites");
        static string GfxPath => Path.Combine(".", "ressources", "Bot", "gfx");

        // Each returned bitmap belongs to the caller and must be disposed after use.
        // Clone before closing the stream so no retained bitmap locks its PNG file.
        private static Bitmap LoadOwnedBitmap(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var source = Image.FromStream(stream))
                    return new Bitmap(source);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is ArgumentException || error is System.Runtime.InteropServices.ExternalException
                || error is OutOfMemoryException)
            {
                // Unavailable or malformed optional artwork uses the map's placeholder.
                return null;
            }
        }

        public static Bitmap InteractivePicSprite(int id_interactive, int direction)
        {
            string suffix = direction == 2 ? "F" : direction == 3 ? "L" : "R";
            return LoadOwnedBitmap(Path.Combine(SpritesPath, id_interactive + suffix + ".png"));
        }

        public static Bitmap InteractivePicGfx(int id_interactive, bool R)
        {
            if (R)
            {
                Bitmap variant = LoadOwnedBitmap(Path.Combine(GfxPath, id_interactive + "R.png"));
                if (variant != null) return variant;
            }
            return LoadOwnedBitmap(Path.Combine(GfxPath, id_interactive + ".png"));
        }
    }
}
