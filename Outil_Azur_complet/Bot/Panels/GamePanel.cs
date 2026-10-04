using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>
    /// Base commune des volets : création paresseuse du contenu, abonnements liés à la session (<see cref="OnBind"/> /
    /// <see cref="OnUnbind"/>), passage sur le thread de l'interface et accès aux services du tiroir (connexion, retour d'action).
    /// Les événements réseau arrivent sur d'autres threads : tout rafraîchissement passe par <see cref="OnUi"/>.
    /// </summary>
    public abstract class GamePanel : IGamePanel
    {
        private Control view;
        private bool disposed;

        public abstract string Title { get; }
        public virtual Image Icon => null;
        /// <summary>Volet ouvert par une fenêtre du serveur (dialogue, zaap, échange) : un seul à la fois dans le tiroir.</summary>
        public virtual bool IsModal => false;
        /// <summary>Vrai tant que la fenêtre correspondante est ouverte côté serveur : le volet reste alors affiché.</summary>
        public virtual bool IsServerWindowOpen => false;
        /// <summary>Contenu créé au premier accès ; après <see cref="Dispose()"/>, le contrôle libéré reste renvoyé.</summary>
        public Control View
        {
            get
            {
                if (view == null) { if (disposed) throw new ObjectDisposedException(GetType().Name); view = CreateView(); }
                return view;
            }
        }
        public PanelHost Host { get; private set; }
        public event EventHandler Closed;

        protected GameClass Game { get; private set; }
        protected Accounts Account => Host?.Account;
        /// <summary>Connexion au serveur active (remplace l'ancien indicateur « bouton d'envoi actif »).</summary>
        protected bool Connected => Host != null && Host.IsConnected;
        protected bool IsDisposed => disposed;

        /// <summary>Construit le contenu une seule fois ; il est ensuite conservé pendant toute la vie du volet.</summary>
        protected abstract Control CreateView();
        /// <summary>Abonnements aux événements de la session ; appelé sur le thread de l'interface.</summary>
        protected virtual void OnBind(GameClass game) { }
        /// <summary>Désabonnements ; doit tolérer une session déjà libérée.</summary>
        protected virtual void OnUnbind(GameClass game) { }
        /// <summary>Recopie l'état de la session dans le contenu (thread de l'interface).</summary>
        public virtual void RefreshView() { }

        /// <summary>
        /// Fermeture demandée par l'utilisateur (bouton ×, Échap). Renvoie <c>true</c> pour retirer le volet tout de suite ;
        /// un volet de fenêtre serveur envoie plutôt la sortie du client (<c>DV</c>, <c>WV</c>, <c>EV</c>) et attend la réponse.
        /// </summary>
        protected internal virtual bool OnUserClose() => true;

        public void Bind(GameClass game)
        {
            if (disposed || ReferenceEquals(game, Game)) return;
            if (Game != null) SafeUnbind(Game);
            Game = game;
            if (game == null) return;
            OnBind(game);
            RefreshView();
        }

        internal void Attach(PanelHost host) { Host = host; }

        /// <summary>Exécute l'action sur le thread de l'interface (directement si l'on y est déjà).</summary>
        protected void OnUi(Action action)
        {
            if (disposed || action == null) return;
            Control target = Host != null && !Host.IsDisposed ? (Control)Host : view;
            if (target == null || target.IsDisposed) return;
            BotUi.OnUi(target, () => { if (!disposed) action(); });
        }

        /// <summary>Message court affiché dans le bandeau de la fenêtre de jeu.</summary>
        protected void Feedback(string message) { if (!string.IsNullOrEmpty(message)) Host?.Report(message); }
        /// <summary>Demande l'affichage du volet dans le tiroir.</summary>
        protected void RequestShow() { if (Host != null && !Host.IsDisposed) Host.Show(this); }
        /// <summary>Signale au tiroir que le volet doit être retiré.</summary>
        protected void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

        /// <summary>Envoie une demande de fenêtre serveur et affiche sa réponse locale (paquet envoyé ou raison du refus).</summary>
        protected async Task ReportAsync(Func<Task<InteractionResult>> action)
        {
            try
            {
                InteractionResult result = await action();
                Feedback(result.Message);
                if (!result.Sent) Account?.Logger?.LogError("INTERFACE", result.Message);
            }
            catch (Exception error) { Feedback(error.Message); }
        }

        /// <summary>
        /// Remplace la police créée par une fabrique de <c>BotUi</c> (contrôle neuf, sans parent) par une police partagée
        /// et libère l'ancienne, qui n'appartenait qu'à ce contrôle.
        /// </summary>
        protected static T WithFont<T>(T control, Font font) where T : Control
        {
            Font previous = control.Parent == null ? control.Font : null;
            control.Font = font;
            if (previous != null && !ReferenceEquals(previous, font) && !ReferenceEquals(previous, Control.DefaultFont) && !BotFonts.IsShared(previous))
                previous.Dispose();
            return control;
        }

        // Fabriques de BotUi avec polices partagées : mêmes rendus que les anciens onglets, sans police perdue par contrôle.
        protected static Label MakeLabel(string text, float size = 10, bool bold = false) =>
            WithFont(BotUi.Label(text, size, bold), BotFonts.Get(size, bold ? FontStyle.Bold : FontStyle.Regular));
        protected static Label MakeStatus(string text) => WithFont(BotUi.Status(text), BotFonts.Get(9));
        protected static Control MakeButton(string title, EventHandler action, bool primary = false, int width = 150) =>
            WithFont(BotUi.Button(title, action, primary, width), BotFonts.Get(9, primary ? FontStyle.Bold : FontStyle.Regular));
        protected static ListView MakeList(float size, params string[] columns) => WithFont(BotUi.List(columns), BotFonts.Get(size));
        protected static RichTextBox MakeJournal() => WithFont(BotUi.Journal(), BotFonts.Get(9));

        /// <summary>Page de fond des volets : parchemin et marge des anciens onglets du tiroir.</summary>
        protected static Panel Page() => new Panel { Dock = DockStyle.Fill, BackColor = BotUi.Paper, Padding = new Padding(7), Margin = new Padding(0) };

        private void SafeUnbind(GameClass game)
        {
            try { OnUnbind(game); }
            catch (Exception error) when (error is NullReferenceException || error is ObjectDisposedException) { }
        }

        public void Dispose()
        {
            if (disposed) return;
            if (Game != null) SafeUnbind(Game);
            Game = null; disposed = true;
            Dispose(true);
            view?.Dispose();
        }

        /// <summary>Libère les ressources propres du volet (images non partagées, minuteries).</summary>
        protected virtual void Dispose(bool disposing) { }
    }
}
