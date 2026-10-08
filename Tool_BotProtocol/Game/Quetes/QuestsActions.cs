using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Session;

namespace Tool_BotProtocol.Game.Quetes
{
    /// <summary>Message de progression <c>Im054</c> / <c>Im055</c> / <c>Im056</c> (<c>Quest.applyQuest</c>, <c>updateQuestData</c>).</summary>
    public enum QuestUpdateKind
    {
        /// <summary><c>Im054;&lt;id&gt;</c> : nouvelle quête (<c>INFOS_54</c>).</summary>
        Started = 54,
        /// <summary><c>Im055;&lt;id&gt;</c> : quête mise à jour, une étape validée (<c>INFOS_55</c>).</summary>
        Updated = 55,
        /// <summary><c>Im056;&lt;id&gt;</c> : quête terminée (<c>INFOS_56</c>).</summary>
        Finished = 56,
    }

    public sealed class QuestUpdate
    {
        public QuestUpdateKind Kind { get; }
        public int QuestId { get; }
        /// <summary>Texte du client (<c>INFOS_54/55/56</c> avec le nom de la quête) ou repli.</summary>
        public string Text { get; }
        internal QuestUpdate(QuestUpdateKind kind, int questId, string text) { Kind = kind; QuestId = questId; Text = text; }
    }

    /// <summary>
    /// Quêtes du personnage selon <c>dofus.aks.Quests</c> et la fenêtre <c>Quests</c> du client 1.34, lues comme StarLoco les envoie
    /// (<c>GameClient.parseQuestData</c>, <c>SocketManager.QuestList</c> / <c>QuestGep</c>). Envois : <c>QL</c> à l'ouverture de la fenêtre
    /// (<c>Quests.initData</c>) et <c>QS&lt;id&gt;</c> au choix d'une quête (<c>itemSelected</c>) ; le décalage d'étape du client
    /// (<c>QS&lt;id&gt;+n</c>) n'est jamais émis, StarLoco lisant l'identifiant par <c>Integer.parseInt</c>. Réceptions (via <c>QuestFrame</c>) :
    /// <c>QL</c> et <c>QS</c>. Les messages <c>Im054/055/056</c> (lot S1, <see cref="GameSession.ServerMessageReceived"/>) sont gardés comme
    /// dernière progression et marquent la liste « à actualiser », sans rien envoyer : le client ne redemande pas la liste.
    /// Les événements sont levés sur le fil de réception réseau : l'interface repasse sur son fil.
    /// </summary>
    public sealed class QuestsActions
    {
        private const string Reference = "QUÊTES";
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private QuestEntry[] quests = new QuestEntry[0];
        private readonly Dictionary<int, QuestStep> steps = new Dictionary<int, QuestStep>();
        private bool received, showFinished, needsRefresh;
        private int? selected;
        private QuestUpdate lastUpdate;
        private string lastMessage = string.Empty;
        private volatile bool windowOpen;
        private GameSession watchedSession;

        internal QuestsActions(Accounts.Accounts owner) { account = owner; }

