using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Data;
using Tool_BotProtocol.Game.Maps;
using Tool_BotProtocol.Game.Maps.Entities;
using Tool_BotProtocol.Game.NPC;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>Réponse proposée par <c>DQ</c> : texte du client (<c>D.a</c>) ou, sans ce texte, « Réponse n° X ».</summary>
    public sealed class DialogAnswer
    {
        internal DialogAnswer(int id, string text, bool hasClientText) { Id = id; Text = text; HasClientText = hasClientText; }
        public int Id { get; }
        public string Text { get; }
        /// <summary>Vrai quand le texte vient des fichiers de langue du client (<c>dialog.xml</c>).</summary>
        public bool HasClientText { get; }
    }

    /// <summary>
    /// Dialogue avec un personnage non joueur, selon <c>dofus.aks.Dialog</c> du client 1.34 :
    /// envoi <c>DC&lt;pnj&gt;</c>, réception <c>DCK&lt;pnj&gt;</c>/<c>DCE</c>, <c>DQ&lt;question&gt;[;&lt;paramètres&gt;]|&lt;réponses&gt;</c>,
    /// <c>DP</c>, <c>DV</c> ; envoi <c>DR&lt;question&gt;|&lt;réponse&gt;</c> et <c>DV</c>. StarLoco n'envoie que les numéros :
    /// les textes viennent des fichiers de langue du client (<see cref="LangData.Dialog"/>), lus sur le fil réseau
    /// à la réception de <c>DQ</c> ; sans ces fichiers, « Question n° X » et « Réponse n° Y ».
    /// </summary>
    public sealed class NpcDialog : InteractionWindow
    {
        /// <summary>Bit de <c>AR</c> que le client lit comme <c>Player.cantSpeakNPC</c> : le clic sur un PNJ ne fait alors rien.</summary>
        public const int CantSpeakNpcRestriction = 512;

        public int NpcId { get; private set; } = -1;
        public string NpcName { get; private set; } = string.Empty;
        /// <summary>Modèle du PNJ (<c>npc_template</c>) lu dans son entrée <c>GM</c> ; -1 s'il est inconnu (percepteur, PNJ absent de la carte).</summary>
        public int NpcTemplateId { get; private set; } = -1;
        /// <summary>
        /// Illustration du dialogue, comme <c>NpcDialog.setNpcCharacteristics</c> du client : <c>customArtwork</c> du PNJ
        /// s'il est positif, sinon son gfx (<c>clips/artworks/big/&lt;n&gt;.swf</c>) ; 0 si aucune n'est connue.
        /// </summary>
        public int PortraitId { get; private set; }
        /// <summary>Vrai quand <c>DCK</c> désigne un percepteur de la carte (question <c>Guild.parseQuestionTaxCollector</c>).</summary>
        public bool IsCollector { get; private set; }
        public int QuestionId { get; private set; } = -1;
        public string[] Parameters { get; private set; } = new string[0];
        public int[] AnswerIds { get; private set; } = new int[0];
        /// <summary>Texte de la question avec ses paramètres substitués (<c>#1</c>…), ou « Question n° X ».</summary>
        public string QuestionText { get; private set; } = string.Empty;
        /// <summary>Vrai quand <see cref="QuestionText"/> vient des fichiers de langue du client.</summary>
        public bool HasQuestionText { get; private set; }
        public IReadOnlyList<DialogAnswer> Answers { get; private set; } = new DialogAnswer[0];
        /// <summary>Vrai après <c>DP</c> : le serveur demande d'attendre avant la question suivante.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// Texte du seul choix proposé par le client lorsqu'une question n'a pas de réponse (<c>QuestionViewer</c> :
        /// clé <c>CONTINUE_TO_SPEAK</c>, réponse -1) ; ce choix envoie <c>DV</c>.
        /// </summary>
        public static string EndChoiceText => LangData.Text.Has("CONTINUE_TO_SPEAK") ? LangData.Text.Get("CONTINUE_TO_SPEAK") : "Quitter le dialogue";

        protected override string Reference => "DIALOGUE";
        protected override AccountStates OpenState => AccountStates.DIALOG;

        internal NpcDialog(Accounts.Accounts account) : base(account) { }

        /// <summary>Vrai si le dernier <c>AR</c> porte le bit <see cref="CantSpeakNpcRestriction"/> (jamais envoyé par StarLoco).</summary>
        public bool CannotSpeakToNpc
        {
            get
            {
                int raw = Account?.Game?.Session?.RawRestrictions ?? -1;
                return raw >= 0 && (raw & CantSpeakNpcRestriction) == CantSpeakNpcRestriction;
            }
        }

        /// <summary>Envoie <c>DC&lt;pnj&gt;</c>, comme le clic sur un PNJ dans le client.</summary>
        public Task<InteractionResult> OpenAsync(int npcId)
        {
            if (CannotSpeakToNpc) return Task.FromResult(Refuse("Votre personnage ne peut pas parler aux PNJ pour le moment."));
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            return SendAsync("DC" + npcId, "Dialogue demandé avec " + ResolveName(npcId) + ".");
        }

        /// <summary>
        /// Action du menu d'un PNJ (<c>N.a</c>), comme <c>Npc.getActionFunction</c> du client : 3 → <c>DC&lt;pnj&gt;</c>,
        /// 1 → <c>ER0|&lt;pnj&gt;</c> (boutique), 2 → <c>ER2</c>, 4 → <c>ER9</c>, 5 → <c>ER10</c>, 6 → <c>ER11</c>, 7 → <c>ER17</c>.
        /// L'action 8 (<c>ER18</c>, échange de monture) est refusée : StarLoco n'a pas de branche pour ce type.
        /// </summary>
        public Task<InteractionResult> RequestActionAsync(int action, int npcId)
        {
            if (CannotSpeakToNpc) return Task.FromResult(Refuse("Votre personnage ne peut pas interagir avec les PNJ pour le moment."));
            if (action == NpcActions.Talk) return OpenAsync(npcId);
            if (action == NpcActions.BuySell)
            {
                NpcShop shop = Account?.Game?.Interactions?.Shop;
                return shop == null ? Task.FromResult(Refuse("La session n'est plus disponible.")) : shop.OpenAsync(npcId);
            }
            if (!NpcActions.ServerHandles(action) || !(NpcActions.ExchangeType(action) is int type))
                return Task.FromResult(Refuse("L'action « " + NpcActions.Label(action) + " » n'est pas prise en charge par le serveur."));
            InteractionResult refused = CheckCanOpen();
            if (refused != null) return Task.FromResult(refused);
            string packet = "ER" + type.ToString(CultureInfo.InvariantCulture) + "|" + npcId.ToString(CultureInfo.InvariantCulture);
            return SendAsync(packet, "« " + NpcActions.Label(action) + " » demandé à " + ResolveName(npcId) + " (" + packet + ").");
        }

        /// <summary>Envoie <c>DR&lt;question&gt;|&lt;réponse&gt;</c> pour une réponse proposée par le serveur.</summary>
        public Task<InteractionResult> AnswerAsync(int answerId)
        {
            if (!IsOpen || QuestionId < 0) return Task.FromResult(Refuse("Aucune question en cours."));
            if (IsPaused) return Task.FromResult(Refuse("Le serveur demande d'attendre la question suivante."));
            if (!AnswerIds.Contains(answerId)) return Task.FromResult(Refuse("La réponse n° " + answerId + " n'est pas proposée."));
            return SendAsync("DR" + QuestionId + "|" + answerId, "Réponse envoyée : " + AnswerText(answerId));
        }

        /// <summary>Envoie <c>DV</c> ; le serveur confirme la fermeture par <c>DV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun dialogue ouvert."));
            return SendAsync("DV", "Fin du dialogue demandée.");
        }

        /// <summary>Texte d'une réponse proposée, ou « Réponse n° X ».</summary>
        public string AnswerText(int answerId)
        {
            DialogAnswer answer = Answers.FirstOrDefault(entry => entry.Id == answerId);
            return answer != null ? answer.Text : "Réponse n° " + answerId;
        }

        private string ResolveName(int npcId)
        {
            MapActor actor = Account?.Game?.Map?.GetActor(npcId);
            if (actor is NpcActor npc && LangData.Npc.Has(npc.TemplateId)) return LangData.Text.Fetch(LangData.Npc.Name(npc.TemplateId));
            if (actor is CollectorActor collector) return collector.DisplayName;
            if (actor != null && !string.IsNullOrEmpty(actor.Name)) return actor.Name;
            return "PNJ " + npcId;
        }

        /// <summary><c>DCK&lt;pnj&gt;</c> : dialogue créé (<c>Dialog.onCreate</c> lit le sprite pour le nom et l'illustration).</summary>
        internal void OnCreated(string payload)
        {
            Reset();
            // Les identifiants de PNJ sur la carte sont négatifs chez StarLoco : seul l'échec de lecture rend le PNJ inconnu.
            bool known = int.TryParse(payload, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int npcId);
            NpcId = known ? npcId : -1;
            NpcName = known ? ResolveName(NpcId) : "PNJ inconnu";
            MapActor actor = known ? Account?.Game?.Map?.GetActor(NpcId) : null;
            if (actor is NpcActor npc)
            {
                NpcTemplateId = npc.TemplateId;
                PortraitId = npc.Artwork > 0 ? npc.Artwork : Math.Max(0, npc.Gfx);
            }
            else if (actor is CollectorActor collector)
            {
                IsCollector = true;
                PortraitId = Math.Max(0, collector.Gfx);
            }
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

        /// <summary>
        /// <c>DQ&lt;question&gt;[;&lt;p1,p2&gt;]|&lt;réponse&gt;;&lt;réponse&gt;</c>, lu comme <c>Dialog.onQuestion</c> : la question est
        /// <c>getDescription(D.q[id], paramètres)</c> puis <c>fetchString</c>, chaque réponse <c>fetchString(D.a[id])</c>.
        /// </summary>
        internal void OnQuestion(string payload)
        {
            string[] parts = (payload ?? string.Empty).Split('|');
            string[] head = parts[0].Split(';');
            if (!int.TryParse(head[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int questionId))
            {
                LogError("Question de dialogue illisible : " + payload);
                Notify();
                return;
            }
            string[] parameters = head.Length > 1 && head[1].Length > 0 ? head[1].Split(',') : new string[0];
            int[] answerIds = parts.Length > 1
                ? parts[1].Split(';').Select(value => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int id) ? id : -1)
                    .Where(id => id >= 0).ToArray()
                : new int[0];
            bool hasText = LangData.Dialog.HasQuestion(questionId);
            string text;
            try { text = hasText ? LangData.Dialog.Question(questionId, parameters) : null; }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Account?.Logger?.LogException(Reference, error);
                text = null;
            }
            if (text == null) hasText = false;
            QuestionId = questionId;
            Parameters = parameters;
            AnswerIds = answerIds;
            QuestionText = hasText ? text : "Question n° " + questionId.ToString(CultureInfo.InvariantCulture);
            HasQuestionText = hasText;
            Answers = answerIds.Select(ReadAnswer).ToArray();
            IsPaused = false;
            // StarLoco envoie DCK avant DQ ; un DQ isolé (percepteur, action de quête) ouvre aussi la fenêtre.
            if (!IsOpen) MarkOpen();
            Log((hasText ? "Question n° " + questionId + " affichée" : "Question n° " + questionId + " (texte du client absent)")
                + " : " + answerIds.Length + " réponse(s) proposée(s).");
            Notify();
        }

        private DialogAnswer ReadAnswer(int id)
        {
            if (LangData.Dialog.HasAnswer(id))
            {
                string text = LangData.Dialog.Answer(id);
                if (!string.IsNullOrEmpty(text)) return new DialogAnswer(id, text, true);
            }
            return new DialogAnswer(id, "Réponse n° " + id.ToString(CultureInfo.InvariantCulture), false);
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
            NpcTemplateId = -1;
            PortraitId = 0;
            IsCollector = false;
            QuestionId = -1;
            Parameters = new string[0];
            AnswerIds = new int[0];
            QuestionText = string.Empty;
            HasQuestionText = false;
            Answers = new DialogAnswer[0];
            IsPaused = false;
        }
    }

    /// <summary>
    /// Actions du menu d'un PNJ déclarées par les textes du client (<c>npc_fr</c> : <c>N.d[modèle].a</c>, noms <c>N.a</c>)
    /// et leur traduction en paquets (<c>Npc.getActionFunction</c> du client 1.34).
    /// </summary>
    public static class NpcActions
    {
        public const int BuySell = 1, Exchange = 2, Talk = 3, Pets = 4, AuctionSell = 5, AuctionBuy = 6, PetResurrection = 7, MountExchange = 8;

        /// <summary>Menu de repli quand les textes du client ne connaissent pas le PNJ : Parler et Acheter/Vendre.</summary>
        public static int[] Fallback => new[] { Talk, BuySell };

        /// <summary>Type d'échange envoyé dans <c>ER&lt;type&gt;|&lt;pnj&gt;</c> ; <c>null</c> pour Parler et les actions inconnues.</summary>
        public static int? ExchangeType(int action)
        {
            switch (action)
            {
                case BuySell: return 0;
                case Exchange: return 2;
                case Pets: return 9;
                case AuctionSell: return 10;
                case AuctionBuy: return 11;
                case PetResurrection: return 17;
                case MountExchange: return 18;
                default: return null;
            }
        }

        /// <summary>
        /// Vrai si le serveur traite l'action : StarLoco ouvre les types 0, 2, 9, 10, 11 et 17 (<c>GameClient.request</c>)
        /// mais n'a aucune branche pour <c>ER18</c> ; le menu n'affiche donc pas l'action 8.
        /// </summary>
        public static bool ServerHandles(int action) => action == Talk || (action != MountExchange && ExchangeType(action) != null);

        /// <summary>
        /// Actions du modèle, dans l'ordre du client (le menu reprend l'ordre de <c>N.d[modèle].a</c>) ;
        /// <see cref="Fallback"/> quand <c>npc.xml</c> n'est pas chargé ou ne connaît pas ce modèle.
        /// </summary>
        public static int[] ForTemplate(int templateId, out bool fromClientTexts)
        {
            fromClientTexts = templateId >= 0 && LangData.IsLoaded("npc") && LangData.Npc.Has(templateId);
            return fromClientTexts ? LangData.Npc.Actions(templateId) : Fallback;
        }

        /// <summary>Nom de l'action dans les textes du client (<c>N.a</c>), sinon un libellé descriptif.</summary>
        public static string Label(int action)
        {
            string key = action.ToString(CultureInfo.InvariantCulture);
            string name = LangData.Npc.ActionName(action);
            if (!string.IsNullOrEmpty(name) && name != key) return name;
            switch (action)
            {
                case BuySell: return "Acheter/Vendre";
                case Exchange: return "Échanger";
                case Talk: return "Parler";
                case Pets: return "Garde de familier";
                case AuctionSell: return "Hôtel de vente : vendre";
                case AuctionBuy: return "Hôtel de vente : acheter";
                case PetResurrection: return "Résurrection de familier";
                case MountExchange: return "Échange de monture";
                default: return "Action n° " + key;
            }
        }
    }
}
