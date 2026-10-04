using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Menus;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Groupes;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Session;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Volet Groupe : le bandeau <c>Party</c> du client 1.34 (une case par membre, couronne du chef, flèche du suivi, jauge de vie,
    /// infobulle « niveau total / prospection totale ») rendu dans le tiroir, avec l'invitation en cours, l'invitation d'un joueur
    /// par son nom, la localisation des membres (<c>PW</c> → <c>IH</c>) et la cible de la boussole (<c>IC</c>). Il s'ouvre à l'entrée
    /// dans un groupe (<c>PCK</c>) et se ferme à la sortie (<c>PV</c>), comme le bandeau du client. Une invitation reçue est posée
    /// dans une boîte Oui / Non / Ignorer (<c>PA</c> / <c>PR</c> / <c>PR</c> et invitant ignoré pour la session) ; un clic sur
    /// un membre ouvre son menu (<see cref="PartyMenuProvider.MemberEntries"/>).
    /// </summary>
    public sealed class PartyPanel : GamePanel
    {
        private const string Reference = "GROUPE";
        private Label heading, summary, invitation, compassLabel, status;
        private PictureBox infoIcon;
        private Control cancelInvitation, inviteButton, locate, leave;
        private TextBox inviteName;
        private PartyMemberList members;
        private PartyCompass compass;
        private ListView locations;
        private Panel invitationBar, infoBar;
        private PartyActions bound;
        private GameSession session;
        private bool wasActive, hadOutgoing;
        private Form inviteDialog;
        private string inviteShown;

        public PartyPanel()
        {
            // Repères du client lus d'avance hors du fil de l'interface (aucune lecture de PNG à l'affichage).
            foreach (string name in new[] { PartyArtworks.Party, PartyArtworks.Leader, PartyArtworks.Follow, PartyArtworks.Info })
                _ = PartyArtworks.LoadAsync(PartyArtworks.UiFamily, name);
        }

        public override string Title => PartyTexts.Get("PARTY", "Groupe");
        public override Image Icon => PartyArtworks.Ui(PartyArtworks.Party) ?? ClientAssets.Icon("icone-amis", 24);
        /// <summary>Boîte Oui / Non / Ignorer de l'invitation reçue encore affichée, sinon <c>null</c>.</summary>
        public Form InvitationDialog => inviteDialog;
        /// <summary>Dernier menu de membre construit (tests, diagnostic) ; il se libère à sa fermeture.</summary>
        public ContextMenuStrip LastMenu { get; private set; }
        /// <summary>Liste des membres dessinée comme les cases du bandeau du client.</summary>
        public PartyMemberList MemberList => members;

        protected override Control CreateView()
        {
            var page = Page();
            heading = MakeLabel("Aucun groupe", 10, true); heading.Dock = DockStyle.Top; heading.Height = 24; heading.AutoEllipsis = true;
            heading.Name = "party-heading";

            infoBar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = BotUi.Paper, Margin = new Padding(0) };
            infoIcon = new PictureBox { Dock = DockStyle.Left, Width = 22, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Padding = new Padding(0, 3, 4, 3) };
            summary = MakeLabel(string.Empty, 8.25f); summary.Dock = DockStyle.Fill; summary.ForeColor = BotUi.Muted;
            summary.TextAlign = ContentAlignment.MiddleLeft; summary.AutoEllipsis = true; summary.Name = "party-summary";
            infoBar.Controls.Add(summary); infoBar.Controls.Add(infoIcon);

            invitationBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = BotUi.PaperLight, Padding = new Padding(6, 3, 3, 3), Visible = false };
            invitation = MakeLabel(string.Empty, 8.25f); invitation.Dock = DockStyle.Fill; invitation.TextAlign = ContentAlignment.MiddleLeft;
            invitation.AutoEllipsis = true; invitation.Name = "party-invitation";
            cancelInvitation = MakeButton("Annuler", async (s, e) => await Run(party => party.CancelInvitationAsync()), false, 90);
            cancelInvitation.Dock = DockStyle.Right;
            invitationBar.Controls.Add(invitation); invitationBar.Controls.Add(cancelInvitation);

            members = new PartyMemberList { Dock = DockStyle.Top, Name = "party-members", AccessibleName = "Membres du groupe" };
            members.MemberClicked += (s, e) => ShowMemberMenu(e.Member.Id, e.Location);

            var inviteTitle = MakeLabel(PartyTexts.Get("ADD_TO_PARTY", "Inviter dans mon groupe"), 9, true); inviteTitle.Dock = DockStyle.Top;
            inviteTitle.Height = 22; inviteTitle.TextAlign = ContentAlignment.BottomLeft;
            var inviteBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 34, ColumnCount = 2, RowCount = 1, Margin = new Padding(0),
                Padding = new Padding(0, 3, 0, 2) };
            inviteBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); inviteBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            inviteName = new TextBox { Dock = DockStyle.Fill, Font = BotFonts.Get(9), BackColor = BotUi.PaperLight, ForeColor = BotUi.Ink,
                BorderStyle = BorderStyle.FixedSingle, MaxLength = 40, Name = "party-invite-name", AccessibleName = "Nom du personnage à inviter" };
            inviteName.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; await Invite(); } };
            // Libellé court : la case étroite du tiroir ne loge pas « Inviter dans mon groupe » (titre de la ligne).
            inviteButton = MakeButton("Inviter", async (s, e) => await Invite(), true, 92);
            inviteBar.Controls.Add(inviteName, 0, 0); inviteBar.Controls.Add(inviteButton, 1, 0);

            locate = MakeButton(PartyTexts.Get("PARTY_WHERE", "Localiser le groupe"), async (s, e) => await Run(party => party.LocateAsync()), false, 160);
            leave = MakeButton(PartyTexts.Get("LEAVE_PARTY", "Quitter"), async (s, e) => await Run(party => party.LeaveAsync()), false, 140);
            var actions = BotUi.Actions(locate, leave); actions.Dock = DockStyle.Top;

            var placesArea = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, 4, 0, 0) };
            var placesTitle = MakeLabel("Positions des membres (PW)", 9, true); placesTitle.Dock = DockStyle.Top; placesTitle.Height = 20;
            // Boussole du bandeau du client (ank.gapi.controls.Compass) : flèche vers la cible de IC depuis la carte actuelle.
            var compassRow = new Panel { Dock = DockStyle.Top, Height = PartyCompass.Side + 4, BackColor = BotUi.Paper, Padding = new Padding(0, 2, 0, 2) };
            compass = new PartyCompass { Dock = DockStyle.Left, Name = "party-compass-dial" };
            compassLabel = MakeLabel(string.Empty, 8.25f); compassLabel.Dock = DockStyle.Fill; compassLabel.ForeColor = BotUi.Muted;
            compassLabel.TextAlign = ContentAlignment.MiddleLeft; compassLabel.AutoEllipsis = true; compassLabel.Padding = new Padding(6, 0, 0, 0);
            compassLabel.Name = "party-compass";
            compassRow.Controls.Add(compassLabel); compassRow.Controls.Add(compass);
            locations = MakeList(8.25f, "Membre", "Position", "Carte");
            locations.Columns[0].Width = 150; locations.Columns[1].Width = 80; locations.Columns[2].Width = 70; locations.Name = "party-locations";
            placesArea.Controls.Add(locations); placesArea.Controls.Add(compassRow); placesArea.Controls.Add(placesTitle);

            status = MakeStatus(string.Empty); status.Name = "party-status";
            // Ordre d'ancrage : le dernier ajouté prend le bord en premier.
            page.Controls.Add(placesArea); page.Controls.Add(actions); page.Controls.Add(inviteBar); page.Controls.Add(inviteTitle); page.Controls.Add(members);
            page.Controls.Add(invitationBar); page.Controls.Add(infoBar); page.Controls.Add(heading); page.Controls.Add(status);
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            bound = game.Interactions?.Party;
            if (bound != null)
            {
                bound.Changed += OnPartyChanged;
                bound.Group.Changed += OnPartyChanged;
                bound.CompassChanged += OnCompassChanged;
                bound.LocationsReceived += OnLocationsReceived;
                bound.InvitationClosed += OnInvitationClosed;
            }
            session = game.Session;
            if (session != null) session.PartyInviteReceived += OnInviteReceived;
            wasActive = bound?.Group.IsActive == true;
            hadOutgoing = bound?.OutgoingInvitee != null;
        }

        protected override void OnUnbind(GameClass game)
        {
            if (bound != null)
            {
                bound.Changed -= OnPartyChanged;
                bound.Group.Changed -= OnPartyChanged;
                bound.CompassChanged -= OnCompassChanged;
                bound.LocationsReceived -= OnLocationsReceived;
                bound.InvitationClosed -= OnInvitationClosed;
            }
            if (session != null) session.PartyInviteReceived -= OnInviteReceived;
            bound = null; session = null;
            wasActive = hadOutgoing = false;
            CloseInviteDialog();
        }

        // ----- Événements du modèle (fil réseau) -----

        private void OnPartyChanged() => Post(() =>
        {
            PartyActions party = Game?.Interactions?.Party;
            bool active = party?.Group.IsActive == true, outgoing = party?.OutgoingInvitee != null;
            // Le client charge le bandeau Party sur PCK et le retire sur PV ; l'invitation envoyée s'affiche aussi.
            if ((active && !wasActive) || (outgoing && !hadOutgoing)) ShowUnlessServerWindow();
            else if (!active && wasActive && !outgoing && Host != null && Host.IsOpen(this)) RaiseClosed();
            wasActive = active; hadOutgoing = outgoing;
            if (inviteShown != null && !string.Equals(party?.PendingInviter, inviteShown, StringComparison.Ordinal)) CloseInviteDialog();
            RefreshView();
        });

        private void OnCompassChanged(Point? target) => Post(RefreshView);
        private void OnLocationsReceived(IReadOnlyList<PartyLocation> received) => Post(RefreshView);
        private void OnInvitationClosed() => Post(() => { CloseInviteDialog(); RefreshView(); });

        /// <summary>
        /// Invitation reçue (fil réseau) : prise en charge seulement si la fenêtre de jeu peut poser la question ; sinon le modèle
        /// refuse lui-même (<c>PR</c>). La réponse n'est envoyée qu'après le choix du joueur.
        /// </summary>
        private void OnInviteReceived(object sender, InvitationEventArgs e)
        {
            PartyActions party = bound;
            if (IsDisposed || e == null || party == null || Host == null || Host.IsDisposed) return;
            string inviter = e.InviterName;
            if (Post(() => AskInvitation(party, inviter))) e.Handled = true;
        }

        private async void AskInvitation(PartyActions party, string inviter)
        {
            try
            {
                if (IsDisposed || !string.Equals(party.PendingInviter, inviter, StringComparison.Ordinal)) return;
                CloseInviteDialog();
                Form[] before = BotDialogs.OpenDialogs.ToArray();
                Task<BotDialogResult> question = BotDialogs.AskYesNoIgnoreAsync(Host, PartyTexts.Get("PARTY", "Groupe"),
                    PartyTexts.Get("A_INVITE_YOU_IN_PARTY", "{0} propose de vous faire entrer dans son groupe. Accepter ?", inviter));
                inviteShown = inviter;
                inviteDialog = BotDialogs.OpenDialogs.Except(before).LastOrDefault();
                BotDialogResult answer = await question;
                if (string.Equals(inviteShown, inviter, StringComparison.Ordinal)) { inviteShown = null; inviteDialog = null; }
                // Invitation close entre-temps (PR de l'invitant, PCK, PV, déconnexion) : plus rien à répondre.
                if (!string.Equals(party.PendingInviter, inviter, StringComparison.Ordinal)) return;
                Func<Task<InteractionResult>> reply;
                if (answer == BotDialogResult.Yes) reply = party.AcceptAsync;
                else if (answer == BotDialogResult.Ignore) reply = party.IgnoreAsync;
                else reply = party.RefuseAsync;
                if (IsDisposed) await reply();
                else await ReportAsync(reply);
            }
            catch (Exception error) { Account?.Logger?.LogException(Reference, error); }
        }

        private void CloseInviteDialog()
        {
            Form dialog = inviteDialog;
            inviteDialog = null; inviteShown = null;
            if (dialog != null && !dialog.IsDisposed) dialog.Close();
        }

        private void ShowUnlessServerWindow()
        {
            // Un dialogue, un échange ou un zaap ouvert reste devant : le volet Groupe attend dans le tiroir.
            if (Host == null || Host.IsDisposed) return;
            if (Host.Current is GamePanel current && !ReferenceEquals(current, this) && current.IsServerWindowOpen) return;
            RequestShow();
        }

        /// <summary>Passe par la fenêtre de jeu (le tiroir jamais affiché n'a pas de poignée) ; faux si aucune fenêtre ne peut la recevoir.</summary>
        private bool Post(Action action) => ExchangePanel.PostToForm(Host, () => { if (!IsDisposed) action(); });

        // ----- Affichage -----

        public override void RefreshView()
        {
            PartyActions party = Game?.Interactions?.Party;
            if (party == null || members == null || IsDisposed) return;
            Groupe group = party.Group;
            IReadOnlyList<PartyMember> list = group.Members;
            long self = Game.character?.id ?? 0;
            if (group.IsActive)
            {
                string chief = group.Leader?.Name ?? group.LeaderName;
                heading.Text = (chief.Length > 0 ? "Groupe de " + chief : PartyTexts.Get("PARTY", "Groupe"))
                    + " · " + list.Count.ToString(CultureInfo.CurrentCulture) + "/" + Groupe.MaxMembers.ToString(CultureInfo.CurrentCulture);
            }
            else heading.Text = "Aucun groupe";
            // Infobulle d'informations du bandeau du client : niveau total et prospection totale.
            summary.Text = group.IsActive
                ? PartyTexts.Get("TOTAL_LEVEL", "Somme des niveaux") + " : " + group.TotalLevel.ToString(CultureInfo.CurrentCulture)
                    + " · " + PartyTexts.Get("TOTAL_DISCERNMENT", "Somme des prospections") + " : " + group.TotalProspection.ToString(CultureInfo.CurrentCulture)
                : "Invitez un joueur par son nom ou depuis son menu sur la carte.";
            infoIcon.Image = group.IsActive ? PartyArtworks.Ui(PartyArtworks.Info) : null;
            infoIcon.Width = infoIcon.Image == null ? 0 : 22;

            string outgoing = party.OutgoingInvitee, pending = party.PendingInviter;
            invitationBar.Visible = outgoing != null || pending != null;
            cancelInvitation.Visible = outgoing != null;
            invitation.Text = outgoing != null ? "Invitation envoyée à " + outgoing + " : en attente de sa réponse."
                : pending != null ? pending + " vous invite dans son groupe (boîte Oui / Non / Ignorer)." : string.Empty;

            members.SetMembers(list, group.LeaderId, group.FollowedId, self);

            Point? target = party.Compass;
            var map = Game.Map;
            compass.SetCoordinates(target, map == null ? Point.Empty : new Point(map.X, map.Y));
            compassLabel.Text = target.HasValue
                ? "Boussole : [" + target.Value.X.ToString(CultureInfo.InvariantCulture) + "," + target.Value.Y.ToString(CultureInfo.InvariantCulture) + "]"
                    + (compass.Angle.HasValue ? string.Empty : " (carte actuelle)")
                    + (group.FollowedId.HasValue ? " · suivi de " + (group.Find(group.FollowedId.Value)?.Name ?? "?") : string.Empty)
                : "Boussole : aucune cible.";
            locations.BeginUpdate(); locations.Items.Clear();
            foreach (PartyLocation place in party.Locations)
            {
                var row = locations.Items.Add(place.PlayerName.Length > 0 ? place.PlayerName : "?");
                row.SubItems.Add("[" + place.X.ToString(CultureInfo.InvariantCulture) + "," + place.Y.ToString(CultureInfo.InvariantCulture) + "]");
                row.SubItems.Add(place.MapId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                row.Tag = place;
            }
            locations.EndUpdate();
            status.Text = party.LastMessage;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            PartyActions party = Game?.Interactions?.Party;
            if (party == null || inviteButton == null) return;
            bool active = party.Group.IsActive;
            inviteButton.Enabled = Connected && !(active && party.Group.IsFull);
            locate.Enabled = Connected && active;
            leave.Enabled = Connected && active;
            cancelInvitation.Enabled = Connected && party.OutgoingInvitee != null;
        }

        private Task Invite()
        {
            string name = inviteName.Text.Trim();
            return Run(async party =>
            {
                InteractionResult result = await party.InviteAsync(name);
                if (result.Sent && !inviteName.IsDisposed) inviteName.Clear();
                return result;
            });
        }

        private Task Run(Func<PartyActions, Task<InteractionResult>> action)
        {
            PartyActions party = Game?.Interactions?.Party;
            return party == null ? Task.CompletedTask : ReportAsync(() => action(party));
        }

        /// <summary>
        /// Menu d'un membre, comme un clic sur sa case dans le bandeau du client : nom en tête, puis les entrées de groupe.
        /// Renvoie le menu affiché (ou <c>null</c> si le membre est inconnu) ; il se libère de lui-même une fois refermé.
        /// </summary>
        public ContextMenuStrip ShowMemberMenu(long memberId, Point? location = null)
        {
            PartyActions party = Game?.Interactions?.Party;
            PartyMember member = party?.Group.Find(memberId);
            if (member == null || members == null || members.IsDisposed) return null;
            var context = new ActorMenuContext(null, null, -1, Account, null, Host, Keys.None);
            var strip = new ContextMenuStrip { Renderer = new RetroMenuRenderer(), Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper,
                ForeColor = BotUi.Ink, ShowImageMargin = false, AccessibleName = "Menu du membre " + member.Name, Name = "party-member-menu" };
            strip.Items.Add(new ToolStripLabel(member.Name.Replace("&", "&&")) { Tag = ActorContextMenu.HeaderTag, Font = BotFonts.Get(8.25f, FontStyle.Bold),
                ForeColor = BotUi.Ink, AccessibleName = "Membre : " + member.Name });
            foreach (MenuEntry entry in PartyMenuProvider.MemberEntries(party, member, Game.character?.id ?? 0))
            {
                if (entry.EstSéparateur) { strip.Items.Add(new ToolStripSeparator()); continue; }
                if (entry.Action == null)
                {
                    strip.Items.Add(new ToolStripLabel(entry.Texte) { Font = BotFonts.Get(8.25f, FontStyle.Italic), ForeColor = BotUi.Muted });
                    continue;
                }
                MenuEntry chosen = entry;
                var item = new ToolStripMenuItem(entry.Texte) { Tag = entry, ToolTipText = entry.Infobulle, AccessibleName = entry.Texte,
                    Enabled = entry.Activé && Connected };
                item.Click += async (s, e) => await RunEntry(chosen, context);
                strip.Items.Add(item);
            }
            LastMenu = strip;
            Point where = location ?? members.PointToClient(Cursor.Position);
            strip.Closed += (s, e) => DisposeMenuLater(strip);
            strip.Show(members, where);
            return strip;
        }

        private async Task RunEntry(MenuEntry entry, ActorMenuContext context)
        {
            try { Feedback(await entry.Action(context)); }
            catch (Exception error) { Feedback(error.Message); Account?.Logger?.LogException(Reference, error); }
        }

        private void DisposeMenuLater(ContextMenuStrip strip)
        {
            // L'entrée cliquée reçoit son Click après la fermeture : la libération attend la fin du message en cours.
            Control owner = members;
            if (owner != null && !owner.IsDisposed && owner.IsHandleCreated)
                try { owner.BeginInvoke((Action)strip.Dispose); return; }
                catch (InvalidOperationException) { }
            strip.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            CloseInviteDialog();
            LastMenu = null;
        }
    }

    /// <summary>
    /// Boussole du volet, comme <c>ank.gapi.controls.Compass</c> du bandeau du client : fond <c>UI_BannerCompassBack</c>, flèche
    /// <c>UI_BannerCompassArrow</c> tournée de <c>atan2(Δy, Δx)</c> degrés de la carte actuelle vers la cible de <c>IC</c>,
    /// <c>UI_BannerCompassNoArrow</c> sur la carte cible, rien sans cible. Les PNG (lot des icônes, <see cref="ClientAssets"/>)
    /// sont lus en tâche de fond ; sans eux, un disque brun et une aiguille olive les remplacent.
    /// </summary>
    public sealed class PartyCompass : Control
    {
        public const int Side = 40;
        private const string Back = "UI_BannerCompassBack", Arrow = "UI_BannerCompassArrow", NoArrow = "UI_BannerCompassNoArrow";
        private Point? target;
        private Point current;

        public PartyCompass()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; Width = Side; Height = Side;
            AccessibleName = "Boussole";
            foreach (string name in new[] { Back, Arrow, NoArrow })
            {
                Task<Bitmap> load = ClientAssets.GetAsync("Client", name);
                if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
            }
        }

        /// <summary>Cible de la boussole (<c>IC</c>), ou <c>null</c>.</summary>
        public Point? Target => target;
        /// <summary>Coordonnées de la carte actuelle.</summary>
        public Point Current => current;
        /// <summary>Rotation de la flèche en degrés (sens horaire, 0 = est), <c>null</c> sans cible ou sur la carte cible.</summary>
        public float? Angle => ArrowAngle(current, target);

        /// <summary>Angle du client : <c>atan2(cible.y − y, cible.x − x)</c> en degrés ; <c>null</c> sans cible ou à la même position.</summary>
        public static float? ArrowAngle(Point from, Point? to)
        {
            if (!to.HasValue) return null;
            int dx = to.Value.X - from.X, dy = to.Value.Y - from.Y;
            if (dx == 0 && dy == 0) return null;
            return (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
        }

        /// <summary>Met à jour la cible et la position (fil de l'interface).</summary>
        public void SetCoordinates(Point? targetCoordinates, Point currentCoordinates)
        {
            if (target == targetCoordinates && current == currentCoordinates) return;
            target = targetCoordinates; current = currentCoordinates;
            AccessibleDescription = target.HasValue
                ? "[" + target.Value.X.ToString(CultureInfo.InvariantCulture) + "," + target.Value.Y.ToString(CultureInfo.InvariantCulture) + "]"
                : "aucune cible";
            Invalidate();
        }

        private void RedrawLater()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Invalidate); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            int side = Math.Min(Width, Height) - 2;
            if (side <= 4) return;
            var dial = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
            SmoothingMode previousMode = graphics.SmoothingMode;
            InterpolationMode previousInterpolation = graphics.InterpolationMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            try
            {
                // Lecture du cache seulement : aucun PNG n'est lu pendant le dessin. Les images partagées de ClientAssets sont
                // verrouillées le temps du dessin (GDI+ refuse deux dessins simultanés d'un même Bitmap).
                ClientAssets.TryCached("Client", Back, out Bitmap back);
                if (back != null) lock (back) graphics.DrawImage(back, dial);
                else
                {
                    using (var fill = new SolidBrush(BotUi.Frame)) graphics.FillEllipse(fill, dial);
                    using (var edge = new Pen(BotUi.Gold)) graphics.DrawEllipse(edge, dial);
                }
                if (!target.HasValue) return;
                float? angle = Angle;
                ClientAssets.TryCached("Client", angle.HasValue ? Arrow : NoArrow, out Bitmap needle);
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(dial.X + dial.Width / 2, dial.Y + dial.Height / 2);
                if (angle.HasValue) graphics.RotateTransform(angle.Value);
                var box = new RectangleF(-dial.Width / 2, -dial.Height / 2, dial.Width, dial.Height);
                if (needle != null) lock (needle) graphics.DrawImage(needle, box);
                else if (angle.HasValue)
                {
                    PointF[] arrow = { new PointF(box.Right - 3, 0), new PointF(-3, -box.Height / 6), new PointF(0, 0), new PointF(-3, box.Height / 6) };
                    using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillPolygon(olive, arrow);
                }
                else using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillEllipse(olive, -3, -3, 6, 6);
                graphics.Restore(state);
            }
            finally
            {
                graphics.SmoothingMode = previousMode;
                graphics.InterpolationMode = previousInterpolation;
            }
        }
    }

    /// <summary>Membre cliqué dans <see cref="PartyMemberList"/>.</summary>
    public sealed class PartyMemberEventArgs : EventArgs
    {
        public PartyMember Member { get; }
        public Point Location { get; }
        internal PartyMemberEventArgs(PartyMember member, Point location) { Member = member; Location = location; }
    }

    /// <summary>
    /// Cases des membres du groupe, du bandeau <c>Party</c> du client (<c>UI_PartyItem</c>) : fond brun, illustration de classe,
    /// jauge de vie verticale rouge à droite, couronne du chef en haut, flèche du membre suivi en bas ; à côté, nom, niveau et
    /// points de vie (l'infobulle du client). Une ligne par membre, par initiative décroissante comme le client. Les images
    /// arrivent en tâche de fond : la liste se redessine quand elles sont prêtes.
    /// </summary>
    public sealed class PartyMemberList : Control
    {
        public const int RowHeight = 42;
        public const int TileWidth = 30, TileHeight = 36;
        /// <summary>Rouge de la jauge <c>_mcHealth</c> (transformation de couleur de <c>UI_PartyItem</c>).</summary>
        public static readonly Color HealthColor = Color.FromArgb(216, 1, 1);
        private IReadOnlyList<PartyMember> items = new PartyMember[0];
        private long? leader, followed;
        private long self;
        private int hover = -1;
        private readonly ToolTip tips = new ToolTip { InitialDelay = 300, ReshowDelay = 100 };

        public PartyMemberList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BotUi.Paper; Font = BotFonts.Get(9, FontStyle.Bold); Cursor = Cursors.Hand; Height = RowHeight;
        }

        public event EventHandler<PartyMemberEventArgs> MemberClicked;
        public IReadOnlyList<PartyMember> Members => items;
        public long? LeaderId => leader;
        public long? FollowedId => followed;

        /// <summary>Remplace les membres affichés (fil de l'interface) et lance la lecture des illustrations manquantes.</summary>
        public void SetMembers(IReadOnlyList<PartyMember> members, long? leaderId, long? followedId, long selfId)
        {
            items = members ?? new PartyMember[0];
            leader = leaderId; followed = followedId; self = selfId;
            hover = -1;
            Height = Math.Max(1, items.Count) * RowHeight + 1;
            AccessibleDescription = string.Join(", ", items.Select(member => member.Name));
            foreach (PartyMember member in items)
            {
                Task<Bitmap> load = PartyArtworks.MiniAsync(member.Gfx);
                if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
            }
            foreach (string name in new[] { PartyArtworks.Leader, PartyArtworks.Follow })
            {
                Task<Bitmap> load = PartyArtworks.LoadAsync(PartyArtworks.UiFamily, name);
                if (!load.IsCompleted) load.ContinueWith(_ => RedrawLater(), TaskScheduler.Default);
            }
            Invalidate();
        }

        /// <summary>Rectangle de la case du membre de rang <paramref name="index"/>.</summary>
        public Rectangle TileBounds(int index) => new Rectangle(4, index * RowHeight + (RowHeight - TileHeight) / 2, TileWidth, TileHeight);

        /// <summary>Membre sous le point, ou <c>null</c>.</summary>
        public PartyMember MemberAt(Point point)
        {
            int index = point.Y < 0 ? -1 : point.Y / RowHeight;
            return index >= 0 && index < items.Count ? items[index] : null;
        }

        /// <summary>Simule un clic sur la ligne du membre (raccourci clavier, tests).</summary>
        public bool ClickMember(long id)
        {
            for (int index = 0; index < items.Count; index++)
                if (items[index].Id == id) { MemberClicked?.Invoke(this, new PartyMemberEventArgs(items[index], new Point(TileBounds(index).Right, TileBounds(index).Top))); return true; }
            return false;
        }

        private void RedrawLater()
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Invalidate); }
            catch (Exception error) when (error is InvalidOperationException || error is ObjectDisposedException) { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            using (var paper = new SolidBrush(BackColor)) graphics.FillRectangle(paper, ClientRectangle);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(graphics, "Aucun membre.", BotFonts.Get(9), new Rectangle(6, 0, Width - 12, RowHeight), BotUi.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                return;
            }
            Bitmap crown = PartyArtworks.Ui(PartyArtworks.Leader), arrow = PartyArtworks.Ui(PartyArtworks.Follow);
            using (var rule = new Pen(BotUi.Gold))
            for (int index = 0; index < items.Count; index++)
            {
                PartyMember member = items[index];
                var row = new Rectangle(0, index * RowHeight, Width, RowHeight);
                if (index == hover) using (var light = new SolidBrush(BotUi.PaperLight)) graphics.FillRectangle(light, row);
                DrawTile(graphics, member, TileBounds(index), crown, arrow);
                int left = TileBounds(index).Right + 10;
                string name = member.Name + (member.Id == self ? " (vous)" : string.Empty);
                TextRenderer.DrawText(graphics, name, BotFonts.Get(9, FontStyle.Bold), new Rectangle(left, row.Y + 4, Width - left - 4, 17), BotUi.Ink,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                string detail = PartyTexts.Get("LEVEL", "Niv.") + " " + member.Level.ToString(CultureInfo.CurrentCulture) + " · "
                    + member.Life.ToString(CultureInfo.CurrentCulture) + " / " + member.MaxLife.ToString(CultureInfo.CurrentCulture) + " "
                    + PartyTexts.Get("LIFEPOINTS", "PV").ToLowerInvariant()
                    + (member.Id == leader ? " · chef" : string.Empty) + (member.Id == followed ? " · suivi" : string.Empty);
                TextRenderer.DrawText(graphics, detail, BotFonts.Get(8.25f), new Rectangle(left, row.Y + 21, Width - left - 4, 16), BotUi.Muted,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                graphics.DrawLine(rule, 2, row.Bottom, Width - 3, row.Bottom);
            }
        }

        private void DrawTile(Graphics graphics, PartyMember member, Rectangle tile, Bitmap crown, Bitmap arrow)
        {
            using (var back = new SolidBrush(BotUi.Frame)) graphics.FillRectangle(back, tile);
            var health = new Rectangle(tile.Right - 5, tile.Y + 2, 3, tile.Height - 4);
            var picture = new Rectangle(tile.X + 2, tile.Y + 2, tile.Width - 9, tile.Height - 4);
            Bitmap mini = PartyArtworks.Mini(member.Gfx);
            if (mini != null) ClientAssets.DrawFit(graphics, mini, picture, true);
            else
            {
                string initial = member.Name.Length > 0 ? member.Name.Substring(0, 1).ToUpperInvariant() : "?";
                TextRenderer.DrawText(graphics, initial, BotFonts.Get(11, FontStyle.Bold), picture, BotUi.Paper,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            // Jauge de vie du client : hauteur = PV / PV max, remplie depuis le bas.
            using (var empty = new SolidBrush(BotUi.FrameLight)) graphics.FillRectangle(empty, health);
            int filled = (int)Math.Round(health.Height * member.LifeRatio);
            if (filled > 0) using (var red = new SolidBrush(HealthColor)) graphics.FillRectangle(red, health.X, health.Bottom - filled, health.Width, filled);
            using (var border = new Pen(BotUi.Gold)) graphics.DrawRectangle(border, tile.X, tile.Y, tile.Width - 1, tile.Height - 1);
            if (member.Id == leader)
            {
                var place = new RectangleF(tile.X + (tile.Width - 14) / 2f, tile.Y - 5, 14, 11);
                if (crown != null) ClientAssets.DrawFit(graphics, crown, place);
                else DrawCrown(graphics, place);
            }
            if (member.Id == followed)
            {
                var place = new RectangleF(tile.X - 3, tile.Bottom - 10, 11, 11);
                if (arrow != null) ClientAssets.DrawFit(graphics, arrow, place);
                else using (var olive = new SolidBrush(BotUi.Olive)) graphics.FillRectangle(olive, place);
            }
        }

        private static void DrawCrown(Graphics graphics, RectangleF place)
        {
            var points = new[] {
                new PointF(place.Left, place.Bottom), new PointF(place.Left, place.Top + 2), new PointF(place.Left + place.Width / 4, place.Top + place.Height / 2),
                new PointF(place.Left + place.Width / 2, place.Top), new PointF(place.Right - place.Width / 4, place.Top + place.Height / 2),
                new PointF(place.Right, place.Top + 2), new PointF(place.Right, place.Bottom) };
            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var gold = new SolidBrush(Color.FromArgb(255, 176, 0))) graphics.FillPolygon(gold, points);
            using (var edge = new Pen(BotUi.FrameLight)) graphics.DrawPolygon(edge, points);
            graphics.SmoothingMode = previous;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = e.Y / RowHeight;
            if (index >= items.Count) index = -1;
            if (index == hover) return;
            hover = index; Invalidate();
            if (index < 0) { tips.SetToolTip(this, null); return; }
            PartyMember member = items[index];
            tips.SetToolTip(this, member.Name + "\n" + PartyTexts.Get("LEVEL", "Niveau") + " : " + member.Level.ToString(CultureInfo.CurrentCulture)
                + "\n" + PartyTexts.Get("LIFEPOINTS", "Vie") + " : " + member.Life.ToString(CultureInfo.CurrentCulture) + " / " + member.MaxLife.ToString(CultureInfo.CurrentCulture));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover < 0) return;
            hover = -1; Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            PartyMember member = MemberAt(e.Location);
            if (member != null) MemberClicked?.Invoke(this, new PartyMemberEventArgs(member, e.Location));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
