using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Exchanges;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Perso;

namespace Tool_BotProtocol.Game.Jobs
{
    /// <summary>
    /// Métiers du personnage selon <c>dofus.aks.Job</c> et la partie artisanat de <c>dofus.aks.Exchange</c> du client 1.34, lus comme StarLoco
    /// les envoie (<c>JobStat</c>, <c>SocketManager.GAME_SEND_J*</c>, <c>GameClient.parseJobOption</c>, <c>setPublicMode</c>) (propriétaire : lot F6).
    /// Réceptions (via <c>JobsFrame</c>) : <c>JS|&lt;métier&gt;;&lt;compétence~p1~p2~p3~p4&gt;,…|…</c> (compétences, liste remplacée),
    /// <c>JX|&lt;métier&gt;;&lt;niveau&gt;;&lt;xp min&gt;;&lt;xp&gt;;&lt;xp max&gt;;|…</c>, <c>JN&lt;métier&gt;|&lt;niveau&gt;</c> (<c>NEW_JOB_LEVEL</c>), <c>JR&lt;métier&gt;</c>
    /// (métier désappris), <c>JO&lt;position&gt;|&lt;options&gt;|&lt;cases minimum&gt;</c>, <c>EW±</c> / <c>EW+&lt;id&gt;|&lt;compétences&gt;</c> / <c>EW-&lt;id&gt;</c>
    /// (mode public), <c>Ej±&lt;métier&gt;</c> (référencement), et pour l'atelier et le livre : <c>Ec</c>, <c>EA</c>, <c>Ea</c>, <c>EJ</c>.
    /// Envois : <c>JO&lt;position dans la liste&gt;|&lt;options&gt;|&lt;cases minimum&gt;</c> (<c>Job.changeJobStats</c>) et <c>EW+</c> / <c>EW-</c>
    /// (<c>Exchange.setPublicMode</c>). Les événements sont levés sur le fil de réception réseau.
    /// </summary>
    public sealed class JobsActions
    {
        private const string Reference = "MÉTIERS";
        /// <summary>Délai pendant lequel un <c>JO</c> reçu identique à la dernière demande lui est attribué (voir <see cref="OnOptions"/>).</summary>
        private static readonly TimeSpan OptionsAnswerDelay = TimeSpan.FromSeconds(10);
        private readonly Accounts.Accounts account;
        private readonly object sync = new object();
        private readonly HashSet<int> references = new HashSet<int>();
        private readonly Dictionary<long, short[]> publicSkills = new Dictionary<long, short[]>();
        private bool publicMode;
        private string lastMessage = string.Empty;
        private PendingOptions pendingOptions;

        private sealed class PendingOptions
        {
            internal int JobId, Index, Flags, MinSlots;
            internal DateTime SentAt;
        }

        internal JobsActions(Accounts.Accounts owner, ExchangeRegistry registry)
        {
            account = owner;
            Craft = registry?.Get<CraftExchange>();
            Book = registry?.Get<CrafterBook>();
        }

        /// <summary>Atelier (type 3).</summary>
        public CraftExchange Craft { get; }
        /// <summary>Livre des artisans (type 14).</summary>
        public CrafterBook Book { get; }
        /// <summary>Mode public de l'artisan (<c>Player.craftPublicMode</c>, <c>EW+</c> / <c>EW-</c>).</summary>
        public bool PublicMode { get { lock (sync) return publicMode; } }
        /// <summary>Métiers pour lesquels le personnage est référencé dans le livre des artisans (<c>Ej+</c>).</summary>
        public IReadOnlyCollection<int> ReferencedJobs { get { lock (sync) return references.OrderBy(id => id).ToArray(); } }
        /// <summary>Dernier message (annonce traduite, refus local, confirmation d'envoi).</summary>
        public string LastMessage { get { lock (sync) return lastMessage; } }

        /// <summary>Compétences d'atelier annoncées pour un artisan en mode public (<c>multiCraftSkillsID</c>, <c>EW+&lt;id&gt;|…</c>) ; vide sinon.</summary>
        public IReadOnlyList<short> PublicSkillsOf(long actorId)
        {
            lock (sync) return publicSkills.TryGetValue(actorId, out short[] skills) ? skills.ToArray() : new short[0];
        }

