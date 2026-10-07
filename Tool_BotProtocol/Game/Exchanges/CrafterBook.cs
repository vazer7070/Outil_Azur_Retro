using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Tool_BotProtocol.Game.Accounts;
using Tool_BotProtocol.Game.Interactions;
using Tool_BotProtocol.Game.Jobs;

namespace Tool_BotProtocol.Game.Exchanges
{
    /// <summary>
    /// Artisan du livre des artisans (<c>EJ+…</c>, <c>Exchange.onCrafterListChanged</c> du client 1.34) :
    /// <c>+&lt;métier&gt;;&lt;id&gt;;&lt;nom&gt;;&lt;niveau&gt;;&lt;carte&gt;;&lt;à l'atelier 0|1&gt;;&lt;classe&gt;;&lt;sexe&gt;;&lt;c1&gt;,&lt;c2&gt;,&lt;c3&gt;;&lt;accessoires&gt;;&lt;options&gt;,&lt;cases&gt;</c>.
    /// </summary>
    public sealed class Crafter
    {
        public int JobId { get; internal set; }
        /// <summary>Identifiant du personnage, gardé tel que reçu (le client le compare comme texte).</summary>
        public string Id { get; internal set; } = string.Empty;
        public string Name { get; internal set; } = string.Empty;
        public int JobLevel { get; internal set; }
        public int MapId { get; internal set; }
        /// <summary>Vrai si l'artisan est dans un atelier (StarLoco : cartes 8731 et 8732 seulement).</summary>
        public bool InWorkshop { get; internal set; }
        public int Breed { get; internal set; }
        public int Sex { get; internal set; }
        public string[] Colors { get; internal set; } = new string[0];
        public string Accessories { get; internal set; } = string.Empty;
        public JobOptions Options { get; internal set; } = JobOptions.Default;