        /// <summary>Dernière liste <c>QL</c> reçue, dans l'ordre du serveur.</summary>
        public IReadOnlyList<QuestEntry> Quests { get { lock (sync) return quests; } }
        /// <summary>Vrai dès qu'une liste <c>QL</c> a été reçue pendant la session.</summary>
        public bool HasReceived { get { lock (sync) return received; } }
        /// <summary>Quêtes en cours (<c>setPendingCount</c> : non terminées).</summary>
        public int PendingCount { get { lock (sync) return quests.Count(quest => !quest.IsFinished); } }
        /// <summary>Case « Afficher les quêtes terminées » (<c>_btnFinished</c>) ; décochée à l'ouverture (<see cref="OpenWindowAsync"/>) comme dans le client.</summary>
        public bool ShowFinished { get { lock (sync) return showFinished; } }
        /// <summary>
        /// Quêtes affichées : toutes, ou seulement celles en cours, triées par le troisième champ du client (<c>sortOn("sortOrder")</c>) ;
        /// sans ce champ (StarLoco), l'ordre du serveur est gardé.
        /// </summary>
        public IReadOnlyList<QuestEntry> VisibleQuests
        {
            get
            {
                lock (sync)
                {
                    IEnumerable<QuestEntry> list = showFinished ? quests : quests.Where(quest => !quest.IsFinished);
                    return list.Select((quest, index) => new { quest, index })
                        .OrderBy(pair => pair.quest.SortOrder ?? int.MaxValue).ThenBy(pair => pair.index)
                        .Select(pair => pair.quest).ToArray();
                }
            }
        }
        /// <summary>Dernière quête choisie (<c>Basics.quests_lastID</c>) : gardée d'une ouverture à l'autre, oubliée par <see cref="Clear"/>.</summary>
        public int? SelectedQuestId { get { lock (sync) return selected; } }
        /// <summary>Quête choisie, si elle figure dans la dernière liste.</summary>
        public QuestEntry SelectedQuest { get { lock (sync) return selected.HasValue ? quests.FirstOrDefault(quest => quest.Id == selected.Value) : null; } }
        /// <summary>Dernière étape reçue (<c>QS</c>) pour la quête, <c>null</c> sinon ; une nouvelle liste <c>QL</c> les oublie, comme le client.</summary>
        public QuestStep StepOf(int questId) { lock (sync) return steps.TryGetValue(questId, out QuestStep step) ? step : null; }
        /// <summary>Étape de la quête choisie, ou <c>null</c>.</summary>
        public QuestStep SelectedStep { get { lock (sync) return selected.HasValue && steps.TryGetValue(selected.Value, out QuestStep step) ? step : null; } }
        /// <summary>Dernier <c>Im054/055/056</c> reçu, ou <c>null</c>.</summary>
        public QuestUpdate LastUpdate { get { lock (sync) return lastUpdate; } }
        /// <summary>Un message de progression est arrivé depuis la dernière liste : elle peut être redemandée (<see cref="RefreshAsync"/>).</summary>
        public bool NeedsRefresh { get { lock (sync) return needsRefresh; } }
        public string LastMessage { get { lock (sync) return lastMessage; } }
        /// <summary>Fenêtre des quêtes ouverte dans l'interface : une liste reçue y resélectionne la dernière quête choisie.</summary>
        public bool WindowOpen { get => windowOpen; set => windowOpen = value; }

        /// <summary>Liste, étape, choix, case ou message changés (fil réseau ou appelant).</summary>
        public event Action Changed;
        /// <summary>Progression <c>Im054/055/056</c> reçue (fil réseau).</summary>
        public event Action<QuestUpdate> ProgressReceived;

        /// <summary>
        /// Abonne le service aux messages <c>Im</c> de la session (la session est créée après les interactions) ; sans effet après le
        /// premier appel réussi. Appelé par le volet à son association, par les envois et par la lecture des paquets.
        /// </summary>
        public void TrackServerMessages()
        {
            GameSession session = account?.Game?.Session;
            if (session == null) return;
            lock (sync)
            {
                if (ReferenceEquals(watchedSession, session)) return;
                if (watchedSession != null) watchedSession.ServerMessageReceived -= OnServerMessage;
                watchedSession = session;
            }
            session.ServerMessageReceived += OnServerMessage;
        }

        // ----- Envois (dofus.aks.Quests) -----

        /// <summary>
        /// Ouverture de la fenêtre (<c>Quests.initData</c>) : la case « Afficher les quêtes terminées » repart décochée, puis la liste
        /// est demandée (<c>QL</c>) ; à sa réception, la dernière quête choisie est redemandée si elle est visible.
        /// </summary>
        public Task<InteractionResult> OpenWindowAsync()
        {
            lock (sync) showFinished = false;
            windowOpen = true;
            RaiseChanged();
            return RefreshAsync();
        }

        /// <summary>Fermeture de la fenêtre : rien n'est envoyé, une liste reçue ensuite ne redemande plus d'étape.</summary>
        public void CloseWindow() => windowOpen = false;