        /// <summary>État changé (fil réseau ou appelant).</summary>
        public event Action Changed;
        /// <summary><c>JN&lt;métier&gt;|&lt;niveau&gt;</c> : le client affiche la boîte <c>NEW_JOB_LEVEL</c>.</summary>
        public event Action<int, int> LevelUp;
        /// <summary><c>JR&lt;métier&gt;</c> : métier désappris.</summary>
        public event Action<int> JobRemoved;

        private CharacterClass Character => account?.Game?.character;

        /// <summary>Position du métier dans la liste du personnage (ordre de <c>JS</c>), comme <c>Jobs.findFirstItem("id", …).index</c>, ou -1.</summary>
        public int IndexOf(int jobId)
        {
            CharacterClass character = Character;
            if (character == null) return -1;
            lock (character.Jobs) return character.Jobs.FindIndex(job => job != null && job.ID == jobId);
        }

        /// <summary><c>JS|…</c> : compétences par métier ; un métier inconnu est ajouté à la fin de la liste, sa liste de compétences est remplacée.</summary>
        internal void OnSkills(string payload)
        {
            CharacterClass character = Character;
            if (character == null) return;
            int read = 0, unreadable = 0;
            lock (character.Jobs)
                foreach (string data in (payload ?? string.Empty).Split('|'))
                {
                    if (data.Length == 0) continue;
                    string[] parts = data.Split(';');
                    if (!TryInt(parts[0], out int jobId) || jobId <= 0) { unreadable++; continue; }
                    var skills = new List<JobSkills>();
                    if (parts.Length > 1)
                        foreach (string entry in parts[1].Split(','))
                        {
                            if (entry.Length == 0) continue;
                            JobSkills skill = JobSkills.Parse(entry);
                            if (skill != null) skills.Add(skill); else unreadable++;
                        }
                    Jobs job = character.Jobs.Find(x => x != null && x.ID == jobId);
                    if (job == null) { job = new Jobs(jobId); character.Jobs.Add(job); }
                    job.ReplaceSkills(skills);
                    read++;
                }
            if (unreadable > 0) Malformed("JS", payload);
            if (read > 0) character.JobsRefreshEvent();
            RaiseChanged();
        }

        /// <summary><c>JX|&lt;métier&gt;;&lt;niveau&gt;;&lt;xp min&gt;;&lt;xp&gt;;&lt;xp max&gt;;|…</c> ; un métier encore inconnu est ajouté.</summary>
        internal void OnExperience(string payload)
        {
            CharacterClass character = Character;
            if (character == null) return;
            int read = 0, unreadable = 0;
            lock (character.Jobs)
                foreach (string data in (payload ?? string.Empty).Split('|'))
                {
                    if (data.Length == 0) continue;
                    string[] fields = data.Split(';');
                    if (fields.Length < 4 || !TryInt(fields[0], out int jobId) || jobId <= 0 || !TryInt(fields[1], out int level) || level < 0
                        || !TryUInt(fields[2], out uint baseXp) || !TryUInt(fields[3], out uint xp)) { unreadable++; continue; }
                    uint nextXp = level < 100 && fields.Length > 4 && TryUInt(fields[4], out uint next) ? next : 0;
                    Jobs job = character.Jobs.Find(x => x != null && x.ID == jobId);
                    if (job == null) { job = new Jobs(jobId); character.Jobs.Add(job); }
                    job.AcutalizeJob(level, baseXp, xp, nextXp);
                    read++;
                }
            if (unreadable > 0) Malformed("JX", payload);
            if (read > 0) character.JobsRefreshEvent();
            RaiseChanged();
        }

