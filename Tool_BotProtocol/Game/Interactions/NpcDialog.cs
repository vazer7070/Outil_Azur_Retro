using System;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Maps.Interfaces;
using Tool_BotProtocol.Game.NPC;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>
    /// Dialogue avec un personnage non joueur, selon <c>dofus.aks.Dialog</c> du client 1.34 :
    /// envoi <c>DC&lt;pnj&gt;</c>, réception <c>DCK&lt;pnj&gt;</c>/<c>DCE</c>, <c>DQ&lt;question&gt;[;&lt;paramètres&gt;]|&lt;réponses&gt;</c>,
    /// <c>DP</c>, <c>DV</c> ; envoi <c>DR&lt;question&gt;|&lt;réponse&gt;</c> et <c>DV</c>.
    /// Les textes ne sont pas exportés dans BotNPCs : seuls les numéros de question et de réponse sont connus.
    /// </summary>
    public sealed class NpcDialog : InteractionWindow
    {
        public int NpcId { get; private set; } = -1;
        public string NpcName { get; private set; } = string.Empty;
        public int QuestionId { get; private set; } = -1;
        public string[] Parameters { get; private set; } = new string[0];
        public int[] AnswerIds { get; private set; } = new int[0];
        /// <summary>Vrai après <c>DP</c> : le serveur demande d'attendre avant la question suivante.</summary>
        public bool IsPaused { get; private set; }

        protected override string Reference => "DIALOGUE";
        protected override AccountStates OpenState => AccountStates.DIALOG;

        internal NpcDialog(Accounts.Accounts account) : base(account) { }

        /// <summary>Envoie <c>DC&lt;pnj&gt;</c>, comme le clic sur un PNJ dans le client.</summary>
        public Task<InteractionResult> OpenAsync(int npcId)
        {
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("DC" + npcId, "Dialogue demandé avec " + ResolveName(npcId) + ".");
        }

        /// <summary>Envoie <c>DR&lt;question&gt;|&lt;réponse&gt;</c> pour une réponse proposée par le serveur.</summary>
        public Task<InteractionResult> AnswerAsync(int answerId)
        {
            if (!IsOpen || QuestionId < 0) return Task.FromResult(Refuse("Aucune question en cours."));
            if (!AnswerIds.Contains(answerId)) return Task.FromResult(Refuse("La réponse n° " + answerId + " n'est pas proposée."));
            return SendAsync("DR" + QuestionId + "|" + answerId, "Réponse n° " + answerId + " envoyée ; le serveur poursuit le dialogue.");
        }

        /// <summary>Envoie <c>DV</c> ; le serveur confirme la fermeture par <c>DV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun dialogue ouvert."));
            return SendAsync("DV", "Fin du dialogue demandée.");
        }

        private string ResolveName(int npcId)
        {
            var map = Account?.Game?.Map;
            if (map != null && map.Entites.TryGetValue(npcId, out Entites entity) && entity is PNJ npc && !string.IsNullOrEmpty(npc.Name))
                return npc.Name;
            return "PNJ " + npcId;
        }

        /// <summary><c>DCK&lt;pnj&gt;</c> : dialogue créé.</summary>
        internal void OnCreated(string payload)
        {
            Reset();
            // Les identifiants de PNJ sur la carte sont négatifs chez StarLoco : seul l'échec de lecture rend le PNJ inconnu.
            bool known = int.TryParse(payload, out int npcId);
            NpcId = known ? npcId : -1;
            NpcName = known ? ResolveName(NpcId) : "PNJ inconnu";
            MarkOpen();
            Log("Dialogue ouvert avec " + NpcName + ".");
            Notify();
        }

        /// <summary><c>DCE</c> : dialogue refusé.</summary>
        internal void OnCreateError()
        {
            LogError("Le serveur a refusé le dialogue avec ce personnage.");
            Notify();
        }

        /// <summary><c>DQ&lt;question&gt;[;&lt;p1,p2&gt;]|&lt;réponse&gt;;&lt;réponse&gt;</c>, lu comme <c>Dialog.onQuestion</c>.</summary>
        internal void OnQuestion(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            string[] head = parts[0].Split(';');
            if (!int.TryParse(head[0], out int questionId))
            {
                LogError("Question de dialogue illisible : " + payload);
                Notify();
                return;
            }
            QuestionId = questionId;
            Parameters = head.Length > 1 && head[1].Length > 0 ? head[1].Split(',') : new string[0];
            AnswerIds = parts.Length > 1
                ? parts[1].Split(';').Select(value => int.TryParse(value, out int id) ? id : -1).Where(id => id >= 0).ToArray()
                : new int[0];
            IsPaused = false;
            // StarLoco envoie DCK avant DQ ; un DQ isolé (percepteur, action de quête) ouvre aussi la fenêtre.
            if (!IsOpen) MarkOpen();
            Log("Question n° " + questionId + " : " + AnswerIds.Length + " réponse(s) proposée(s).");
            Notify();
        }

        /// <summary><c>DP</c> : pause demandée par le serveur.</summary>
        internal void OnPause()
        {
            if (!IsOpen) return;
            IsPaused = true;
            Log("Le serveur met le dialogue en pause.");
            Notify();
        }

        /// <summary><c>DV</c> : fin du dialogue.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Dialogue terminé.");
            Notify();
        }

        protected override void Reset()
        {
            NpcId = -1;
            NpcName = string.Empty;
            QuestionId = -1;
            Parameters = new string[0];
            AnswerIds = new int[0];
            IsPaused = false;
        }
    }
}
