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
        static string SpritesPath = $@".\ressources\Bot\sprites";
        static string GfxPath = $@".\ressources\Bot\gfx";
        public static Bitmap InteractivePicSprite(int id_interactive, int direction)
        {
            string H = "";
            switch (direction)
            {
                case 1:
                    H = "R";
                    break;
                    case 2:
                    H = "F";
                    break;
                case 3:
                    H = "L";
                    break;
                    default:
                    H = "R";
                    break;
            };            
            if (File.Exists($@"{SpritesPath}\{id_interactive}{H}.png"))
                return (Bitmap)Image.FromFile($@"{SpritesPath}\{id_interactive}{H}.png");
            return null;
        }

        public static Bitmap InteractivePicGfx(int id_interactive, bool R)
        {
            if (File.Exists($@"{GfxPath}\{id_interactive}.png"))
                if(R)
                    return (Bitmap)Image.FromFile($@"{GfxPath}\{id_interactive}R.png");
                else
                    return (Bitmap)Image.FromFile($@"{GfxPath}\{id_interactive}.png");
            return null;
        }

    }
}