        /// <summary><c>JN&lt;métier&gt;|&lt;niveau&gt;</c> : nouveau niveau (<c>NEW_JOB_LEVEL</c>).</summary>
        internal void OnLevel(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 2 || !TryInt(fields[0], out int jobId) || jobId <= 0 || !TryInt(fields[1], out int level) || level <= 0) { Malformed("JN", payload); return; }
            CharacterClass character = Character;
            if (character != null)
            {
                lock (character.Jobs)
                {
                    Jobs job = character.Jobs.Find(x => x != null && x.ID == jobId);
                    if (job != null && job.Level < level) job.Level = level;
                }
                character.JobsRefreshEvent();
            }
            string name = JobCatalog.JobName(jobId), value = level.ToString(CultureInfo.InvariantCulture);
            Inform(JobCatalog.Text("NEW_JOB_LEVEL", "Ton métier " + name + " passe niveau " + value + ".", name, value), false);
            Raise(LevelUp, jobId, level);
            RaiseChanged();
        }

        /// <summary><c>JR&lt;métier&gt;</c> : métier désappris (<c>REMOVE_JOB</c>), retiré de la liste.</summary>
        internal void OnRemove(string payload)
        {
            if (!TryInt(payload, out int jobId) || jobId <= 0) { Malformed("JR", payload); return; }
            CharacterClass character = Character;
            bool removed = false;
            if (character != null)
            {
                lock (character.Jobs) removed = character.Jobs.RemoveAll(job => job != null && job.ID == jobId) > 0;
                if (removed) character.JobsRefreshEvent();
            }
            if (removed)
            {
                string name = JobCatalog.JobName(jobId);
                Inform(JobCatalog.Text("REMOVE_JOB", "Tu as désappris le métier " + name + ".", name), false);
                Raise(JobRemoved, jobId);
            }
            RaiseChanged();
        }

        /// <summary>
        /// <c>JO&lt;position&gt;|&lt;options&gt;|&lt;cases minimum&gt;</c> : options du métier à cette position de la liste (<c>Job.onOptions</c>).
        /// StarLoco n'initialise jamais <c>JobStat.position</c> et répond toujours <c>JO0|…</c> : une réponse dont les options et les cases sont
        /// exactement celles de la dernière demande (<see cref="SetOptionsAsync"/>, moins de 10 s avant) est donc attribuée au métier demandé.
        /// </summary>
        internal void OnOptions(string payload)
        {
            string[] fields = (payload ?? string.Empty).Split('|');
            if (fields.Length < 3 || !TryInt(fields[0], out int index) || index < 0 || !TryInt(fields[1], out int flags) || !TryInt(fields[2], out int minSlots))
            {
                Malformed("JO", payload);
                return;
            }
            CharacterClass character = Character;
            if (character == null) return;
            var options = new JobOptions(flags, minSlots);
            int target = index;
            lock (sync)
            {
                PendingOptions pending = pendingOptions;
                if (pending != null && DateTime.UtcNow - pending.SentAt <= OptionsAnswerDelay && pending.Flags == options.Flags && pending.MinSlots == options.MinSlots)
                {
                    int current = IndexOf(pending.JobId);
                    if (current >= 0) target = current;
                    pendingOptions = null;
                }
            }
            Jobs job;
            lock (character.Jobs) job = target < character.Jobs.Count ? character.Jobs[target] : null;
            if (job == null) { account?.Logger?.LogDebug(Reference, "JO" + payload + " : aucun métier à la position " + target + "."); return; }
            job.SetOptions(options);
            character.JobsRefreshEvent();
            RaiseChanged();
        }

