using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Groupes;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Colonne du groupe, fenêtre <c>Party</c> du client (<c>UI_Party</c>, 30 × 291 px posés en x 713 sur la scène de 742 px :
    /// à droite de la carte). En haut, le bouton Afficher / Cacher (<c>_btnOpenClose</c>, <c>PARTY_OPEN_CLOSE</c>) et la pastille
    /// d'informations (<c>_mcInfo</c> : niveau total, prospection totale) ; puis une case tous les 30 px par membre
    /// (<c>_piMember0</c> à <c>_piMember7</c>, initiative décroissante) : sprite <c>staticR</c> du membre recoloré par ses trois
    /// couleurs de <c>PM</c> (<c>api.colors.addSprite</c>, <see cref="SpriteRecolor"/>), retourné et réduit à 65 %
    /// (<c>_xscale = -65</c>) ; jauge de vie rouge à droite (<c>_mcHealth._yscale</c> = PV / PV max) ; couronne du chef au-dessus,
    /// flèche du membre suivi au-dessous ; infobulle nom / niveau / points de vie ; clic → menu du membre (<see cref="MemberClicked"/>).
    /// Visible de <c>PCK</c> à <c>PV</c>, quel que soit le volet ouvert. Le fond ne peut pas laisser voir la carte (contrôle
    /// WinForms opaque) : il prend le brun du cadre, hauteur réduite aux cases occupées.
    /// </summary>
    public sealed class PartyColumn : Control
    {
        /// <summary>Largeur de <c>UI_Party</c>.</summary>
        public const int ColumnWidth = 30;
        /// <summary>Hauteur de <c>UI_Party</c> avec ses huit cases.</summary>
        public const int FullHeight = 291;
        /// <summary>Haut de la colonne sur la scène du client (y 41) : la colonne est posée à cette distance du haut de la carte.</summary>
        public const int StageTop = 41;
        /// <summary>Pas des cases (<c>_piMember</c> en y 82, 112… dans <c>UI_Party</c>) et haut de la première case dans la colonne.</summary>
        public const int SlotPitch = 30, FirstSlotTop = 41;
        /// <summary>Cadre blanc d'une case (<c>UI_PartyItem</c> : 0..25 × 0..21) ; le sprite occupe 1..20, la jauge 21..24.</summary>
        public const int TileWidth = 26, TileHeight = 22;
        /// <summary>Échelle du sprite du membre (<c>_xscale = -65</c>, <c>_yscale = 65</c>).</summary>
        public const float SpriteScale = 0.65f;
        /// <summary>Rouge de la jauge <c>_mcHealth</c>.</summary>
        public static readonly Color HealthColor = PartyMemberList.HealthColor;
        private const int ToggleSide = 11;
        private static readonly Rectangle ToggleBounds = new Rectangle(2, 2, ToggleSide, ToggleSide);
        private static readonly Rectangle InfoBounds = new Rectangle(2, 19, TileWidth, 14);

        private readonly ActorSprites sprites;
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };
        private Accounts account;
        private Groupe group;
        private IReadOnlyList<PartyMember> members = new PartyMember[0];
        private long? leader, followed;
        private long self;
        private bool collapsed;
        private int hover = -2;
        private string shownTip;

        public PartyColumn() : this(null) { }

        /// <param name="spriteDirectory">Dossier des sprites d'acteurs ; <c>null</c> = <see cref="ActorSprites.DefaultDirectory"/>.</param>
        public PartyColumn(string spriteDirectory)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = BotUi.Frame; ForeColor = BotUi.Ink; Font = BotFonts.Get(8); TabStop = false; Visible = false; Cursor = Cursors.Hand;
            Name = "party-column"; AccessibleRole = AccessibleRole.List; AccessibleName = PartyTexts.Get("PARTY", "Groupe");
            Size = new Size(ColumnWidth, FirstSlotTop);
            sprites = new ActorSprites(spriteDirectory);
            sprites.SheetsLoaded += (s, e) => BotUi.OnUi(this, Invalidate);
            foreach (string name in new[] { PartyArtworks.Leader, PartyArtworks.Follow, PartyArtworks.Info })
            {
                Task<Bitmap> load = PartyArtworks.LoadAsync(PartyArtworks.UiFamily, name);
                if (!load.IsCompleted) load.ContinueWith(_ => BotUi.OnUi(this, Invalidate), TaskScheduler.Default);
            }
        }

        /// <summary>Clic sur la case d'un membre (repère de la colonne) : la fenêtre ouvre son menu.</summary>
        public event EventHandler<PartyMemberEventArgs> MemberClicked;

        /// <summary>Membres affichés, par initiative décroissante.</summary>
        public IReadOnlyList<PartyMember> Members => members;
        public long? LeaderId => leader;
        public long? FollowedId => followed;
        /// <summary>Cases repliées par le bouton Afficher / Cacher (<c>_btnOpenClose.selected</c>).</summary>
        public bool Collapsed
        {
            get => collapsed;
            set { if (collapsed == value) return; collapsed = value; UpdateSize(); Invalidate(); }
        }
        /// <summary>Texte de l'infobulle d'informations (<c>INFORMATIONS</c>, <c>TOTAL_LEVEL</c>, <c>TOTAL_DISCERNMENT</c>).</summary>
        public string InfoText => PartyTexts.Get("INFORMATIONS", "Informations") + "\n"
            + PartyTexts.Get("TOTAL_LEVEL", "Niveau total") + " : " + members.Sum(member => member.Level).ToString(CultureInfo.CurrentCulture) + "\n"
            + PartyTexts.Get("TOTAL_DISCERNMENT", "Prospection totale") + " : " + members.Sum(member => member.Prospection).ToString(CultureInfo.CurrentCulture);

        /// <summary>Attache la colonne au groupe du compte (fil de l'interface) ; <c>null</c> la détache.</summary>
        public void Bind(Accounts value)
        {
            Release();
            account = value;
            group = value?.Game?.Interactions?.Party?.Group;
            if (group != null) group.Changed += OnGroupChanged;
            RefreshFromParty();
        }

        /// <summary>Retire l'abonnement au groupe (fermeture de la fenêtre) ; peut être appelé plusieurs fois.</summary>
        public void Release()
        {
            if (group != null) group.Changed -= OnGroupChanged;
            group = null; account = null;
        }

        /// <summary>Attend la lecture des sprites demandés (tests, captures) ; vrai si plus rien n'est en cours.</summary>
        public bool WaitForSprites(int milliseconds) => sprites.WaitForPending(milliseconds);

        /// <summary>Rectangle de la case de rang <paramref name="index"/> (repère de la colonne).</summary>
        public static Rectangle TileBounds(int index) => new Rectangle(2, FirstSlotTop + index * SlotPitch, TileWidth, TileHeight);

        /// <summary>Zone du sprite dans une case (<c>_ldrSprite</c> sous son masque) : les 20 premiers pixels.</summary>
        public static Rectangle SpriteArea(Rectangle tile) => new Rectangle(tile.X + 1, tile.Y + 1, 19, tile.Height - 2);

        /// <summary>Jauge de vie d'une case (<c>_mcHealth</c>) : 4 px à droite, remplie depuis le bas.</summary>
        public static Rectangle HealthArea(Rectangle tile) => new Rectangle(tile.Right - 5, tile.Y + 1, 4, tile.Height - 2);

        /// <summary>Membre sous le point (repère de la colonne), ou <c>null</c>.</summary>
        public PartyMember MemberAt(Point point)
        {
            int index = IndexAt(point);
            return index >= 0 ? members[index] : null;
        }

        /// <summary>Simule un clic sur la case du membre (tests, raccourci) ; faux s'il n'est pas affiché.</summary>
        public bool ClickMember(long id)
        {
            if (collapsed) return false;
            for (int index = 0; index < members.Count; index++)
                if (members[index].Id == id)
                {
                    Rectangle tile = TileBounds(index);
                    MemberClicked?.Invoke(this, new PartyMemberEventArgs(members[index], new Point(tile.Left, tile.Bottom)));
                    return true;
                }
            return false;
        }

        /// <summary>Simule un clic sur le bouton Afficher / Cacher.</summary>
        public void ToggleCollapsed() => Collapsed = !Collapsed;

        private void OnGroupChanged() => BotUi.OnUi(this, RefreshFromParty);

        /// <summary>Recopie le groupe (fil de l'interface) : membres, chef, suivi ; visible tant que le groupe existe.</summary>
        public void RefreshFromParty()
        {
            if (IsDisposed || Disposing) return;
            Groupe current = group;
            bool active = current != null && current.IsActive;
            IReadOnlyList<PartyMember> list = active ? current.Members.Take(Groupe.MaxMembers).ToArray() : new PartyMember[0];
            members = list;
            leader = active ? current.LeaderId : null;
            followed = active ? current.FollowedId : null;
            self = account?.Game?.character?.id ?? 0;
            foreach (PartyMember member in list)
                if (member.Gfx > 0) sprites.Prefetch(member.Gfx, 1);
            if (!active) { sprites.ReleaseAll(); collapsed = false; }
            AccessibleDescription = string.Join(", ", list.Select(member => member.Name));
            hover = -2; SetTip(null);
            UpdateSize();
            Visible = active;
            Invalidate();
        }

        private void UpdateSize()
        {
            int height = collapsed || members.Count == 0 ? InfoBounds.Bottom + 4 : FirstSlotTop + (members.Count - 1) * SlotPitch + TileHeight + 9;
            Size = new Size(ColumnWidth, Math.Min(FullHeight, height));
        }

        private int IndexAt(Point point)
        {
            if (collapsed) return -1;
            for (int index = 0; index < members.Count; index++)
            {
                Rectangle tile = TileBounds(index);
                if (point.X >= 0 && point.X < Width && point.Y >= tile.Top - 4 && point.Y < tile.Top + SlotPitch - 4) return index;
            }
            return -1;
        }

        // ----- Dessin -----

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var back = new SolidBrush(BackColor)) graphics.FillRectangle(back, ClientRectangle);
            using (var rule = new Pen(BotUi.FrameLight)) graphics.DrawRectangle(rule, 0, 0, Width - 1, Height - 1);
            DrawToggle(graphics);
            DrawInfo(graphics);
            if (collapsed) return;
            Bitmap crown = PartyArtworks.Ui(PartyArtworks.Leader), arrow = PartyArtworks.Ui(PartyArtworks.Follow);
            for (int index = 0; index < members.Count; index++) DrawTile(graphics, members[index], TileBounds(index), index == hover, crown, arrow);
        }

        private void DrawToggle(Graphics graphics)
        {
            Rectangle box = ToggleBounds;
            using (var paper = new SolidBrush(hover == -1 ? BotUi.PaperLight : BotUi.Paper)) graphics.FillRectangle(paper, box);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawRectangle(rule, box.X, box.Y, box.Width - 1, box.Height - 1);
            // Triangle vers le haut : cases affichées ; vers le bas : repliées (ButtonPartyUp / ButtonPartyDown).
            float cx = box.X + box.Width / 2f, top = box.Y + 3, bottom = box.Bottom - 4;
            PointF[] triangle = collapsed
                ? new[] { new PointF(box.X + 2.5f, top), new PointF(box.Right - 3.5f, top), new PointF(cx - 0.5f, bottom) }
                : new[] { new PointF(box.X + 2.5f, bottom), new PointF(box.Right - 3.5f, bottom), new PointF(cx - 0.5f, top) };
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var ink = new SolidBrush(BotUi.Ink)) graphics.FillPolygon(ink, triangle);
            graphics.SmoothingMode = previous;
        }

        private void DrawInfo(Graphics graphics)
        {
            if (collapsed) return;
            Rectangle box = InfoBounds;
            using (var paper = new SolidBrush(hover == -3 ? BotUi.PaperLight : BotUi.Paper)) graphics.FillRectangle(paper, box);
            using (var rule = new Pen(BotUi.Gold)) graphics.DrawRectangle(rule, box.X, box.Y, box.Width - 1, box.Height - 1);
            Bitmap swords = PartyArtworks.Ui(PartyArtworks.Info);
            var place = new RectangleF(box.X + (box.Width - 12) / 2f, box.Y + 1, 12, box.Height - 2);
            if (swords != null) ClientAssets.DrawFit(graphics, swords, place);
            else TextRenderer.DrawText(graphics, "i", BotFonts.Get(7, FontStyle.Bold), box, BotUi.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawTile(Graphics graphics, PartyMember member, Rectangle tile, bool hovered, Bitmap crown, Bitmap arrow)
        {
            // Cadre de UI_PartyItem : fond clair, filets, séparation entre le sprite et la jauge.
            using (var paper = new SolidBrush(hovered ? BotUi.PaperLight : BotUi.Paper)) graphics.FillRectangle(paper, tile);
            Rectangle area = SpriteArea(tile), health = HealthArea(tile);
            DrawSprite(graphics, member, area);
            using (var empty = new SolidBrush(BotUi.FrameLight)) graphics.FillRectangle(empty, health);
            int filled = (int)Math.Round(health.Height * member.LifeRatio);
            if (filled > 0) using (var red = new SolidBrush(HealthColor)) graphics.FillRectangle(red, health.X, health.Bottom - filled, health.Width, filled);
            using (var rule = new Pen(hovered ? BotUi.Olive : BotUi.Gold))
            {
                graphics.DrawRectangle(rule, tile.X, tile.Y, tile.Width - 1, tile.Height - 1);
                graphics.DrawLine(rule, health.X - 1, tile.Y, health.X - 1, tile.Bottom - 1);
            }
            if (member.Id == leader)
            {
                var place = new RectangleF(tile.X + (tile.Width - 12) / 2f, tile.Y - 6, 12, 9);
                if (crown != null) ClientAssets.DrawFit(graphics, crown, place);
                else using (var gold = new SolidBrush(Color.FromArgb(255, 176, 0))) graphics.FillRectangle(gold, place);
            }
            if (member.Id == followed)
            {
                var place = new RectangleF(tile.X + (tile.Width - 9) / 2f, tile.Bottom, 9, 7);
                if (arrow != null) ClientAssets.DrawFit(graphics, arrow, place);
                else using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillRectangle(olive, place);
            }
        }

        /// <summary>
        /// Sprite <c>staticR</c> du membre avec ses couleurs, retourné, à 65 %, cadré sur le haut (tête) dans la zone de la case. En
        /// cours de lecture ou absent : l'illustration de classe, sinon l'initiale du nom.
        /// </summary>
        private void DrawSprite(Graphics graphics, PartyMember member, Rectangle area)
        {
            SpritePose pose = member.Gfx > 0 ? sprites.Resolve(member.Gfx, 1, false, "static", new ActorColors(member.Color1, member.Color2, member.Color3)) : null;
            SpriteSheet sheet = pose?.Sheet;
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(area);
                if (sheet != null)
                {
                    bool mirrored = !pose.Mirrored;
                    RectangleF placed = sheet.Destination(PointF.Empty, SpriteScale, SpriteScale, mirrored);
                    var target = new RectangleF(area.X + (area.Width - placed.Width) / 2f, area.Y, placed.Width, placed.Height);
                    lock (sheet.Image) SpritePainter.Draw(graphics, sheet, 0, target, mirrored, null);
                    return;
                }
                Bitmap mini = PartyArtworks.Mini(member.Gfx);
                if (mini != null) { ClientAssets.DrawFit(graphics, mini, area, true); return; }
                string initial = member.Name.Length > 0 ? member.Name.Substring(0, 1).ToUpperInvariant() : "?";
                TextRenderer.DrawText(graphics, initial, BotFonts.Get(9, FontStyle.Bold), area, BotUi.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            finally { graphics.Restore(state); }
        }

        // ----- Souris -----

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = ToggleBounds.Contains(e.Location) ? -1 : !collapsed && InfoBounds.Contains(e.Location) ? -3 : IndexAt(e.Location);
            if (index == hover) return;
            hover = index; Invalidate();
            if (index == -1) SetTip(PartyTexts.Get("PARTY_OPEN_CLOSE", "Affiche/Cache le groupe"));
            else if (index == -3) SetTip(InfoText);
            else if (index >= 0)
            {
                PartyMember member = members[index];
                SetTip(member.Name + (member.Id == self ? " (vous)" : string.Empty) + "\n"
                    + PartyTexts.Get("LEVEL", "Niveau") + " : " + member.Level.ToString(CultureInfo.CurrentCulture) + "\n"
                    + PartyTexts.Get("LIFEPOINTS", "Points de vie") + " : " + member.Life.ToString(CultureInfo.CurrentCulture) + " / " + member.MaxLife.ToString(CultureInfo.CurrentCulture));
            }
            else SetTip(null);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover == -2) return;
            hover = -2; SetTip(null); Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            if (e.Button == MouseButtons.Left && ToggleBounds.Contains(e.Location)) { ToggleCollapsed(); return; }
            PartyMember member = MemberAt(e.Location);
            if (member != null) MemberClicked?.Invoke(this, new PartyMemberEventArgs(member, e.Location));
        }

        private void SetTip(string text)
        {
            if (string.Equals(text, shownTip, StringComparison.Ordinal)) return;
            shownTip = text;
            tips.SetToolTip(this, text);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Release();
                MemberClicked = null;
                tips.Dispose();
                sprites.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
