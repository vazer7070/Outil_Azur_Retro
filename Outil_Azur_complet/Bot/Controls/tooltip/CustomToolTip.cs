using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Accounts;

namespace Outil_Azur_complet.Bot.Controls.tooltip
{
    public class CustomToolTip : ToolTip
    {
        Accounts a;
        UserMapCell cell;
        string F;
        string s;
        public CustomToolTip(Accounts A, UserMapCell C, string f, string S)
        {
            this.a = A;
            this.cell = C;
            this.F = f;
            this.s = S;
            this.OwnerDraw = true;
            this.Popup += new PopupEventHandler(this.OnPopup);
            this.Draw += new DrawToolTipEventHandler(this.OnDraw);
        }

        private void OnPopup(object sender, PopupEventArgs e)
        {
            e.ToolTipSize = new Size(200, 100);
        }

        private void OnDraw(object sender, DrawToolTipEventArgs e)
        {
            Graphics g = e.Graphics;

            var element = a.Game.Map.MonsterList().FirstOrDefault(x => x.Cell.CellID == cell.id);
            var level = a.Game.Map.MonsterList().FirstOrDefault(x => x.Cell.CellID == cell.id).MobsGroupelevel;

            var img = UserMapControl.ReturnMonsterStar(int.Parse(s));
            g.DrawImage(img, 20, 20, 40, 40);

            g.DrawString($"Niveau:{level}", new Font(e.Font, FontStyle.Bold), Brushes.Black, new PointF(e.Bounds.X + 38, e.Bounds.Y + 2));

           g.DrawString(F, new Font(e.Font, FontStyle.Regular), Brushes.White, new PointF(e.Bounds.X + 120, e.Bounds.Y + 20));
        }
    }
}
