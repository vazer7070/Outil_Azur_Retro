using System;
using System.Globalization;
using Tool_BotProtocol.Game.Maps.Interactives;

namespace Tool_BotProtocol.Game.Jobs
{
    /// <summary>
    /// Compétence d'un métier telle que <c>JS</c> la transmet : <c>&lt;id&gt;~&lt;p1&gt;~&lt;p2&gt;~&lt;p3&gt;~&lt;p4&gt;</c>
    /// (<c>dofus.datacenter.Skill</c> du client 1.34, <c>JobStat.parseJS</c> de StarLoco). Récolte : p1/p2 = quantités
    /// minimale et maximale, p4 = durée en millisecondes. Artisanat : p1 = nombre de cases de l'atelier, p2 = 0, p4 = chance en %.
    /// Une instance n'est plus modifiée après sa publication : un nouveau <c>JS</c> remplace la liste entière du métier.
    /// </summary>
    public class JobSkills
    {
        public short Id { get; private set; }
        /// <summary>Paramètre 1 : quantité minimale (récolte) ou nombre de cases (artisanat).</summary>
        public byte QuaMini { get; private set; }
        /// <summary>Paramètre 2 : quantité maximale (récolte), 0 pour l'artisanat.</summary>
        public byte QuaMax { get; private set; }
        /// <summary>Paramètre 3, toujours 0 chez StarLoco.</summary>
        public int Param3 { get; private set; }
        /// <summary>Objet interactif connu du bot (<c>BotInteractives</c>) qui porte la compétence, ou <c>null</c>.</summary>
        public InteractivesParent Interactive { get; private set; }
        /// <summary>
        /// Vrai pour une compétence d'artisanat (atelier, <c>ECK3</c>), faux pour une récolte. Déterminé comme le client :
        /// une compétence de <c>skills_fr</c> qui produit un objet (<c>i</c>) est une récolte, les autres sont des ateliers ;
        /// sans les textes du client, par l'objet interactif connu du bot, puis par le format de StarLoco (maximum à 0).
        /// </summary>
        public bool CanCraft { get; private set; }
        public bool IsHarvest => !CanCraft;
        /// <summary>Paramètre 4 : durée de récolte (ms) ou chance de réussite (%) selon la nature de la compétence.</summary>
        public float Time { get; private set; }

        /// <summary>Cases de l'atelier (artisanat) ; 0 pour une récolte.</summary>
        public int Slots => CanCraft ? QuaMini : 0;
        /// <summary>Chance de réussite annoncée (artisanat) ; 0 pour une récolte.</summary>
        public int Chance => CanCraft ? (int)Time : 0;
        /// <summary>Durée de la récolte en millisecondes ; 0 pour l'artisanat.</summary>
        public int DurationMs => CanCraft ? 0 : (int)Time;
        /// <summary>Nom de la compétence (<c>skills_fr</c>), ou son numéro.</summary>
        public string Name => JobCatalog.SkillName(Id);
        /// <summary>Objet interactif où s'exerce la compétence (texte du client), ou <c>null</c>.</summary>
        public string Source => JobCatalog.SkillSource(Id);

        public JobSkills(short id, byte min, byte max, float T) : this(id, min, max, 0, T) { }

        public JobSkills(short id, byte min, byte max, int param3, float param4)
        {
            Id = id;
            QuaMini = min;
            QuaMax = max;
            Param3 = param3;
            Time = param4;
            InteractivesParent parent = null;
            try { parent = InteractivesParent.GetInteractiveBySkill(id); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { parent = null; }
            Interactive = parent;
            bool? fromLang = JobCatalog.IsCraftSkill(id);
            if (fromLang.HasValue) CanCraft = fromLang.Value;
            else if (parent != null) CanCraft = !parent.Recoltable;
            else CanCraft = max == 0 && min > 0;
        }

        /// <summary>
        /// Lit une entrée <c>id~p1~p2~p3~p4</c> du paquet <c>JS</c> ; les paramètres absents ou illisibles valent 0
        /// (le client les lit avec <c>Number</c>). <c>null</c> si l'identifiant est illisible.
        /// </summary>
        public static JobSkills Parse(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return null;
            string[] fields = entry.Split('~');
            if (!short.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out short id) || id <= 0) return null;
            int Field(int index) => index < fields.Length && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
            float time = 0;
            if (fields.Length > 4 && !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out time)) time = 0;
            if (float.IsNaN(time) || float.IsInfinity(time)) time = 0;
            return new JobSkills(id, ToByte(Field(1)), ToByte(Field(2)), Field(3), Math.Max(0, time));
        }

        private static byte ToByte(int value) => (byte)Math.Max(0, Math.Min(byte.MaxValue, value));

        /// <summary>Ancienne mise à jour en place, conservée pour compatibilité ; <c>JS</c> remplace désormais la liste.</summary>
        public void Actualise(short id, byte min, byte max, float T)
        {
            Id = id;
            QuaMini = min;
            QuaMax = max;
            Time = T;
        }

        /// <summary>
        /// Texte de la compétence comme <c>JobViewerSkillItem</c> : « (2,5 s)  1 à 3 » pour une récolte,
        /// « 8 cases (100 %) » pour l'artisanat.
        /// </summary>
        public string Describe()
        {
            if (CanCraft)
                return Slots.ToString(CultureInfo.CurrentCulture) + " " + JobCatalog.SlotWord(Slots) + " (" + Chance.ToString(CultureInfo.CurrentCulture) + " %)";
            double seconds = Math.Round(Time / 100.0) / 10.0;
            string quantity = QuaMini == QuaMax || QuaMax == 0 ? QuaMini.ToString(CultureInfo.CurrentCulture)
                : QuaMini.ToString(CultureInfo.CurrentCulture) + " " + JobCatalog.Text("TO_RANGE", "à").Trim() + " " + QuaMax.ToString(CultureInfo.CurrentCulture);
            return "(" + seconds.ToString("0.#", CultureInfo.CurrentCulture) + " s)  " + quantity;
        }

        public override string ToString() => Name + " — " + Describe();
    }
}
