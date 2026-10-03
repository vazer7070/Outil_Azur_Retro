using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls.buttons
{
    public class RoundedButton : Button
    {
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            using (var graphicsPath = new GraphicsPath())
            {
                graphicsPath.AddEllipse(0, 0, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
                Region previous = Region;
                Region = new Region(graphicsPath);
                previous?.Dispose();
            }
            base.OnPaint(e);
        }
    }
}
