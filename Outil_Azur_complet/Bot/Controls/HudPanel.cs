using System.Drawing;
using System.Windows.Forms;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>Emplacement du bandeau bas de la fenêtre de jeu.</summary>
    public enum HudSlot { Left, Center, Right }

    /// <summary>
    /// Bandeau bas du client Retro (cadre brun, double filet doré) en trois emplacements : à gauche la discussion,
    /// au centre l'orbe de vie, à droite les raccourcis. Les lots ajoutent leurs contrôles avec <see cref="SetSlot"/>
    /// ou dans <see cref="LeftSlot"/>, <see cref="CenterSlot"/> et <see cref="RightSlot"/> sans modifier le formulaire principal.
    /// </summary>
    public sealed class HudPanel : Panel
    {
        /// <summary>Largeur fixe de l'emplacement central (orbe de vie et barre d'expérience).</summary>
        public const int CenterWidth = 116;
        private readonly TableLayoutPanel layout;

        public HudPanel()
        {
            DoubleBuffered = true; BackColor = BotUi.FrameLight; Padding = new Padding(7); Margin = new Padding(0);
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CenterWidth));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            LeftSlot = Slot("hud-left"); CenterSlot = Slot("hud-center"); RightSlot = Slot("hud-right");
            layout.Controls.Add(LeftSlot, 0, 0); layout.Controls.Add(CenterSlot, 1, 0); layout.Controls.Add(RightSlot, 2, 0);
            Controls.Add(layout);
        }

        public Panel LeftSlot { get; }
        public Panel CenterSlot { get; }
        public Panel RightSlot { get; }

        public Panel this[HudSlot slot] => slot == HudSlot.Left ? LeftSlot : slot == HudSlot.Center ? CenterSlot : RightSlot;

        /// <summary>Remplace le contenu de l'emplacement (les anciens contrôles sont libérés) ; <c>null</c> le vide.</summary>
        public void SetSlot(HudSlot slot, Control content)
        {
            Panel target = this[slot];
            target.SuspendLayout();
            while (target.Controls.Count > 0)
            {
                Control old = target.Controls[0];
                target.Controls.RemoveAt(0);
                if (!ReferenceEquals(old, content)) old.Dispose();
            }
            if (content != null) { content.Dock = DockStyle.Fill; target.Controls.Add(content); }
            target.ResumeLayout();
        }

        private static Panel Slot(string name) =>
            new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0), Name = name, BackColor = Color.Transparent };

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 3 || Height < 3) return;
            using (var dark = new Pen(Color.FromArgb(30, 29, 24)))
            using (var light = new Pen(BotUi.Gold)) {
                e.Graphics.DrawRectangle(dark, 0, 0, Width - 1, Height - 1);
                e.Graphics.DrawRectangle(light, 1, 1, Width - 3, Height - 3);
            }
        }
    }
}