        /// <summary><c>QL</c> (<c>getList</c>) : demandé à chaque ouverture de la fenêtre et par « Actualiser ».</summary>
        public Task<InteractionResult> RefreshAsync()
        {
            TrackServerMessages();
            return SendAsync("QL", "Liste des quêtes demandée.");
        }

        /// <summary>
        /// <c>QS&lt;id&gt;</c> (<c>getStep</c> sans décalage) : la quête devient la dernière choisie avant l'envoi, comme <c>itemSelected</c>.
        /// Refusé localement pour une quête absente de la dernière liste : StarLoco lève alors une exception sans répondre.
        /// </summary>
        public Task<InteractionResult> SelectAsync(int questId)
        {
            TrackServerMessages();
            lock (sync)
            {
                if (!received) return Refused("Liste des quêtes non reçue : ouvrez la fenêtre des quêtes une fois connecté.");
                if (!quests.Any(quest => quest.Id == questId)) return Refused("Quête n° " + Number(questId) + " absente de la liste du personnage.");
                selected = questId;
            }
            RaiseChanged();
            return SendAsync("QS" + Number(questId), "Étapes de « " + QuestTexts.QuestName(questId) + " » demandées.");
        }

        /// <summary>Bouton « fermer » de la vue des étapes (<c>_btnCloseStep</c>) : plus de quête choisie, rien n'est envoyé.</summary>
        public void Deselect()
        {
            lock (sync) selected = null;
            RaiseChanged();
        }

        /// <summary>
        /// Case « Afficher les quêtes terminées » : la liste est refiltrée (<c>modelChanged</c>) et, si la dernière quête choisie reste
        /// visible, ses étapes sont redemandées (<c>QS&lt;id&gt;</c>) comme dans le client.
        /// </summary>
        public Task<InteractionResult> SetShowFinishedAsync(bool show)
        {
            lock (sync) showFinished = show;
            RaiseChanged();
            return ReselectAsync();
        }

        /// <summary>Oublie la liste, les étapes, la dernière quête choisie et la progression (déconnexion, changement de personnage).</summary>
        public void Clear()
        {
            lock (sync)
            {
                quests = new QuestEntry[0]; steps.Clear();
                received = false; showFinished = false; needsRefresh = false;
                selected = null; lastUpdate = null; lastMessage = string.Empty;
            }
            windowOpen = false;
            RaiseChanged();
        }

        // ----- Réceptions (QuestFrame) -----

        /// <summary><c>QL+&lt;id;fini|…&gt;</c> : nouvelle liste ; les étapes reçues sont oubliées et la dernière quête choisie, si elle est visible
        /// et la fenêtre ouverte, est redemandée (<c>modelChanged</c>).</summary>
        internal Task OnListPacket(string message)
        {
            TrackServerMessages();
            string body = message != null && message.Length >= 2 ? message.Substring(2) : null;
            if (!QuestPackets.TryParseList(body, out List<QuestEntry> list, out string error))
            {
                LogError("Liste de quêtes illisible ignorée (" + Preview(message) + ") : " + error + ".");
                return Task.CompletedTask;
            }
            lock (sync)
            {
                quests = list.ToArray();
                steps.Clear();
                received = true; needsRefresh = false;
                lastMessage = QuestTexts.PendingCount(quests.Count(quest => !quest.IsFinished)) + ".";
            }
            account?.Logger?.LogInfo(Reference, list.Count.ToString(CultureInfo.InvariantCulture) + " quête(s) reçue(s).");
            RaiseChanged();
            return windowOpen ? ReselectAsync() : Task.CompletedTask;
        }

        /// <summary><c>QS&lt;quête&gt;|…</c> : étape courante, objectifs et hiérarchie. Une quête absente de la liste est ignorée (<c>onStep</c>).</summary>
        internal void OnStepPacket(string message)
        {
            TrackServerMessages();
            string body = message != null && message.Length >= 2 ? message.Substring(2) : null;
            if (!QuestPackets.TryParseStep(body, out QuestStep step, out string error))
            {
                LogError("Étape de quête illisible ignorée (" + Preview(message) + ") : " + error + ".");
                return;
            }
            lock (sync)
            {
                if (!quests.Any(quest => quest.Id == step.QuestId))
                {
                    lastMessage = "Étapes reçues pour une quête absente de la liste (n° " + Number(step.QuestId) + ") : ignorées.";
                }
                else
                {
                    steps[step.QuestId] = step;
                    lastMessage = step.HasCurrentStep ? "Étape « " + step.Name + " »." : "« " + QuestTexts.QuestName(step.QuestId) + " » : aucune étape en cours.";
                }
            }
            RaiseChanged();
        }

