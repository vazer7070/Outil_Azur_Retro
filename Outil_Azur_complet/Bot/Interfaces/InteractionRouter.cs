using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Controls;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps.Interfaces;

namespace Outil_Azur_complet.Bot.Interfaces
{
    /// <summary>Clic sur la carte transmis aux abonnés du routeur ; <see cref="Handled"/> arrête le traitement.</summary>
    public sealed class MapClickEventArgs : EventArgs
    {
        public MapClickEventArgs(short cellId, MouseButtons button, Keys modifiers, Entites actor, IReadOnlyList<Entites> actors)
        {
            CellId = cellId; Button = button; Modifiers = modifiers; Actor = actor; Actors = actors ?? new Entites[0];
        }
        public short CellId { get; }
        public MouseButtons Button { get; }
        public Keys Modifiers { get; }
        /// <summary>Acteur visé (le premier de la cellule), ou <c>null</c> pour un clic sur une cellule.</summary>
        public Entites Actor { get; }
        public IReadOnlyList<Entites> Actors { get; }
        public bool Shift => (Modifiers & Keys.Shift) == Keys.Shift;
        public bool Ctrl => (Modifiers & Keys.Control) == Keys.Control;
        public bool Handled { get; set; }
    }

    /// <summary>Abonné asynchrone d'un clic : le routeur attend chaque abonné et s'arrête dès que l'un traite le clic.</summary>
    public delegate Task MapClickHandler(object sender, MapClickEventArgs e);

    /// <summary>
    /// Routeur des clics de la carte. Clic gauche sur une cellule → <see cref="MoveRequested"/> (le traitement par défaut
    /// de la vue lance le sort sélectionné, se place ou se déplace en combat, utilise le zaap ou se déplace) ; clic gauche
    /// sur un acteur → <see cref="ActorClicked"/>, puis les fournisseurs qui implémentent <see cref="IActorClickHandler"/>,
    /// puis l'entrée « par défaut » du menu, sinon déplacement ; clic droit sur un acteur → <see cref="ActorMenuRequested"/>
    /// puis le menu contextuel (Ctrl + clic droit : un sous-menu par acteur de la cellule). Maj et Ctrl sont transmis.
    /// Hors du thread de l'interface, n'appeler que <see cref="Report"/>.
    /// </summary>
    public sealed class InteractionRouter : IDisposable
    {
        private readonly Control owner;
        private readonly Func<Accounts> account;
        private readonly Func<short, IReadOnlyList<Entites>> actorsAt;
        private readonly Func<bool> cellActionFirst;
        private readonly Func<short, Task> defaultCellAction;
        private bool disposed;

        public InteractionRouter(Control owner, Func<Accounts> account, Func<short, IReadOnlyList<Entites>> actorsAt,
            Func<bool> cellActionFirst, Func<short, Task> defaultCellAction)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.account = account ?? (() => null);
            this.actorsAt = actorsAt ?? (cell => new Entites[0]);
            this.cellActionFirst = cellActionFirst ?? (() => false);
            this.defaultCellAction = defaultCellAction;
        }

        /// <summary>Registre des menus (celui de l'application par défaut ; un test peut en fournir un autre).</summary>
        public ActorMenuRegistry Registry { get; set; } = ActorMenuRegistry.Default;
        /// <summary>Tiroir des volets transmis aux entrées de menu (pour ouvrir un volet).</summary>
        public PanelHost Panels { get; set; }
        /// <summary>Dernier menu construit (diagnostic et tests) ; il est libéré à sa fermeture.</summary>
        public ContextMenuStrip LastMenu { get; private set; }
        public Accounts Account => account();

        /// <summary>Clic gauche sur une cellule (ou action « se déplacer ici » d'un menu).</summary>
        public event MapClickHandler MoveRequested;
        /// <summary>Clic gauche sur un acteur, avant les fournisseurs de menus et l'action par défaut.</summary>
        public event MapClickHandler ActorClicked;
        /// <summary>Clic droit sur un acteur, avant l'affichage du menu contextuel.</summary>
        public event MapClickHandler ActorMenuRequested;
        /// <summary>Message court pour le bandeau (résultat d'une action, refus local).</summary>
        public event Action<string> Feedback;

        /// <summary>Traite un clic de la carte (cellule, bouton, touches Maj/Ctrl) ; ne lève jamais d'exception.</summary>
        public async Task RouteAsync(short cellId, MouseButtons button, Keys modifiers = Keys.None)
        {
            if (disposed) return;
            try
            {
                IReadOnlyList<Entites> actors = actorsAt(cellId) ?? new Entites[0];
                if (button == MouseButtons.Left)
                {
                    if (actors.Count == 0 || cellActionFirst()) { await RequestMoveAsync(cellId, modifiers); return; }
                    await ActorLeftClickAsync(cellId, actors, modifiers);
                }
                else if (button == MouseButtons.Right && actors.Count > 0)
                {
                    var args = new MapClickEventArgs(cellId, button, modifiers, actors[0], actors);
                    if (await RaiseAsync(ActorMenuRequested, args)) return;
                    ShowActorMenu(actors, cellId, modifiers, (modifiers & Keys.Control) == Keys.Control);
                }
            }
            catch (Exception error)
            {
                Report("Action impossible : " + error.Message);
                Account?.Logger?.LogException("CARTE", error);
            }
        }

