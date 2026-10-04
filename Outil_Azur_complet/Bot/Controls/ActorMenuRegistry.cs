using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Outil_Azur_complet.Bot.Interfaces;
using Outil_Azur_complet.Bot.Panels;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.Monstres;
using Tool_BotProtocol.Game.NPC;
using Tool_BotProtocol.Game.Perso;

namespace Outil_Azur_complet.Bot.Controls
{
    /// <summary>
    /// Famille d'un acteur de la carte, alignée sur les types <c>GM</c> du client (joueur, PNJ, groupe de monstres…).
    /// Tant que le modèle d'acteurs de la couche protocole ne porte pas son propre type, <see cref="ActorClassifier"/> le
    /// déduit de la classe de l'entité ; les fournisseurs de menus ne dépendent que de cette énumération.
    /// </summary>
    public enum MenuActorKind { Unknown, Player, Npc, MonsterGroup, FightMonster, Merchant, Collector, Prism, ParkMount, FightSwords }

    /// <summary>Classement des entités de la carte pour les menus et les clics.</summary>
    public static class ActorClassifier
    {
        public static MenuActorKind Of(Entites actor)
        {
            if (actor is PNJ) return MenuActorKind.Npc;
            if (actor is Monstres) return MenuActorKind.MonsterGroup;
            if (actor is Personnages || actor is CharacterClass) return MenuActorKind.Player;
            return MenuActorKind.Unknown;
        }

        /// <summary>Vrai pour le personnage du compte (le client propose alors un autre menu).</summary>
        public static bool IsSelf(Entites actor, GameClass game) =>
            actor != null && (actor is CharacterClass || (game?.character != null && ReferenceEquals(actor, game.character)));

        /// <summary>Nom affiché en tête du menu, comme l'entrée statique du <c>PopupMenu</c> du client.</summary>
        public static string DisplayName(Entites actor)
        {
            if (actor == null) return string.Empty;
            return string.IsNullOrWhiteSpace(actor.Name) ? "Acteur n° " + actor.id : actor.Name;
        }
    }

    /// <summary>
    /// Entrée d'un menu contextuel d'acteur. <see cref="Action"/> renvoie le message à afficher dans le bandeau
    /// (ou <c>null</c>) ; <see cref="Icône"/> est une image partagée de <c>ClientAssets</c> que le menu ne libère jamais.
    /// </summary>
    public sealed class MenuEntry
    {
        public string Texte { get; set; }
        public Image Icône { get; set; }
        public bool Activé { get; set; } = true;
        public Func<ActorMenuContext, Task<string>> Action { get; set; }
        public IList<MenuEntry> SousMenu { get; set; }
        /// <summary>Raccourci affiché à droite de l'entrée (« Clic gauche », « Maj + clic »…).</summary>
        public string Raccourci { get; set; }
        public string Infobulle { get; set; }
        /// <summary>Action exécutée par un clic gauche sur l'acteur quand aucun lot n'a décidé autre chose.</summary>
        public bool ParDéfaut { get; set; }
        public bool EstSéparateur { get; private set; }

        public MenuEntry() { }
        public MenuEntry(string texte, Func<ActorMenuContext, Task<string>> action) { Texte = texte; Action = action; }

        public static MenuEntry Séparateur() => new MenuEntry { EstSéparateur = true, Activé = false };
        /// <summary>Ligne d'information non cliquable (par exemple « Partie pleine »).</summary>
        public static MenuEntry Statique(string texte) => new MenuEntry { Texte = texte, Activé = false };
    }

    /// <summary>Contexte transmis aux entrées : acteur visé, session, routeur de la carte, tiroir des volets, touches.</summary>
    public sealed class ActorMenuContext
    {
        public ActorMenuContext(Entites actor, IReadOnlyList<Entites> actorsOnCell, short cellId, Accounts account,
            InteractionRouter router, PanelHost panels, Keys modifiers)
        {
            Actor = actor; ActorsOnCell = actorsOnCell ?? new Entites[0]; CellId = cellId; Account = account;
            Router = router; Panels = panels; Modifiers = modifiers;
        }
        public Entites Actor { get; }
        public IReadOnlyList<Entites> ActorsOnCell { get; }
        public short CellId { get; }
        public Accounts Account { get; }
        public GameClass Game => Account?.Game;
        public InteractionRouter Router { get; }
        public PanelHost Panels { get; }
        public Keys Modifiers { get; }
        public bool Shift => (Modifiers & Keys.Shift) == Keys.Shift;
        public bool Ctrl => (Modifiers & Keys.Control) == Keys.Control;
        public MenuActorKind Kind => ActorClassifier.Of(Actor);
        public bool IsSelf => ActorClassifier.IsSelf(Actor, Game);
        /// <summary>Message court dans le bandeau de la fenêtre de jeu.</summary>
        public void Feedback(string message) => Router?.Report(message);
    }

    /// <summary>
    /// Fournisseur d'entrées pour le menu contextuel des acteurs de la carte. Chaque lot ajoute sa classe (par exemple
    /// <c>Bot/Menus/PlayerMenuProvider.cs</c>) : toute classe publique non abstraite de l'application qui implémente
    /// cette interface et possède un constructeur sans paramètre est enregistrée automatiquement dans
    /// <see cref="ActorMenuRegistry.Default"/>, triée par <see cref="ActorMenuOrderAttribute"/> puis par nom.
    /// </summary>
    public interface IActorMenuProvider
    {
        bool Handles(Entites a);
        IEnumerable<MenuEntry> Entries(Entites a, GameClass g);
    }

