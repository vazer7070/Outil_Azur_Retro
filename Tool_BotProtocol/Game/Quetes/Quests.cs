using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Game.Quetes
{
    /// <summary>
    /// Quête de la liste <c>QL</c> (<c>dofus.datacenter.Quest</c> du client 1.34) : identifiant, terminée ou non, et l'ordre de
    /// tri que le client lit en troisième champ (StarLoco ne l'envoie pas : <c>Player.getQuestGmPacket</c>).
    /// </summary>
    public sealed class QuestEntry
    {
        public int Id { get; }
        public bool IsFinished { get; }
        /// <summary>Troisième champ <c>id;fini;ordre</c> du client, <c>null</c> chez StarLoco.</summary>
        public int? SortOrder { get; }
        /// <summary>Nom de la quête (<c>Q.q</c>), « Quête n° X » sans les textes du client.</summary>
        public string Name => QuestTexts.QuestName(Id);

        public QuestEntry(int id, bool finished, int? sortOrder = null) { Id = id; IsFinished = finished; SortOrder = sortOrder; }
        public override string ToString() => Name;
    }

    /// <summary>État d'une étape dans la hiérarchie du client (<c>QuestStep._nState</c>).</summary>
    public enum QuestStepState
    {
        /// <summary>0 : étape suivante, pas encore commencée (libellé grisé).</summary>
        NotDone = 0,
        /// <summary>1 : étape courante (flèche).</summary>
        Current = 1,
        /// <summary>2 : étape précédente, validée (coche).</summary>
        Finished = 2,
    }

    /// <summary>Objectif de l'étape courante (<c>dofus.datacenter.QuestObjective</c>) : texte composé et coordonnées du client.</summary>
    public sealed class QuestObjective
    {
        public int Id { get; }
        public bool IsFinished { get; }
        /// <summary>Modèle <c>Q.t[type]</c> et paramètres résolus (PNJ, objet, monstre, sous-zone), ou « Objectif n° X ».</summary>
        public string Description => QuestTexts.ObjectiveText(Id);
        /// <summary>Coordonnées <c>Q.o[id].x/y</c> (boussole du client) ou <c>null</c>.</summary>
        public Point? Coordinates => QuestTexts.ObjectiveCoordinates(Id);

        public QuestObjective(int id, bool finished) { Id = id; IsFinished = finished; }
        public override string ToString() => Description;
    }

    /// <summary>Ligne de la hiérarchie des étapes (<c>QuestStep.allSteps</c>) : précédentes, courante puis suivantes.</summary>
    public sealed class QuestStepLine
    {
        public int Id { get; }
        public QuestStepState State { get; }
        public string Name => QuestTexts.StepName(Id);
        public string Description => QuestTexts.StepDescription(Id);

        public QuestStepLine(int id, QuestStepState state) { Id = id; State = state; }
        public override string ToString() => Name;
    }

    public enum QuestRewardKind { Experience, Kamas, Item, Emote, Job, Spell }

    /// <summary>Récompense d'une étape (<c>Q.s[id].r = [xp, kamas, [[objet, qté]…], [émotes], [métiers], [sorts]]</c>, <c>QuestStep.rewards</c>).</summary>
    public sealed class QuestReward
    {
        public QuestRewardKind Kind { get; }
        /// <summary>Objet, émote, métier ou sort ; 0 pour l'expérience et les kamas.</summary>
        public int Id { get; }
        /// <summary>Expérience, kamas ou quantité d'objets.</summary>
        public long Amount { get; }
        /// <summary>Libellé du client : nombre pour l'expérience et les kamas, « x2 Nom » (quantité &gt; 1) ou nom sinon.</summary>
        public string Label { get; }

        public QuestReward(QuestRewardKind kind, int id, long amount, string label) { Kind = kind; Id = id; Amount = amount; Label = label ?? string.Empty; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// Réponse <c>QS</c> de StarLoco (<c>Quest.getGmQuestDataPacket</c>), lue comme <c>dofus.aks.Quests.onStep</c> :
    /// <c>&lt;quête&gt;|&lt;étape courante&gt;|&lt;objectif,0|1;…&gt;|&lt;étapes précédentes ;…&gt;|&lt;étapes suivantes ;…&gt;[|&lt;question&gt;[;p1,p2]|]</c>.
    /// Une étape courante vide (toutes les étapes validées) donne <see cref="StepId"/> = 0.
    /// </summary>
    public sealed class QuestStep
    {
        public int QuestId { get; }
        /// <summary>Étape courante (<c>Q.s</c>), 0 si le serveur n'en donne pas (quête terminée).</summary>
        public int StepId { get; }
        public IReadOnlyList<QuestObjective> Objectives { get; }
        /// <summary>Étapes précédentes, dans l'ordre du client (liste reçue inversée : la plus ancienne d'abord).</summary>
        public IReadOnlyList<int> PreviousSteps { get; }
        public IReadOnlyList<int> NextSteps { get; }
        /// <summary>Question du PNJ de la quête (<c>STEP_DIALOG</c>), <c>null</c> si absente.</summary>
        public int? DialogId { get; }
        public IReadOnlyList<string> DialogParameters { get; }

        public QuestStep(int questId, int stepId, IList<QuestObjective> objectives, IList<int> previous, IList<int> next, int? dialogId, IList<string> dialogParameters)
        {
            QuestId = questId; StepId = stepId;
            Objectives = (objectives ?? new QuestObjective[0]).ToArray();
            PreviousSteps = (previous ?? new int[0]).ToArray();
            NextSteps = (next ?? new int[0]).ToArray();
            DialogId = dialogId;
            DialogParameters = (dialogParameters ?? new string[0]).ToArray();
        }

        public bool HasCurrentStep => StepId > 0;
        public string Name => HasCurrentStep ? QuestTexts.StepName(StepId) : string.Empty;
        public string Description => HasCurrentStep ? QuestTexts.StepDescription(StepId) : string.Empty;
        public IReadOnlyList<QuestReward> Rewards => HasCurrentStep ? QuestTexts.Rewards(StepId) : new QuestReward[0];
        /// <summary>Texte de la question du PNJ (paramètres substitués), ou <c>null</c>.</summary>
        public string DialogText => DialogId.HasValue ? QuestTexts.Dialog(DialogId.Value, DialogParameters.ToList()) : null;

        /// <summary>Hiérarchie des étapes (<c>allSteps</c>) : précédentes (validées), courante, suivantes.</summary>
        public IReadOnlyList<QuestStepLine> AllSteps
        {
            get
            {
                var lines = new List<QuestStepLine>();
                lines.AddRange(PreviousSteps.Select(id => new QuestStepLine(id, QuestStepState.Finished)));
                if (HasCurrentStep) lines.Add(new QuestStepLine(StepId, QuestStepState.Current));
                lines.AddRange(NextSteps.Select(id => new QuestStepLine(id, QuestStepState.NotDone)));
                return lines;
            }
        }
    }

    /// <summary>Lecture des corps de paquets <c>QL</c> et <c>QS</c> ; jamais d'exception, une erreur est rendue en texte.</summary>
    public static class QuestPackets
    {
        /// <summary>Taille maximale acceptée d'une liste (garde contre un paquet démesuré).</summary>
        public const int MaxEntries = 4096;

        /// <summary>
        /// Corps de <c>QL</c> sans « QL » : StarLoco écrit <c>+id;fini|id;fini</c>, le client lit à partir du troisième caractère
        /// (<c>substr(3)</c>) <c>id;fini[;ordre]</c> séparés par « | ». Un « + » de tête est donc sauté ; un corps vide (ou « + » seul)
        /// est une liste vide. Comme le client, seul « 1 » vaut « terminée ». Une entrée dont l'identifiant n'est pas un entier
        /// positif rend tout le paquet illisible (le client créerait une quête NaN).
        /// </summary>
        public static bool TryParseList(string body, out List<QuestEntry> quests, out string error)
        {
            quests = new List<QuestEntry>(); error = null;
            if (body == null) { error = "corps absent"; return false; }
            if (body.StartsWith("+", StringComparison.Ordinal)) body = body.Substring(1);
            if (body.Length == 0) return true;
            string[] entries = body.Split('|');
            if (entries.Length > MaxEntries) { error = "liste trop longue"; return false; }
            var seen = new HashSet<int>();
            foreach (string entry in entries)
            {
                string[] fields = entry.Split(';');
                if (!TryId(fields[0], out int id)) { error = "identifiant de quête illisible « " + fields[0] + " »"; quests.Clear(); return false; }
                int? order = fields.Length > 2 && int.TryParse(fields[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
                if (!seen.Add(id)) continue; // le client garderait les deux lignes ; le bot n'en montre qu'une
                quests.Add(new QuestEntry(id, fields.Length > 1 && fields[1] == "1", order));
            }
            return true;
        }

        /// <summary>Corps de <c>QS</c> sans « QS » (voir <see cref="QuestStep"/>).</summary>
        public static bool TryParseStep(string body, out QuestStep step, out string error)
        {
            step = null; error = null;
            if (string.IsNullOrEmpty(body)) { error = "corps vide"; return false; }
            string[] fields = body.Split('|');
            if (fields.Length < 3) { error = "champs manquants"; return false; }
            if (!TryId(fields[0], out int questId)) { error = "identifiant de quête illisible « " + fields[0] + " »"; return false; }
            int stepId = 0;
            if (fields[1].Length > 0 && !TryId(fields[1], out stepId)) { error = "étape illisible « " + fields[1] + " »"; return false; }
            var objectives = new List<QuestObjective>();
            if (fields[2].Length > 0)
            {
                foreach (string entry in fields[2].Split(';'))
                {
                    string[] parts = entry.Split(',');
                    if (!TryId(parts[0], out int objective)) { error = "objectif illisible « " + entry + " »"; return false; }
                    objectives.Add(new QuestObjective(objective, parts.Length > 1 && parts[1] == "1"));
                }
            }
            if (!TryIds(Field(fields, 3), out List<int> previous)) { error = "étapes précédentes illisibles « " + Field(fields, 3) + " »"; return false; }
            previous.Reverse(); // r9.reverse() du client
            if (!TryIds(Field(fields, 4), out List<int> next)) { error = "étapes suivantes illisibles « " + Field(fields, 4) + " »"; return false; }
            int? dialog = null; var parameters = new List<string>();
            string dialogField = Field(fields, 5);
            if (dialogField.Length > 0)
            {
                string[] parts = dialogField.Split(';');
                if (int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int question) && question > 0) dialog = question;
                if (parts.Length > 1 && parts[1].Length > 0) parameters.AddRange(parts[1].Split(','));
            }
            step = new QuestStep(questId, stepId, objectives, previous, next, dialog, parameters);
            return true;
        }

        private static string Field(string[] fields, int index) => index < fields.Length ? fields[index] : string.Empty;

        private static bool TryId(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

        private static bool TryIds(string text, out List<int> values)
        {
            values = new List<int>();
            if (string.IsNullOrEmpty(text)) return true;
            foreach (string part in text.Split(';'))
            {
                if (!TryId(part, out int id)) return false;
                values.Add(id);
            }
            return true;
        }
    }

    /// <summary>
    /// Textes du client pour les quêtes (<c>quests_fr</c> : <c>Q.q</c>, <c>Q.s</c>, <c>Q.o</c>, <c>Q.t</c> ; <c>lang_fr</c>) avec repli
    /// quand ils ne sont pas chargés ; jamais d'exception.
    /// </summary>
    public static class QuestTexts
    {
        public static string Get(string key, string fallback, params string[] args)
        {
            args = args ?? new string[0];
            try
            {
                if (key != null && LangData.Text.Has(key)) return LangData.Text.Plain(LangData.Text.Get(key, args));
            }
            catch (Exception) { /* Fichier de langue illisible : texte de repli. */ }
            try { return string.Format(CultureInfo.CurrentCulture, fallback ?? key ?? string.Empty, args.Cast<object>().ToArray()); }
            catch (FormatException) { return fallback ?? key ?? string.Empty; }
        }

        /// <summary>« %1 quête{~ps} en cours » accordé comme <c>Quests.setPendingCount</c>.</summary>
        public static string PendingCount(int count)
        {
            string number = count.ToString(CultureInfo.CurrentCulture);
            string text = Get("PENDING_QUEST", count < 2 ? "{0} quête en cours" : "{0} quêtes en cours", number);
            return LangData.Combine(text, "m", count < 2) ?? text;
        }

        public static string QuestName(int id) => Lookup("quete", id, "nom") ?? "Quête n° " + Number(id);
        public static string StepName(int id) => Lookup("etape", id, "nom") ?? "Étape n° " + Number(id);
        public static string StepDescription(int id) => Lookup("etape", id, "description") ?? string.Empty;

        public static string ObjectiveText(int id)
        {
            try
            {
                if (Raw("objectif", id) != null)
                {
                    string text = LangData.Quest.Objective(id);
                    if (!string.IsNullOrEmpty(text) && text != Number(id)) return text;
                }
            }
            catch (Exception) { /* Paramètres illisibles : repli. */ }
            return "Objectif n° " + Number(id);
        }

        public static Point? ObjectiveCoordinates(int id)
        {
            IReadOnlyDictionary<string, string> raw = Raw("objectif", id);
            if (raw == null || !raw.TryGetValue("x", out string x) || !raw.TryGetValue("y", out string y)) return null;
            if (!int.TryParse(x, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int px)
                || !int.TryParse(y, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int py)) return null;
            return new Point(px, py);
        }

        /// <summary>Question du PNJ (<c>D.q</c>) avec ses paramètres, ou « Question n° X ».</summary>
        public static string Dialog(int id, IList<string> parameters)
        {
            try { if (LangData.Dialog.HasQuestion(id)) return LangData.Text.Plain(LangData.Dialog.Question(id, parameters)); }
            catch (Exception) { /* Texte illisible : repli. */ }
            return "Question n° " + Number(id);
        }

        /// <summary>Récompenses de l'étape dans l'ordre de <c>QuestStep.rewards</c> ; les valeurs absentes (<c>null</c>) sont sautées.</summary>
        public static IReadOnlyList<QuestReward> Rewards(int stepId)
        {
            var rewards = new List<QuestReward>();
            IReadOnlyDictionary<string, string> raw = Raw("etape", stepId);
            if (raw == null || !raw.TryGetValue("recompenses", out string json) || string.IsNullOrWhiteSpace(json)) return rewards;
            try
            {
                var array = Newtonsoft.Json.Linq.JArray.Parse(json);
                long? Long(int index) => index < array.Count && array[index].Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (long?)array[index] : null;
                Newtonsoft.Json.Linq.JArray List(int index) => index < array.Count ? array[index] as Newtonsoft.Json.Linq.JArray : null;
                if (Long(0) is long xp) rewards.Add(new QuestReward(QuestRewardKind.Experience, 0, xp, xp.ToString(CultureInfo.CurrentCulture)));
                if (Long(1) is long kamas) rewards.Add(new QuestReward(QuestRewardKind.Kamas, 0, kamas, kamas.ToString(CultureInfo.CurrentCulture)));
                foreach (var item in List(2) ?? new Newtonsoft.Json.Linq.JArray())
                {
                    if (!(item is Newtonsoft.Json.Linq.JArray pair) || pair.Count < 1 || pair[0].Type != Newtonsoft.Json.Linq.JTokenType.Integer) continue;
                    int id = (int)pair[0];
                    long quantity = pair.Count > 1 && pair[1].Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (long)pair[1] : 1;
                    string name = LangData.Item.Has(id) ? LangData.Item.Name(id) : "Objet n° " + Number(id);
                    rewards.Add(new QuestReward(QuestRewardKind.Item, id, quantity, (quantity > 1 ? "x" + quantity.ToString(CultureInfo.CurrentCulture) + " " : string.Empty) + name));
                }
                foreach (int emote in Ints(List(3)))
                    rewards.Add(new QuestReward(QuestRewardKind.Emote, emote, 1, LangData.IsLoaded("emotes") ? LangData.Emote.Name(emote) : "Émote n° " + Number(emote)));
                foreach (int job in Ints(List(4)))
                    rewards.Add(new QuestReward(QuestRewardKind.Job, job, 1, LangData.IsLoaded("jobs") ? LangData.Job.Name(job) : "Métier n° " + Number(job)));
                foreach (int spell in Ints(List(5)))
                    rewards.Add(new QuestReward(QuestRewardKind.Spell, spell, 1, LangData.Spell.Has(spell) ? LangData.Spell.Name(spell) : "Sort n° " + Number(spell)));
            }
            catch (Exception) { /* JSON illisible : les récompenses déjà lues restent. */ }
            return rewards;
        }

        private static IEnumerable<int> Ints(Newtonsoft.Json.Linq.JArray array)
        {
            if (array == null) yield break;
            foreach (var token in array)
                if (token.Type == Newtonsoft.Json.Linq.JTokenType.Integer) yield return (int)token;
                else if (token.Type == Newtonsoft.Json.Linq.JTokenType.String && int.TryParse((string)token, NumberStyles.None, CultureInfo.InvariantCulture, out int value)) yield return value;
        }

        private static IReadOnlyDictionary<string, string> Raw(string table, int id)
        {
            try { return LangData.Raw("quests", table, Number(id)); }
            catch (Exception) { return null; }
        }

        private static string Lookup(string table, int id, string attribute)
        {
            IReadOnlyDictionary<string, string> raw = Raw(table, id);
            return raw != null && raw.TryGetValue(attribute, out string value) && value != null && value != "null" ? value : null;
        }

        private static string Number(int id) => id.ToString(CultureInfo.InvariantCulture);
    }
}