        private void OnServerMessage(ServerMessage message)
        {
            try
            {
                if (message == null || message.Kind != ServerMessageKind.Info || !message.NumericId.HasValue) return;
                int id = message.NumericId.Value;
                if (id != 54 && id != 55 && id != 56) return;
                if (message.Args.Count == 0 || !int.TryParse(message.Args[0], NumberStyles.None, CultureInfo.InvariantCulture, out int questId) || questId <= 0) return;
                var kind = (QuestUpdateKind)id;
                string fallback = kind == QuestUpdateKind.Started ? "Nouvelle quête : {0}" : kind == QuestUpdateKind.Updated ? "Quête mise à jour : {0}" : "Quête terminée : {0}";
                var update = new QuestUpdate(kind, questId, QuestTexts.Get(message.LangKey, fallback, QuestTexts.QuestName(questId)));
                lock (sync)
                {
                    lastUpdate = update;
                    needsRefresh = received;
                    lastMessage = update.Text;
                }
                Raise(ProgressReceived, handler => handler(update));
                RaiseChanged();
            }
            catch (Exception error)
            {
                try { account?.Logger?.LogError(Reference, "Message de progression illisible ignoré : " + error.Message); }
                catch (Exception) { /* Journal fermé. */ }
            }
        }

        // ----- Outils -----

        /// <summary><c>modelChanged</c> : redemande la dernière quête choisie si elle est visible ; sinon rien.</summary>
        private Task<InteractionResult> ReselectAsync()
        {
            int? last = SelectedQuestId;
            if (last == null || !VisibleQuests.Any(quest => quest.Id == last.Value)) return Refused("Aucune quête choisie visible.");
            return SendAsync("QS" + Number(last.Value), "Étapes de « " + QuestTexts.QuestName(last.Value) + " » demandées.");
        }

        private async Task<InteractionResult> SendAsync(string packet, string message)
        {
            var connection = account?.Connexion;
            if (connection == null || !connection.IsConnected()) return Refuse("Connectez le personnage avant cette action.");
            try { await connection.SendPacket(packet).ConfigureAwait(false); }
            catch (Exception error)
            {
                account?.Logger?.LogException(Reference, error);
                return Refuse("Envoi impossible : " + error.Message);
            }
            lock (sync) lastMessage = message;
            account?.Logger?.LogInfo(Reference, message);
            RaiseChanged();
            return new InteractionResult(true, message);
        }

        private static InteractionResult Refuse(string message) => new InteractionResult(false, message);
        private static Task<InteractionResult> Refused(string message) => Task.FromResult(Refuse(message));

        private void LogError(string message)
        {
            lock (sync) lastMessage = message;
            try { account?.Logger?.LogError(Reference, message); }
            catch (Exception) { /* Journal fermé : la lecture des paquets continue. */ }
            RaiseChanged();
        }

        private void RaiseChanged() => Raise(Changed, handler => handler());

        private void Raise<T>(T subscribers, Action<T> invoke) where T : class
        {
            var multicast = subscribers as Delegate;
            if (multicast == null) return;
            foreach (Delegate subscriber in multicast.GetInvocationList())
            {
                try { invoke((T)(object)subscriber); }
                catch (Exception error)
                {
                    try { account?.Logger?.LogError(Reference, "Un abonné des quêtes a échoué : " + error.Message); }
                    catch (Exception) { /* Journal fermé. */ }
                }
            }
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Preview(string message) => message == null ? string.Empty : message.Length > 80 ? message.Substring(0, 80) + "…" : message;
    }
}
