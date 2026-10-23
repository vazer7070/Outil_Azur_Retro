using System.Globalization;

namespace Tool_BotProtocol.Game.Maps.Entities
{
    /// <summary>
    /// Percepteur : <c>GM|+cell;dir;0;id;prénom36,nom36;-6;6000^100;niveau;guilde;emblème</c> (<c>Collector.parseGM</c>,
    /// <c>createTaxCollector</c>) ; en combat <c>…;niveau;PV;PA;PM;résistances×7;équipe</c>.
    /// Le client compose le nom à partir des deux identifiants (base 36) : sans ces textes, « Percepteur de &lt;guilde&gt; ».
    /// </summary>
    public sealed class CollectorActor : MapActor
    {
        public override ActorKind Kind => ActorKind.Collector;
        /// <summary>Identifiants du prénom et du nom (base 36 dans le paquet), tels que lus.</summary>
        public int FirstNameId { get; set; }
        public int LastNameId { get; set; }
        public string NameIdsRaw { get; set; } = string.Empty;
        public int Level { get; set; }
        public string GuildName { get; set; } = string.Empty;
        public string GuildEmblem { get; set; } = string.Empty;
        public int? Life { get; set; }
        public int? ActionPoints { get; set; }
        public int? MovementPoints { get; set; }
        public int? Team { get; set; }

        public override string DisplayName => string.IsNullOrEmpty(GuildName) ? "Percepteur" : "Percepteur de " + GuildName;

        internal void SetNameIds(string raw)
        {
            NameIdsRaw = raw ?? string.Empty;
            string[] parts = NameIdsRaw.Split(',');
            FirstNameId = ParseBase36(parts[0]);
            LastNameId = parts.Length > 1 ? ParseBase36(parts[1]) : 0;
        }

        private static int ParseBase36(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 6) return 0;
            int result = 0;
            foreach (char character in value.ToLower(CultureInfo.InvariantCulture))
            {
                int digit = character >= '0' && character <= '9' ? character - '0' : character >= 'a' && character <= 'z' ? character - 'a' + 10 : -1;
                if (digit < 0) return 0;
                result = result * 36 + digit;
            }
            return result;
        }
    }
}