        /// <summary>Demande le traitement d'un clic gauche sur la cellule (déplacement, sort, placement, zaap).</summary>
        public async Task RequestMoveAsync(short cellId, Keys modifiers = Keys.None)
        {
            if (disposed) return;
            var args = new MapClickEventArgs(cellId, MouseButtons.Left, modifiers, null, null);
            if (await RaiseAsync(MoveRequested, args)) return;
            if (defaultCellAction != null) await defaultCellAction(cellId);
        }

        /// <summary>Construit et affiche le menu des acteurs de la cellule sous la souris.</summary>
        public ContextMenuStrip ShowActorMenu(IReadOnlyList<Entites> actors, short cellId, Keys modifiers, bool perActor)
        {
            if (disposed || actors == null || actors.Count == 0) return null;
            ReleaseLastMenu();
            ContextMenuStrip strip = ActorContextMenu.Build(this, actors, cellId, modifiers, perActor);
            LastMenu = strip;
            try { ActorContextMenu.Show(strip, owner, owner.PointToClient(Cursor.Position)); }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException || error is NotImplementedException)
            { Account?.Logger?.LogException("CARTE", error); }
            return strip;
        }

        public ActorMenuContext CreateContext(Entites actor, IReadOnlyList<Entites> actors, short cellId, Keys modifiers) =>
            new ActorMenuContext(actor, actors, cellId, Account, this, Panels, modifiers);

        /// <summary>Exécute une entrée de menu et affiche son message. Renvoie <c>false</c> si elle n'a pas pu s'exécuter.</summary>
        public async Task<bool> ExecuteAsync(MenuEntry entry, ActorMenuContext context)
        {
            if (entry?.Action == null || !entry.Activé || disposed) return false;
            try
            {
                string message = await entry.Action(context);
                if (!string.IsNullOrEmpty(message)) { Report(message); Account?.Logger?.LogInfo("CARTE", message); }
                return true;
            }
            catch (Exception error)
            {
                Report("Action impossible : " + error.Message);
                Account?.Logger?.LogException("CARTE", error);
                return false;
            }
        }

        /// <summary>Transmet un message au bandeau (sur le thread de l'interface).</summary>
        public void Report(string message)
        {
            if (string.IsNullOrEmpty(message) || disposed) return;
            BotUi.OnUi(owner, () => Feedback?.Invoke(message));
        }

        internal void ReportProviderError(IActorMenuProvider provider, Exception error)
        {
            Account?.Logger?.LogError("CARTE", "Menu « " + provider.GetType().Name + " » ignoré : " + error.Message);
        }

        private async Task ActorLeftClickAsync(short cellId, IReadOnlyList<Entites> actors, Keys modifiers)
        {
            Entites actor = actors[0];
            var args = new MapClickEventArgs(cellId, MouseButtons.Left, modifiers, actor, actors);
            if (await RaiseAsync(ActorClicked, args)) return;
            ActorMenuContext context = CreateContext(actor, actors, cellId, modifiers);
            foreach (IActorClickHandler handler in Registry.ClickHandlersFor(actor, ReportProviderError))
                if (await handler.OnActorClickAsync(context)) return;
            MenuEntry entry = Registry.DefaultEntryFor(actor, context.Game, ReportProviderError);
            if (entry != null) { await ExecuteAsync(entry, context); return; }
            await RequestMoveAsync(cellId, modifiers);
        }

        private async Task<bool> RaiseAsync(MapClickHandler handlers, MapClickEventArgs args)
        {
            if (handlers == null) return false;
            foreach (MapClickHandler handler in handlers.GetInvocationList())
            {
                await handler(this, args);
                if (args.Handled) return true;
            }
            return false;
        }

        /// <summary>
        /// Referme et libère le menu précédent (nouveau clic droit ou carte libérée). Un menu refermé par le clic
        /// d'une entrée se libère de lui-même après ce clic (<see cref="ActorContextMenu.Show"/>).
        /// </summary>
        private void ReleaseLastMenu()
        {
            ContextMenuStrip previous = LastMenu; LastMenu = null;
            if (previous == null || previous.IsDisposed) return;
            if (previous.Visible) previous.Close();
            previous.Dispose();
        }

        public void Dispose()
        {
            if (disposed) return;
            ReleaseLastMenu();
            disposed = true;
            MoveRequested = null; ActorClicked = null; ActorMenuRequested = null; Feedback = null;
        }
    }
}
