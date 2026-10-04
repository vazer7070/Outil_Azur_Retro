using System;
using System.Globalization;
using Tool_BotProtocol.Game.Interactions;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>
    /// Fenêtre d'échange ouverte par le serveur avec <c>ECK&lt;type&gt;[|&lt;données&gt;]</c> (classe <c>dofus.aks.Exchange</c> du
    /// client 1.34). Le registre (<see cref="ExchangeRegistry"/>) lui transmet les paquets de la famille <c>E</c> tant qu'elle
    /// est l'échange en cours : <c>EL</c>, <c>EMK</c>, <c>EmK</c>, <c>EsK</c>, <c>EK</c>, <c>Ew</c> puis <c>EV</c>.
    /// Chaque méthode est appelée sur le fil de réception réseau et ne doit jamais lever d'exception.
    /// </summary>
    public interface IExchange
    {
        /// <summary>Type <c>ECK</c> de l'échange ouvert, -1 lorsqu'il est fermé.</summary>
        int ExchangeType { get; }
        bool IsOpen { get; }
        /// <summary><c>ECK&lt;type&gt;|&lt;données&gt;</c> : l'échange est créé (données sans le séparateur <c>|</c>, éventuellement vides).</summary>
        void OnCreated(int type, string data);
        /// <summary><c>EL&lt;liste&gt;</c> : contenu initial (articles, coffre, lots…).</summary>
        void OnList(string payload);
        /// <summary><c>EMK&lt;O|G&gt;…</c> : mouvement de son propre côté (<c>Exchange.onLocalMovement</c>).</summary>
        void OnLocalMovement(string data);
        /// <summary><c>EmK&lt;O|G&gt;…</c> : mouvement de l'autre côté (<c>Exchange.onDistantMovement</c>).</summary>
        void OnDistantMovement(string data);
        /// <summary><c>EsK&lt;O|G&gt;…</c> : mouvement du stockage (<c>Exchange.onStorageMovement</c>).</summary>
        void OnStorageMovement(string data);
        /// <summary><c>EK&lt;0|1&gt;&lt;acteur&gt;</c> : état « prêt » d'un participant (<c>Exchange.onReady</c>).</summary>
        void OnReady(string data);
        /// <summary><c>Ew&lt;pods&gt;;&lt;max&gt;</c> : charge de la monture (<c>Exchange.onMountPods</c>).</summary>
        void OnPods(string data);
        /// <summary><c>EV[a]</c> : fin de l'échange, « a » quand il a été validé (<c>Exchange.onLeave</c>).</summary>
        void OnLeave(string suffix);
        /// <summary>Oublie l'échange sans rien envoyer (déconnexion, changement de personnage).</summary>
        void Clear();
    }

    /// <summary>
    /// Déclare la classe qui gère un type <c>ECK</c>. Toute classe non abstraite de <c>Tool_BotProtocol</c> qui implémente
    /// <see cref="IExchange"/>, porte cet attribut et possède un constructeur <c>(Accounts)</c> est enregistrée automatiquement
    /// par <see cref="ExchangeRegistry"/> ; une même classe peut gérer plusieurs types (une instance par compte).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ExchangeTypeAttribute : Attribute
    {
        public ExchangeTypeAttribute(int type) { Type = type; }
        public int Type { get; }
    }

    /// <summary>Objet d'une colonne d'échange ou d'un coffre.</summary>
    public sealed class ExchangeItem
    {
        /// <summary>Identifiant de l'exemplaire (celui de l'inventaire pour ses propres objets).</summary>
        public uint Id { get; internal set; }
        /// <summary>Modèle de l'objet, 0 s'il n'a pas été transmis et que l'objet n'est pas dans le sac.</summary>
        public int TemplateId { get; internal set; }
        public int Quantity { get; internal set; }
        /// <summary>Effets bruts transmis par le serveur (format des fiches d'objets).</summary>
        public string Effects { get; internal set; } = string.Empty;
        /// <summary>Nom depuis <c>BotObjets</c>, sinon « Objet n° X ».</summary>
        public string Name { get; internal set; } = string.Empty;

        internal ExchangeItem Copy() => (ExchangeItem)MemberwiseClone();
    }

    /// <summary>
    /// Base des fenêtres d'échange : chaque paquet est protégé (un paquet mal formé est journalisé, jamais propagé) puis
    /// confié à la méthode <c>Handle…</c> correspondante. Les méthodes non redéfinies journalisent le paquet inattendu.
    /// </summary>
    public abstract class ExchangeWindow : InteractionWindow, IExchange
    {
        protected ExchangeWindow(Accounts.Accounts account) : base(account) { }

        public int ExchangeType { get; private set; } = -1;

        void IExchange.OnCreated(int type, string data) => Guard("ECK", data, () =>
        {
            Reset();
            ExchangeType = type;
            HandleCreated(type, data ?? string.Empty);
        });

        /// <summary>Remet la fenêtre à zéro, type compris ; les classes dérivées vident leur propre état dans <see cref="ResetState"/>.</summary>
        protected sealed override void Reset()
        {
            ExchangeType = -1;
            ResetState();
        }
        protected abstract void ResetState();

        /// <summary>Oublie la fenêtre sans envoyer de paquet (déconnexion) ; <see cref="OnClearing"/> vide d'abord l'état propre à la fenêtre.</summary>
        public new void Clear()
        {
            OnClearing();
            base.Clear();
        }
        /// <summary>État à oublier en plus de <see cref="ResetState"/> lors d'un <see cref="Clear"/> (demande en attente…).</summary>
        protected virtual void OnClearing() { }
        void IExchange.OnList(string payload) => Guard("EL", payload, () => HandleList(payload ?? string.Empty));
        void IExchange.OnLocalMovement(string data) => Guard("EMK", data, () => HandleLocalMovement(data ?? string.Empty));
        void IExchange.OnDistantMovement(string data) => Guard("EmK", data, () => HandleDistantMovement(data ?? string.Empty));
        void IExchange.OnStorageMovement(string data) => Guard("EsK", data, () => HandleStorageMovement(data ?? string.Empty));
        void IExchange.OnReady(string data) => Guard("EK", data, () => HandleReady(data ?? string.Empty));
        void IExchange.OnPods(string data) => Guard("Ew", data, () => HandlePods(data ?? string.Empty));
        void IExchange.OnLeave(string suffix) => Guard("EV", suffix, () =>
        {
            HandleLeave(suffix ?? string.Empty);
            if (!IsOpen) ExchangeType = -1;
        });

        protected abstract void HandleCreated(int type, string data);
        protected virtual void HandleList(string payload) => Unexpected("EL", payload);
        protected virtual void HandleLocalMovement(string data) => Unexpected("EMK", data);
        protected virtual void HandleDistantMovement(string data) => Unexpected("EmK", data);
        protected virtual void HandleStorageMovement(string data) => Unexpected("EsK", data);
        protected virtual void HandleReady(string data) => Unexpected("EK", data);
        protected virtual void HandlePods(string data) => Unexpected("Ew", data);
        /// <summary>Par défaut : ferme la fenêtre, comme <c>Exchange.onLeave</c> qui décharge toutes les interfaces d'échange.</summary>
        protected virtual void HandleLeave(string suffix)
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log(suffix == "a" ? ExchangeRegistry.Text("EXCHANGE_OK", "Échange terminé.") : ExchangeRegistry.Text("EXCHANGE_CANCEL", "Échange annulé."));
            Notify();
        }

        /// <summary>Paquet de la famille <c>E</c> sans signification pour cette fenêtre : journalisé, ignoré.</summary>
        protected void Unexpected(string prefix, string data) =>
            Account?.Logger?.LogDebug(Reference, "Paquet " + prefix + (data ?? string.Empty) + " ignoré pour cet échange (type " + ExchangeType + ").");

        /// <summary>Paquet illisible : journalisé comme erreur, l'état reste celui d'avant le paquet.</summary>
        protected void Malformed(string prefix, string data)
        {
            Account?.Logger?.LogError(Reference, "Paquet " + prefix + " illisible ignoré : " + Shorten(data));
        }

        protected static bool TryInt(string value, out int result) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        protected static bool TryLong(string value, out long result) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        /// <summary>Identifiant d'exemplaire décimal (<c>EMK</c>, <c>EmK</c>, <c>EsK</c> : StarLoco écrit l'entier Java tel quel).</summary>
        protected static bool TryId(string value, out uint result) =>
            uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

        private void Guard(string prefix, string data, Action action)
        {
            try { action(); }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Account?.Logger?.LogError(Reference, "Paquet " + prefix + " non appliqué (" + error.GetType().Name + ") : " + Shorten(data));
            }
        }

        private static string Shorten(string data)
        {
            data = data ?? string.Empty;
            return data.Length > 120 ? data.Substring(0, 120) + "…" : data;
        }
    }
}