        /// <summary>
        /// Options d'artisan (<c>JobOptionsViewer</c>, bouton « Sauvegarder ») : <c>JO&lt;position&gt;|&lt;options&gt;|&lt;cases minimum&gt;</c>, la position étant
        /// celle du métier dans la liste (<c>Jobs.findFirstItem</c>). Les cases minimum vont de 2 au plus grand nombre de cases du métier (8 au plus) ;
        /// sans réglage possible (2 cases ou moins), le client envoie 2. Les options changent à la réponse <c>JO</c> du serveur.
        /// </summary>
        public Task<InteractionResult> SetOptionsAsync(int jobId, int flags, int minSlots)
        {
            CharacterClass character = Character;
            int index = IndexOf(jobId);
            if (character == null || index < 0) return Refused("Ce métier n'est pas connu du personnage.");
            Jobs job;
            lock (character.Jobs) job = character.Jobs[index];
            if (!job.IsCraftJob) return Refused("Ce métier n'a pas d'atelier : il n'a pas d'options d'artisan.");
            if ((flags & ~7) != 0) return Refused("Options invalides.");
            int maxSlots = Math.Min(8, job.MaxSkillSlots);
            int slots = maxSlots > 2 ? minSlots : 2;
            if (slots < 2 || slots > Math.Max(2, maxSlots)) return Refused("Nombre minimum d'ingrédients invalide (2 à " + Math.Max(2, maxSlots) + ").");
            lock (sync) pendingOptions = new PendingOptions { JobId = jobId, Index = index, Flags = flags, MinSlots = slots, SentAt = DateTime.UtcNow };
            return SendAsync("JO" + index.ToString(CultureInfo.InvariantCulture) + "|" + flags.ToString(CultureInfo.InvariantCulture) + "|" + slots.ToString(CultureInfo.InvariantCulture),
                "Options du métier « " + job + " » envoyées.");
        }

        /// <summary>Mode public (<c>JobOptionsViewer</c>, bouton « Activer » / « Désactiver ») : <c>EW+</c> ou <c>EW-</c>.</summary>
        public Task<InteractionResult> SetPublicModeAsync(bool enabled)
        {
            CharacterClass character = Character;
            if (character == null || character.GetJobsSnapshot().Length == 0) return Refused("Le personnage n'a aucun métier.");
            return SendAsync(enabled ? "EW+" : "EW-", enabled ? "Activation du mode public demandée." : "Désactivation du mode public demandée.");
        }