        /// <summary>Position de la carte d'après les textes du client, sinon son numéro.</summary>
        public string Location
        {
            get
            {
                try
                {
                    var point = Data.LangData.Map.Coords(MapId);
                    if (point != null) return point.Value.X.ToString(CultureInfo.InvariantCulture) + "," + point.Value.Y.ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception error) when (!(error is OutOfMemoryException)) { /* Textes illisibles : numéro de carte. */ }
                return "carte " + MapId.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Lit une entrée <c>EJ+</c> (sans le signe) ; <c>null</c> si le métier, l'identifiant ou le nom manquent.</summary>
        public static Crafter Parse(string body)
        {
            string[] fields = (body ?? string.Empty).Split(';');
            if (fields.Length < 3 || !TryInt(fields[0], out int job) || job <= 0 || fields[1].Length == 0 || fields[2].Length == 0) return null;
            int Field(int index) => index < fields.Length && TryInt(fields[index], out int value) ? value : 0;
            string[] options = fields.Length > 10 ? fields[10].Split(',') : new string[0];
            int flags = options.Length > 0 && TryInt(options[0], out int f) ? f : 0;
            int slots = options.Length > 1 && TryInt(options[1], out int s) ? s : 0;
            return new Crafter
            {
                JobId = job, Id = fields[1], Name = fields[2], JobLevel = Field(3), MapId = Field(4), InWorkshop = Field(5) != 0,
                Breed = Field(6), Sex = Field(7), Colors = fields.Length > 8 ? fields[8].Split(',') : new string[0],
                Accessories = fields.Length > 9 ? fields[9] : string.Empty, Options = new JobOptions(flags, slots)
            };
        }

        private static bool TryInt(string value, out int result) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Livre des artisans (type 14), l'interface <c>CrafterList</c> du client 1.34 (propriétaire : lot F6). La compétence 170 d'un livre
    /// (<c>GameCase</c> de StarLoco) répond <c>ECK14|&lt;métier&gt;;&lt;métier&gt;;…</c> ; choisir un métier envoie <c>EJF&lt;métier&gt;</c>
    /// (<c>Exchange.getCrafterForJob</c>) et StarLoco répond un <c>EJ+…</c> par artisan connecté qui exerce ce métier (ou un message si
    /// aucun) ; <c>EJ-&lt;métier&gt;;&lt;id&gt;</c> en retire un. Fermer envoie <c>EV</c>.
    /// </summary>
    [ExchangeType(ExchangeTypes.CrafterList)]
    public sealed class CrafterBook : ExchangeWindow
    {
        private readonly object sync = new object();
        private int[] jobs = new int[0];
        private List<Crafter> crafters = new List<Crafter>();

        internal CrafterBook(Accounts.Accounts account) : base(account) { }

        protected override string Reference => "ARTISANS";
        protected override AccountStates OpenState => AccountStates.EXCHANGE;

        /// <summary>Métiers proposés par le livre (<c>ECK14</c>), dans l'ordre reçu.</summary>
        public IReadOnlyList<int> Jobs { get { lock (sync) return jobs.ToArray(); } }
        /// <summary>Artisans reçus (<c>EJ+</c>), dans l'ordre d'arrivée.</summary>
        public IReadOnlyList<Crafter> Crafters { get { lock (sync) return crafters.ToArray(); } }
        /// <summary>Métier demandé par le dernier <c>EJF</c>, 0 sinon.</summary>
        public int SelectedJob { get; private set; }

        /// <summary><c>EJF&lt;métier&gt;</c> : artisans connectés de ce métier.</summary>
        public Task<InteractionResult> FindCraftersAsync(int jobId)
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun livre des artisans ouvert."));
            if (jobId <= 0) return Task.FromResult(Refuse("Métier inconnu."));
            SelectedJob = jobId;
            lock (sync) crafters = new List<Crafter>();
            Notify();
            return SendAsync("EJF" + jobId.ToString(CultureInfo.InvariantCulture), "Artisans « " + JobCatalog.JobName(jobId) + " » demandés.");
        }

        /// <summary>Envoie <c>EV</c> ; la fenêtre se ferme au <c>EV</c> du serveur.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun livre des artisans ouvert."));
            return SendAsync("EV", "Fermeture du livre des artisans demandée.");
        }

        /// <summary><c>ECK14|&lt;métier&gt;;…</c>.</summary>
        protected override void HandleCreated(int type, string data)
        {
            var list = new List<int>();
            int unreadable = 0;
            foreach (string part in data.Split(';'))
            {
                if (part.Length == 0) continue;
                if (TryInt(part, out int job) && job > 0) { if (!list.Contains(job)) list.Add(job); }
                else unreadable++;
            }
            if (unreadable > 0) Malformed("ECK14", data);
            lock (sync) jobs = list.ToArray();
            MarkOpen();
            Log(JobCatalog.Text("CRAFTERS_LIST", "Liste des artisans") + " : " + list.Count + " métier(s).");
            Notify();
        }

        /// <summary><c>EJ+&lt;fiche&gt;</c> ajoute ou remplace un artisan (même identifiant), <c>EJ-&lt;métier&gt;;&lt;id&gt;</c> le retire.</summary>
        internal void OnCrafterListChanged(string body)
        {
            if (!IsOpen) { Account?.Logger?.LogDebug(Reference, "EJ" + body + " reçu sans livre des artisans ouvert : ignoré."); return; }
            if (string.IsNullOrEmpty(body) || (body[0] != '+' && body[0] != '-')) { Malformed("EJ", body); return; }
            if (body[0] == '-')
            {
                string[] fields = body.Substring(1).Split(';');
                string id = fields.Length > 1 ? fields[1] : string.Empty;
                if (id.Length == 0) { Malformed("EJ", body); return; }
                lock (sync) crafters = crafters.Where(entry => entry.Id != id).ToList();
                Notify();
                return;
            }
            Crafter crafter = Crafter.Parse(body.Substring(1));
            if (crafter == null) { Malformed("EJ", body); return; }
            lock (sync)
            {
                var updated = crafters.ToList();
                int index = updated.FindIndex(entry => entry.Id == crafter.Id);
                if (index >= 0) updated[index] = crafter; else updated.Add(crafter);
                crafters = updated;
            }
            Notify();
        }

        protected override void ResetState()
        {
            lock (sync) { jobs = new int[0]; crafters = new List<Crafter>(); }
            SelectedJob = 0;
        }
    }
}
