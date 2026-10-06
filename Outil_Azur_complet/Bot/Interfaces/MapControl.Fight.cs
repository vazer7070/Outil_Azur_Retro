using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Controls.Banner;
using Tool_BotProtocol.Game.Combats;

namespace Outil_Azur_complet.Bot.Interfaces
{
    /// <summary>
    /// Combat sur la carte (lot F12b) : drapeau posé sur une cellule signalée par un coéquipier (<c>Gf&lt;id&gt;|&lt;cellule&gt;</c>,
    /// clip <c>flag.swf</c> du client dessiné quelques secondes au-dessus de la cellule) et mode « signaler une cellule »
    /// (bouton drapeau de <c>FightOptionButtons</c>) : le prochain clic gauche envoie <c>Gf&lt;cellule&gt;</c> au lieu d'agir.
    /// Les abonnements se posent au premier usage et se retirent avec le contrôle.
    /// </summary>
    public partial class MapControl
    {
        /// <summary>Durée d'affichage du drapeau (le clip du client dure 30 images, le bot le laisse visible plus longtemps).</summary>
        public const int FlagDisplayMilliseconds = 8000;
        private Timer flagTimer;
        private FightFlag shownFlag;
        private DateTime flagShownAt;
        private Bitmap flagImage;
        private bool flagHooked, flagMode;

        /// <summary>Drapeau actuellement dessiné sur la carte, ou <c>null</c>.</summary>
        public FightFlag DisplayedFlag => shownFlag;

        /// <summary>Mode « signaler une cellule » : le prochain clic gauche envoie <c>Gf&lt;cellule&gt;</c> (<c>Game.setFlag</c>).</summary>
        public bool FlagMode
        {
            get => flagMode;
            set
            {
                if (flagMode == value) return;
                HookFlag();
                flagMode = value;
                UserMap.Cursor = value ? Cursors.Cross : Cursors.Default;
                ActionFeedback?.Invoke(value ? "Cliquez sur la cellule à signaler à votre équipe · Échap : annuler" : "Signalement annulé.");
            }
        }

        /// <summary>Bascule le mode de signalement (bouton drapeau).</summary>
        public void ToggleFlagMode() => FlagMode = !FlagMode;

        /// <summary>Dessine le drapeau sur la cellule signalée (thread de l'interface) pendant <see cref="FlagDisplayMilliseconds"/>.</summary>
        public void ShowFlag(FightFlag flag)
        {
            if (flag == null || IsDisposed || Disposing) return;
            HookFlag();
            shownFlag = flag; flagShownAt = DateTime.UtcNow;
            if (flagImage == null) BannerArt.Request(this, "FlagCell", image => { flagImage = image; UserMap.Invalidate(); });
            if (flagTimer == null)
            {
                flagTimer = new Timer { Interval = 150 };
                flagTimer.Tick += (s, e) => TickFlag();
            }
            flagTimer.Start();
            UserMap.Invalidate();
        }

        private void HookFlag()
        {
            if (flagHooked) return;
            flagHooked = true;
            UserMap.Paint += PaintFlag;
            if (Router != null) { Router.MoveRequested += FlagClickAsync; Router.ActorClicked += FlagClickAsync; }
            Disposed += (s, e) => { flagTimer?.Dispose(); flagTimer = null; };
        }

        private void TickFlag()
        {
            if (shownFlag == null) { flagTimer?.Stop(); return; }
            if ((DateTime.UtcNow - flagShownAt).TotalMilliseconds > FlagDisplayMilliseconds || Account.Game?.Fight?.IsInFight != true)
            {
                shownFlag = null; flagTimer?.Stop(); UserMap.Invalidate(); return;
            }
            Rectangle area = FlagBounds(shownFlag.CellId);
            if (area.IsEmpty) UserMap.Invalidate(); else UserMap.Invalidate(Rectangle.Inflate(area, 4, 8));
        }

        /// <summary>Cadre du drapeau au-dessus d'une cellule (repère de la vue) ; vide si la cellule n'est pas affichée.</summary>
        private Rectangle FlagBounds(int cellId)
        {
            UserMapCell[] cells = UserMap.Cells;
            if (cells == null || cellId < 0 || cellId >= cells.Length || cells[cellId]?.Points == null || cells[cellId].Points.Length < 4) return Rectangle.Empty;
            UserMapCell cell = cells[cellId];
            int width = Math.Max(12, (int)Math.Round(cell.Rectangle.Width * 0.62));
            int height = flagImage == null ? width * 47 / 59 : Math.Max(1, width * flagImage.Height / Math.Max(1, flagImage.Width));
            Point centre = cell.Centre;
            return new Rectangle(centre.X - width / 2, centre.Y + cell.Rectangle.Height / 4 - height, width, height);
        }

        private void PaintFlag(object sender, PaintEventArgs e)
        {
            FightFlag flag = shownFlag;
            if (flag == null) return;
            Rectangle area = FlagBounds(flag.CellId);
            if (area.IsEmpty) return;
            // Léger balancement vertical, comme le clip animé du client.
            double phase = (DateTime.UtcNow - flagShownAt).TotalMilliseconds / 350.0;
            area.Offset(0, (int)Math.Round(Math.Sin(phase) * 3));
            Bitmap image = flagImage;
            if (image != null) BannerArt.Draw(e.Graphics, image, area);
            else
            {
                using (var pole = new Pen(BotUi.Frame, 2)) e.Graphics.DrawLine(pole, area.Left + area.Width / 2, area.Top, area.Left + area.Width / 2, area.Bottom);
                using (var cloth = new SolidBrush(Color.FromArgb(230, 232, 86, 24)))
                    e.Graphics.FillPolygon(cloth, new[] { new Point(area.Left + area.Width / 2, area.Top), new Point(area.Right, area.Top + area.Height / 4), new Point(area.Left + area.Width / 2, area.Top + area.Height / 2) });
            }
        }

        private async Task FlagClickAsync(object sender, MapClickEventArgs e)
        {
            if (!flagMode || e == null || e.Handled) return;
            e.Handled = true;
            flagMode = false; UserMap.Cursor = Cursors.Default; // sortie silencieuse : le message qui suit vient de l'envoi
            var fight = Account.Game?.Fight;
            if (fight == null) return;
            try
            {
                CombatActionResult result = await fight.SetFlagAsync(e.CellId);
                ActionFeedback?.Invoke(result.Message);
            }
            catch (Exception error) when (!(error is OutOfMemoryException)) { ActionFeedback?.Invoke("Signalement impossible : " + error.Message); }
        }
    }
}