        /// <summary>
        /// <c>EW+</c> / <c>EW-</c> : mode public du personnage ; <c>EW+&lt;id&gt;|&lt;compétence&gt;;…</c> / <c>EW-&lt;id&gt;</c> : compétences d'un artisan public
        /// (<c>multiCraftSkillsID</c> du sprite ; StarLoco ne l'envoie qu'au personnage lui-même).
        /// </summary>
        internal void OnPublicMode(string payload)
        {
            payload = payload ?? string.Empty;
            if (payload.Length == 0 || (payload[0] != '+' && payload[0] != '-')) { Malformed("EW", payload); return; }
            bool enabled = payload[0] == '+';
            if (payload.Length == 1)
            {
                lock (sync) publicMode = enabled;
                SetMessage(JobCatalog.Text("PUBLIC_MODE", "Mode public") + " : " + (enabled ? JobCatalog.Text("ACTIVE", "Actif") : JobCatalog.Text("INACTIVE", "Inactif")) + ".");
                account?.Logger?.LogInfo(Reference, LastMessage);
                RaiseChanged();
                return;
            }
            string[] fields = payload.Substring(1).Split('|');
            if (!long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actor)) { Malformed("EW", payload); return; }
            short[] skills = enabled && fields.Length > 1 && fields[1].Length > 0
                ? fields[1].Split(';').Select(part => short.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out short id) ? id : (short)0).Where(id => id > 0).ToArray()
                : new short[0];
            lock (sync) { if (skills.Length > 0) publicSkills[actor] = skills; else publicSkills.Remove(actor); }
            RaiseChanged();
        }

        /// <summary><c>Ej+&lt;métier&gt;</c> / <c>Ej-&lt;métier&gt;</c> : référencement dans le livre des artisans (<c>CRAFTER_REFERENCE_ADD/REMOVE</c>).</summary>
        internal void OnCrafterReference(string payload)
        {
            payload = payload ?? string.Empty;
            if (payload.Length < 2 || (payload[0] != '+' && payload[0] != '-') || !TryInt(payload.Substring(1), out int jobId) || jobId <= 0) { Malformed("Ej", payload); return; }
            bool added = payload[0] == '+';
            lock (sync) { if (added) references.Add(jobId); else references.Remove(jobId); }
            string name = JobCatalog.JobName(jobId);
            Inform(added ? JobCatalog.Text("CRAFTER_REFERENCE_ADD", "Vous êtes désormais référencé dans la liste des artisans en tant que " + name + ".", name)
                : JobCatalog.Text("CRAFTER_REFERENCE_REMOVE", "Vous n'êtes plus référencé en tant que " + name + " dans la liste des artisans.", name), false);
            RaiseChanged();
        }

        /// <summary><c>EcK…</c> / <c>EcE…</c> : résultat d'une fabrication, transmis à l'atelier ouvert (sinon journalisé).</summary>
        internal void OnCraft(bool success, string body)
        {
            if (Craft == null || !Craft.IsOpen) { account?.Logger?.LogDebug(Reference, "Ec" + (success ? "K" : "E") + body + " reçu sans atelier ouvert : ignoré."); return; }
            Craft.OnCraft(success, body);
            Inform(Craft.LastMessage, !success);
        }

        /// <summary><c>EA&lt;n&gt;</c> : étape d'une série.</summary>
        internal void OnCraftLoop(string body)
        {
            if (Craft == null || !Craft.IsOpen) { account?.Logger?.LogDebug(Reference, "EA" + body + " reçu sans atelier ouvert : ignoré."); return; }
            Craft.OnCraftLoop(body);
            Inform(Craft.LastMessage, false);
        }

        /// <summary><c>Ea&lt;code&gt;</c> : fin d'une série.</summary>
        internal void OnCraftLoopEnd(string body)
        {
            if (Craft == null || !Craft.IsOpen) { account?.Logger?.LogDebug(Reference, "Ea" + body + " reçu sans atelier ouvert : ignoré."); return; }
            Craft.OnCraftLoopEnd(body);
            Inform(Craft.LastMessage, Craft.LastLoopEnd != 1);
        }

        /// <summary><c>EJ±…</c> : livre des artisans.</summary>
        internal void OnCrafterListChanged(string body) => Book?.OnCrafterListChanged(body);

        /// <summary>Oublie l'état sans rien envoyer (déconnexion) ; l'atelier et le livre sont vidés par le registre des échanges.</summary>
        public void Clear()
        {
            lock (sync)
            {
                publicMode = false;
                references.Clear();
                publicSkills.Clear();
                pendingOptions = null;
                lastMessage = string.Empty;
            }
            RaiseChanged();
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
            SetMessage(message);
            account?.Logger?.LogInfo(Reference, message);
            RaiseChanged();
            return new InteractionResult(true, message);
        }

        private InteractionResult Refuse(string message) { SetMessage(message); RaiseChanged(); return new InteractionResult(false, message); }
        private Task<InteractionResult> Refused(string message) => Task.FromResult(Refuse(message));
        private void SetMessage(string message) { lock (sync) lastMessage = message ?? string.Empty; }
        private void Malformed(string prefix, string payload) =>
            account?.Logger?.LogError(Reference, "Paquet " + prefix + " illisible ignoré : " + (payload == null ? string.Empty : payload.Length > 80 ? payload.Substring(0, 80) + "…" : payload));

        /// <summary>Information du client (<c>INFO_CHAT</c> / <c>ERROR_CHAT</c>) : journal, ligne locale du chat et <see cref="LastMessage"/>.</summary>
        private void Inform(string text, bool error)
        {
            if (string.IsNullOrEmpty(text)) return;
            SetMessage(text);
            if (error) account?.Logger?.LogError(Reference, text); else account?.Logger?.LogInfo(Reference, text);
            try { account?.Game?.Chat?.AddLocal(error ? ChatMessageKind.Error : ChatMessageKind.Info, text); }
            catch (Exception failure) { account?.Logger?.LogException(Reference, failure); }
        }

        private void RaiseChanged()
        {
            Action handlers = Changed;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            }
        }

        private void Raise<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            }
        }

        private void Raise<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second)
        {
            if (handlers == null) return;
            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
            {
                try { handler(first, second); }
                catch (Exception error) { account?.Logger?.LogException(Reference, error); }
            }
        }

        private static bool TryInt(string value, out int result) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        private static bool TryUInt(string value, out uint result)
        {
            result = 0;
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) || parsed < 0) return false;
            result = (uint)Math.Min(uint.MaxValue, parsed);
            return true;
        }
    }
}
