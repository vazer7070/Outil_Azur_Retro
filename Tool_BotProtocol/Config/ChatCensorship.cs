using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Tool_BotProtocol.Game.Data;

namespace Tool_BotProtocol.Config
{
    /// <summary>
    /// Filtre des mots insultants des messages reçus, comme <c>ChatManager.applyInputCensorship</c> du client : actif avec l'option
    /// <c>CensorshipFilter</c> et le réglage <c>CENSORSHIP_ENABLE_INPUT</c> de <c>lang</c> ; dictionnaire = table <c>CSR</c> de
    /// <c>lang.xml</c> (mot <c>c</c>, poids <c>l</c>, recherche dans les mots <c>d</c>).
    /// Chaque mot du message, mis en majuscules et réduit aux lettres A-Z, chiffres et espaces, est comparé au dictionnaire ;
    /// un mot trouvé est remplacé par sa forme en majuscules dont chaque lettre ou chiffre devient un caractère de
    /// <c>CENSORSHIP_CHAR</c> tiré au hasard, jamais deux fois le même de suite.
    /// </summary>
    public sealed class ChatCensorship
    {
        private static readonly char[] CensorshipChars = { '%', '&', '§', '@', '?' };
        private readonly HashSet<string> words;
        private readonly string[] inWord;
        private readonly Random random;

        public ChatCensorship(IEnumerable<string> words, IEnumerable<string> inWordEntries = null, Random random = null)
        {
            this.words = new HashSet<string>((words ?? new string[0]).Where(w => !string.IsNullOrEmpty(w)).Select(w => w.ToUpperInvariant()), StringComparer.Ordinal);
            inWord = (inWordEntries ?? new string[0]).Where(w => !string.IsNullOrEmpty(w)).Select(w => w.ToUpperInvariant()).ToArray();
            this.random = random ?? new Random();
        }

        /// <summary>Nombre de mots du dictionnaire.</summary>
        public int Count => words.Count;

        /// <summary>
        /// Dictionnaire des textes du client chargés (<see cref="LangData"/>) ; <c>null</c> si <c>lang.xml</c> manque ou si le
        /// réglage <c>CENSORSHIP_ENABLE_INPUT</c> n'est pas « true ».
        /// </summary>
        public static ChatCensorship FromLang(Random random = null)
        {
            if (!LangData.IsLoaded("lang")) return null;
            IReadOnlyDictionary<string, string> enabled = LangData.Raw("lang", "config", "CENSORSHIP_ENABLE_INPUT");
            if (enabled == null || !enabled.TryGetValue("valeur", out string flag) || !string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase)) return null;
            var list = new List<string>(); var parsed = new List<string>();
            foreach (string id in LangData.Ids("lang", "CSR"))
            {
                IReadOnlyDictionary<string, string> entry = LangData.Raw("lang", "CSR", id);
                if (entry == null || !entry.TryGetValue("c", out string word) || string.IsNullOrEmpty(word)) continue;
                list.Add(word);
                if (entry.TryGetValue("d", out string inside) && string.Equals(inside, "true", StringComparison.OrdinalIgnoreCase)) parsed.Add(word);
            }
            return list.Count == 0 ? null : new ChatCensorship(list, parsed, random);
        }

        /// <summary>Texte filtré ; les espaces d'origine sont gardés, seuls les mots trouvés changent.</summary>
        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(text) || words.Count == 0) return text;
            string[] original = text.Split(' ');
            string[] simplified = AvoidPunctuation(text.ToUpperInvariant()).Split(' ');
            // Le texte simplifié perd des caractères mais aucun espace : les deux découpages ont autant de mots.
            if (simplified.Length != original.Length) return text;
            var result = new string[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                string word = simplified[i];
                bool censored = word.Length > 0 && (words.Contains(word) || inWord.Any(entry => word.IndexOf(entry, StringComparison.Ordinal) >= 0));
                result[i] = censored ? CensoredWord(word) : original[i];
            }
            return string.Join(" ", result);
        }

        /// <summary><c>avoidPonctuation</c> : ne garde que A-Z, 0-9 et l'espace (le texte est déjà en majuscules).</summary>
        public static string AvoidPunctuation(string text)
        {
            var builder = new StringBuilder(text?.Length ?? 0);
            foreach (char c in text ?? string.Empty)
                if ((c > 64 && c < 91) || (c > 47 && c < 58) || c == ' ') builder.Append(c);
            return builder.ToString();
        }

        private string CensoredWord(string word)
        {
            var builder = new StringBuilder(word.Length);
            char previous = '\0';
            foreach (char c in word)
            {
                if ((c > 47 && c < 58) || (c > 64 && c < 91) || (c > 96 && c < 123))
                {
                    char next;
                    lock (random) do next = CensorshipChars[random.Next(CensorshipChars.Length)]; while (next == previous);
                    previous = next;
                    builder.Append(next);
                }
                else { previous = c; builder.Append(c); }
            }
            return builder.ToString();
        }

        public override string ToString() => Count.ToString(CultureInfo.InvariantCulture) + " mot(s)";
    }
}
