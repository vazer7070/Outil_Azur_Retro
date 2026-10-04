using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>Lien d'une ligne du chat (<c>Banner.onHref</c> du client).</summary>
    public enum ChatLinkKind
    {
        /// <summary>Nom d'un joueur (<c>ShowPlayerPopupMenu</c>).</summary>
        Player,
        /// <summary>Coordonnées <c>[x,y]</c> (<c>updateCompass</c>).</summary>
        Coordinates,
    }

    /// <summary>Un lien cliquable : nom de joueur (avec l'identifiant reçu dans <c>cMK</c>) ou coordonnées de carte.</summary>
    public sealed class ChatLink
    {
        private ChatLink(ChatLinkKind kind, string name, long? actorId, int x, int y, ChatMessage message)
        {
            Kind = kind; Name = name ?? string.Empty; ActorId = actorId; X = x; Y = y; Message = message;
        }

        public ChatLinkKind Kind { get; }
        public string Name { get; }
        public long? ActorId { get; }
        public int X { get; }
        public int Y { get; }
        /// <summary>Message qui porte le lien.</summary>
        public ChatMessage Message { get; }

        public static ChatLink Player(string name, long? actorId, ChatMessage message) => new ChatLink(ChatLinkKind.Player, name, actorId, 0, 0, message);
        public static ChatLink Coordinates(int x, int y, ChatMessage message) => new ChatLink(ChatLinkKind.Coordinates, null, null, x, y, message);

        public override string ToString() => Kind == ChatLinkKind.Player ? Name
            : "[" + X.ToString(CultureInfo.InvariantCulture) + "," + Y.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>Clic sur un lien du chat.</summary>
    public sealed class ChatLinkEventArgs : EventArgs
    {
        public ChatLinkEventArgs(ChatLink link, MouseButtons button, Keys modifiers, Point location)
        {
            Link = link; Button = button; Modifiers = modifiers; Location = location;
        }
        public ChatLink Link { get; }
        public MouseButtons Button { get; }
        public Keys Modifiers { get; }
        /// <summary>Position dans le contrôle qui a reçu le clic.</summary>
        public Point Location { get; }
        public bool Shift => (Modifiers & Keys.Shift) == Keys.Shift;
    }

    /// <summary>Coordonnées trouvées dans un texte (position et valeurs).</summary>
    public struct ChatCoordinateMatch
    {
        public int Start; public int Length; public int X; public int Y;
    }

    /// <summary>
    /// Liens du chat, comme <c>ChatManager.getLinkName</c>, <c>parseInlinePos</c> et <c>GameManager.showPlayerPopupMenu</c>
    /// du client 1.34. Nom d'un joueur présent sur la carte : menu contextuel de l'acteur (<see cref="ActorContextMenu"/>,
    /// fournisseurs des autres lots) ; joueur absent, ou présent sans aucune entrée enregistrée : menu réduit
    /// « message privé » (prépare <c>/w &lt;nom&gt; </c>), « informations » (<c>BW&lt;nom&gt;</c>) et « ajouter à mes amis »
    /// (<c>FA&lt;nom&gt;</c>). Maj + clic prépare directement le chuchotement, comme le client.
    /// </summary>
    public sealed class ChatLinks
    {
        /// <summary>Coordonnées remplacées au plus par message (<c>ChatManager.MAX_POS_REPLACE</c>).</summary>
        public const int MaxCoordinateLinks = 6;
        /// <summary>Le client ne relie que des coordonnées de moins de 150 en valeur absolue.</summary>
        public const int CoordinateLimit = 150;
        private static readonly Regex CoordinatePattern = new Regex(@"\[\s*(-?\d{1,4})\s*,\s*(-?\d{1,4})\s*\]", RegexOptions.CultureInvariant);

        private readonly Func<Accounts> account;
        private readonly Func<InteractionRouter> router;
        private readonly Action<string> prefill;
        private readonly Action<string> feedback;

        public ChatLinks(Func<Accounts> account, Func<InteractionRouter> router, Action<string> prefill, Action<string> feedback)
        {
            this.account = account ?? (() => null);
            this.router = router ?? (() => null);
            this.prefill = prefill ?? (text => { });
            this.feedback = feedback ?? (text => { });
        }

        /// <summary>Dernier menu affiché pour un nom (diagnostic et tests) ; il se libère à sa fermeture.</summary>
        public ContextMenuStrip LastMenu { get; private set; }

        /// <summary>Coordonnées <c>[x,y]</c> ou <c>[x, y]</c> d'un texte (au plus <see cref="MaxCoordinateLinks"/>).</summary>
        public static IReadOnlyList<ChatCoordinateMatch> FindCoordinates(string text)
        {
            var found = new List<ChatCoordinateMatch>();
            if (string.IsNullOrEmpty(text) || text.IndexOf('[') < 0) return found;
            foreach (Match match in CoordinatePattern.Matches(text))
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int y)) continue;
                if (Math.Abs(x) >= CoordinateLimit || Math.Abs(y) >= CoordinateLimit) continue;
                found.Add(new ChatCoordinateMatch { Start = match.Index, Length = match.Length, X = x, Y = y });
                if (found.Count == MaxCoordinateLinks) break;
            }
            return found;
        }

        /// <summary>Direction d'une carte cible depuis la carte courante (y croît vers le sud, comme dans le client).</summary>
        public static string DescribeCompass(int x, int y, Map current)
        {
            string target = "[" + x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture) + "]";
            if (current == null || current.MapID == 0) return "Boussole vers " + target + " (carte actuelle inconnue).";
            int dx = x - current.X, dy = y - current.Y;
            if (dx == 0 && dy == 0) return "Boussole : vous êtes déjà en " + target + ".";
            var parts = new List<string>();
            if (dy != 0) parts.Add(Math.Abs(dy) + (Math.Abs(dy) > 1 ? " cartes" : " carte") + (dy < 0 ? " au nord" : " au sud"));
            if (dx != 0) parts.Add(Math.Abs(dx) + (Math.Abs(dx) > 1 ? " cartes" : " carte") + (dx > 0 ? " à l'est" : " à l'ouest"));
            return "Boussole vers " + target + " : " + string.Join(", ", parts) + " (vous êtes en " + current.GetCoordinates + ").";
        }

        /// <summary>Nom affichable sans danger dans un paquet : deux caractères au moins, sans espace, « | », « ; », « &lt; » ni « &gt; ».</summary>
        public static bool IsValidName(string name) =>
            !string.IsNullOrEmpty(name) && name.Length >= 2 &&
            !name.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '|' || c == ';' || c == '<' || c == '>');

        /// <summary>Vrai si le nom est celui du personnage du compte.</summary>
        public bool IsSelf(string name)
        {
            string self = account()?.Game?.character?.Name;
            return !string.IsNullOrEmpty(self) && string.Equals(self, name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Joueur de la carte portant ce nom (identifiant de <c>cMK</c> d'abord, puis nom sans casse), ou null.</summary>
        public PlayerActor FindPlayer(string name, long? actorId)
        {
            Map map = account()?.Game?.Map;
            if (map == null || string.IsNullOrEmpty(name)) return null;
            if (actorId.HasValue && map.GetActor(actorId.Value) is PlayerActor byId && string.Equals(byId.Name, name, StringComparison.OrdinalIgnoreCase))
                return byId;
            PlayerActor present = map.Players.FirstOrDefault(player => string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase));
            if (present != null) return present;
            return map.Self is PlayerActor self && string.Equals(self.Name, name, StringComparison.OrdinalIgnoreCase) ? self : null;
        }

        /// <summary>
        /// Clic sur un nom : Maj prépare <c>/w &lt;nom&gt; </c> ; sinon menu de l'acteur s'il est sur la carte et qu'un lot
        /// lui fournit des entrées, menu réduit dans les autres cas. Renvoie le menu affiché (ou null).
        /// </summary>
        public ContextMenuStrip OnPlayerClicked(ChatLink link, Control owner, Point location, Keys modifiers)
        {
            if (link == null || link.Kind != ChatLinkKind.Player || !IsValidName(link.Name)) return null;
            if ((modifiers & Keys.Shift) == Keys.Shift)
            {
                if (!IsSelf(link.Name)) prefill("/w " + link.Name + " ");
                return null;
            }
            PlayerActor actor = FindPlayer(link.Name, link.ActorId);
            InteractionRouter map = router();
            if (actor != null && !actor.IsSelf && map != null)
            {
                bool hasEntries;
                try { hasEntries = map.Registry.EntriesFor(actor, account()?.Game).Count > 0; }
                catch (Exception) { hasEntries = false; }
                if (hasEntries)
                {
                    ReleaseLastMenu();
                    ContextMenuStrip strip = map.ShowActorMenu(new Entites[] { actor }, unchecked((short)actor.CellId), Keys.None, false);
                    LastMenu = strip; return strip;
                }
            }
            ContextMenuStrip reduced = BuildPlayerMenu(link.Name);
            Show(reduced, owner, location);
            return reduced;
        }

        /// <summary>Menu réduit d'un joueur (nom en tête, puis les actions du chat) ; l'appelant l'affiche ou le libère.</summary>
        public ContextMenuStrip BuildPlayerMenu(string name)
        {
            var strip = NewMenu("chat-player-menu", "Menu du joueur " + name);
            strip.Items.Add(Header(name));
            foreach (ToolStripItem item in PlayerItems(name)) strip.Items.Add(item);
            return strip;
        }

        /// <summary>Entrées du menu réduit (aussi utilisées comme sous-menu d'une ligne).</summary>
        public IList<ToolStripItem> PlayerItems(string name)
        {
            var items = new List<ToolStripItem>();
            if (!IsValidName(name)) { items.Add(new ToolStripMenuItem("Nom illisible") { Enabled = false }); return items; }
            if (IsSelf(name)) { items.Add(new ToolStripMenuItem("Votre personnage") { Enabled = false }); return items; }
            items.Add(Item(ChatUiText.Get("WISPER_MESSAGE", "Envoyer un message privé"), "Préparer « /w " + name + " » dans la saisie (Maj + clic)",
                () => prefill("/w " + name + " ")));
            items.Add(Item(ChatUiText.Get("WHOIS", "Informations sur le joueur"), "Demander au serveur où se trouve ce joueur (BW)",
                () => _ = SendAsync("BW" + name, "Informations demandées pour " + name + ".")));
            items.Add(Item(ChatUiText.Get("ADD_TO_FRIENDS", "Ajouter aux amis"), "Ajouter ce joueur à la liste d'amis (FA)",
                () => _ = SendAsync("FA" + name, "Ajout de " + name + " aux amis demandé.")));
            return items;
        }

        internal static ContextMenuStrip NewMenu(string name, string accessibleName) =>
            new ContextMenuStrip { Renderer = new RetroMenuRenderer(), Font = BotFonts.Get(8.25f), BackColor = BotUi.Paper, ForeColor = BotUi.Ink,
                ShowImageMargin = false, Name = name, AccessibleName = accessibleName };

        internal static ToolStripItem Header(string text) =>
            new ToolStripLabel((text ?? string.Empty).Replace("&", "&&")) { Tag = ActorContextMenu.HeaderTag, Font = BotFonts.Get(8.25f, FontStyle.Bold),
                ForeColor = BotUi.Ink, AccessibleName = text };

        internal static ToolStripMenuItem Item(string text, string tooltip, Action action)
        {
            var item = new ToolStripMenuItem((text ?? string.Empty).Trim()) { ToolTipText = tooltip, AccessibleName = (text ?? string.Empty).Trim() };
            item.Click += (s, e) => action();
            return item;
        }

        /// <summary>Affiche un menu sous la souris ; il se libère après sa fermeture (et le clic éventuel).</summary>
        internal void Show(ContextMenuStrip strip, Control owner, Point location)
        {
            ReleaseLastMenu();
            LastMenu = strip;
            if (strip == null || owner == null || owner.IsDisposed) return;
            strip.Closed += (s, e) =>
            {
                if (owner.IsHandleCreated && !owner.IsDisposed)
                    try { owner.BeginInvoke((Action)(() => { if (ReferenceEquals(LastMenu, strip)) LastMenu = null; strip.Dispose(); })); return; }
                    catch (InvalidOperationException) { }
                if (ReferenceEquals(LastMenu, strip)) LastMenu = null;
                strip.Dispose();
            };
            try { strip.Show(owner, location); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
            { account()?.Logger?.LogException("CHAT", error); }
        }

        /// <summary>Ferme et libère le menu précédent.</summary>
        public void ReleaseLastMenu()
        {
            ContextMenuStrip previous = LastMenu; LastMenu = null;
            if (previous == null || previous.IsDisposed) return;
            InteractionRouter map = router();
            if (map != null && ReferenceEquals(map.LastMenu, previous)) return; // menu d'acteur : libéré par le routeur
            if (previous.Visible) previous.Close();
            previous.Dispose();
        }

        private async Task SendAsync(string packet, string done)
        {
            var connection = account()?.Connexion;
            if (connection == null || !connection.IsConnected()) { feedback("Connectez le personnage avant d'utiliser ce menu."); return; }
            try { await connection.SendPacketAsync(packet).ConfigureAwait(false); feedback(done); }
            catch (Exception error)
            {
                account()?.Logger?.LogException("CHAT", error);
                feedback("Envoi impossible : " + error.Message);
            }
        }
    }
}