    /// <summary>
    /// Facultatif : un fournisseur qui implémente aussi cette interface décide du clic gauche sur ses acteurs
    /// (ouvrir le menu, se déplacer, attaquer…). Renvoyer <c>true</c> quand le clic est traité.
    /// </summary>
    public interface IActorClickHandler
    {
        Task<bool> OnActorClickAsync(ActorMenuContext context);
    }

    /// <summary>Position d'un fournisseur découvert automatiquement (100 par défaut ; plus petit = plus haut dans le menu).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ActorMenuOrderAttribute : Attribute
    {
        public int Order { get; }
        public ActorMenuOrderAttribute(int order) { Order = order; }
    }

    /// <summary>
    /// Registre des fournisseurs de menus d'acteurs. Les entrées des fournisseurs qui acceptent l'acteur sont fusionnées
    /// dans l'ordre d'enregistrement, un séparateur entre deux fournisseurs. Un fournisseur qui lève une exception est
    /// ignoré pour ce menu (et signalé), sans empêcher les autres.
    /// </summary>
    public sealed class ActorMenuRegistry
    {
        public const int DefaultOrder = 100;
        private static readonly Lazy<ActorMenuRegistry> defaultRegistry =
            new Lazy<ActorMenuRegistry>(() => Discover(typeof(ActorMenuRegistry).Assembly));
        private readonly List<IActorMenuProvider> providers = new List<IActorMenuProvider>();
        private readonly object sync = new object();

        /// <summary>Registre de l'application : fournisseurs découverts dans l'assembly au premier accès.</summary>
        public static ActorMenuRegistry Default => defaultRegistry.Value;
        public static void Register(IActorMenuProvider provider) => Default.Add(provider);
        public static bool Unregister(IActorMenuProvider provider) => Default.Remove(provider);

        public IReadOnlyList<IActorMenuProvider> Providers { get { lock (sync) return providers.ToArray(); } }

        public void Add(IActorMenuProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (sync) if (!providers.Contains(provider)) providers.Add(provider);
        }

        public bool Remove(IActorMenuProvider provider) { lock (sync) return providers.Remove(provider); }

        /// <summary>Entrées de tous les fournisseurs qui acceptent l'acteur, dans l'ordre d'enregistrement.</summary>
        public IReadOnlyList<MenuEntry> EntriesFor(Entites actor, GameClass game, Action<IActorMenuProvider, Exception> onError = null)
        {
            var merged = new List<MenuEntry>();
            if (actor == null) return merged;
            foreach (IActorMenuProvider provider in Providers)
            {
                List<MenuEntry> group;
                try
                {
                    if (!provider.Handles(actor)) continue;
                    group = (provider.Entries(actor, game) ?? Enumerable.Empty<MenuEntry>()).Where(entry => entry != null).ToList();
                }
                catch (Exception error) { onError?.Invoke(provider, error); continue; }
                while (group.Count > 0 && group[0].EstSéparateur) group.RemoveAt(0);
                if (group.Count == 0) continue;
                if (merged.Count > 0) merged.Add(MenuEntry.Séparateur());
                merged.AddRange(group);
            }
            while (merged.Count > 0 && merged[merged.Count - 1].EstSéparateur) merged.RemoveAt(merged.Count - 1);
            return merged;
        }

        /// <summary>Première entrée « par défaut » active (clic gauche sans menu), ou <c>null</c>.</summary>
        public MenuEntry DefaultEntryFor(Entites actor, GameClass game, Action<IActorMenuProvider, Exception> onError = null) =>
            EntriesFor(actor, game, onError).FirstOrDefault(entry => entry.ParDéfaut && entry.Activé && entry.Action != null);

        /// <summary>Fournisseurs qui acceptent l'acteur et décident eux-mêmes du clic gauche.</summary>
        public IReadOnlyList<IActorClickHandler> ClickHandlersFor(Entites actor, Action<IActorMenuProvider, Exception> onError = null)
        {
            var handlers = new List<IActorClickHandler>();
            if (actor == null) return handlers;
            foreach (IActorMenuProvider provider in Providers)
            {
                var handler = provider as IActorClickHandler; if (handler == null) continue;
                try { if (provider.Handles(actor)) handlers.Add(handler); }
                catch (Exception error) { onError?.Invoke(provider, error); }
            }
            return handlers;
        }

        /// <summary>Registre rempli des fournisseurs publics de l'assembly (constructeur sans paramètre).</summary>
        public static ActorMenuRegistry Discover(Assembly assembly)
        {
            var registry = new ActorMenuRegistry();
            if (assembly == null) return registry;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error) { types = error.Types.Where(type => type != null).ToArray(); }
            var found = types.Where(type => type.IsClass && !type.IsAbstract && type.IsPublic && typeof(IActorMenuProvider).IsAssignableFrom(type)
                    && type.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(type => type.GetCustomAttribute<ActorMenuOrderAttribute>()?.Order ?? DefaultOrder)
                .ThenBy(type => type.FullName, StringComparer.Ordinal);
            foreach (Type type in found)
            {
                try { registry.Add((IActorMenuProvider)Activator.CreateInstance(type)); }
                catch (TargetInvocationException) { }
                catch (MissingMethodException) { }
            }
            return registry;
        }
    }
}
