using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Tool_BotProtocol.Game.Session
{
    /// <summary>Famille d'un message <c>Im</c>, premier caractère après « Im » (client 1.34, <c>Infos.onMessage</c>).</summary>
    public enum ServerMessageKind
    {
        /// <summary>« 0 » : texte <c>INFOS_&lt;id&gt;</c>, canal d'information.</summary>
        Info = 0,
        /// <summary>« 1 » : texte <c>ERROR_&lt;id&gt;</c>, canal d'erreur.</summary>
        Error = 1,
        /// <summary>« 2 » : texte <c>PVP_&lt;id&gt;</c>, canal JcJ.</summary>
        Pvp = 2,
    }

    /// <summary>Une entrée d'un paquet <c>Im&lt;type&gt;&lt;id&gt;[;a~b~c][|&lt;id&gt;[;…]]</c>.</summary>
    public sealed class ServerMessage
    {
        public ServerMessageKind Kind { get; }
        /// <summary>Identifiant tel que reçu (« 152 » pour <c>Im0152</c>). Le client le lit comme un nombre quand il le peut.</summary>
        public string Id { get; }
        public int? NumericId { get; }
        /// <summary>Paramètres du deuxième champ, séparés par « ~ » ; les champs suivants sont ignorés comme dans le client.</summary>
        public IReadOnlyList<string> Args { get; }
        /// <summary>Texte affiché : texte de langue résolu, sinon le repli « Im&lt;type&gt;&lt;id&gt; » suivi des paramètres.</summary>
        public string Text { get; }
        /// <summary>Vrai lorsque <see cref="Text"/> vient des fichiers de langue (résolveur branché).</summary>
        public bool Resolved { get; }

        internal ServerMessage(ServerMessageKind kind, string id, string[] args, string text, bool resolved)
        {
            Kind = kind;
            Id = id;
            NumericId = int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : (int?)null;
            Args = Array.AsReadOnly(args);
            Text = text;
            Resolved = resolved;
        }

        /// <summary>Clé de langue que le client cherche : <c>INFOS_</c>, <c>ERROR_</c> ou <c>PVP_</c> + nombre, ou l'identifiant brut s'il n'est pas numérique.</summary>
        public string LangKey => NumericId.HasValue
            ? (Kind == ServerMessageKind.Info ? "INFOS_" : Kind == ServerMessageKind.Error ? "ERROR_" : "PVP_") + NumericId.Value.ToString(CultureInfo.InvariantCulture)
            : Id;

        public override string ToString() => Text;
    }

    /// <summary>Lecture des paquets <c>Im</c> selon <c>Infos.onMessage</c> du client 1.34 et résolution de leurs textes.</summary>
    public static class ServerMessages
    {
        /// <summary>
        /// Résolveur des textes <c>INFOS_/ERROR_/PVP_</c> des fichiers de langue (type 0/1/2, identifiant, paramètres) :
        /// renvoie le texte avec ses paramètres substitués, ou null s'il ne le connaît pas. Sans résolveur, ou pour un
        /// texte inconnu, le bot affiche le repli « Im&lt;type&gt;&lt;id&gt; » et les paramètres bruts. Aucun texte n'est codé en dur.
        /// </summary>
        public static Func<int, string, string[], string> Resolver { get; set; }

        /// <summary>
        /// Découpe un paquet <c>Im…</c> comme le client : le caractère après « Im » donne le type, le reste est découpé
        /// sur « | » ; chaque entrée est <c>&lt;id&gt;[;&lt;a~b~…&gt;]</c>. Une entrée sans identifiant ou un type autre que
        /// 0/1/2 est ignoré, comme le client. Ne lève jamais d'exception.
        /// </summary>
        public static IReadOnlyList<ServerMessage> Parse(string packet)
        {
            var messages = new List<ServerMessage>();
            if (packet == null || packet.Length < 4 || !packet.StartsWith("Im", StringComparison.Ordinal)) return messages;
            char type = packet[2];
            if (type < '0' || type > '2') return messages;
            var kind = (ServerMessageKind)(type - '0');
            foreach (string entry in packet.Substring(3).Split('|'))
            {
                string[] fields = entry.Split(';');
                string id = fields[0];
                if (id.Length == 0) continue;
                string[] args = fields.Length > 1 ? fields[1].Split('~') : new string[0];
                string resolved = Resolve(kind, id, args);
                messages.Add(new ServerMessage(kind, id, args, resolved ?? Fallback(type, id, args), resolved != null));
            }
            return messages;
        }

        /// <summary>Repli sans fichier de langue : « Im0152 : a ; b ».</summary>
        public static string Fallback(char type, string id, IEnumerable<string> args)
        {
            string text = "Im" + type + id;
            string[] values = (args ?? Enumerable.Empty<string>()).ToArray();
            return values.Length == 0 ? text : text + " : " + string.Join(" ; ", values);
        }

        private static string Resolve(ServerMessageKind kind, string id, string[] args)
        {
            Func<int, string, string[], string> resolver = Resolver;
            if (resolver == null) return null;
            try
            {
                string text = resolver((int)kind, id, (string[])args.Clone());
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (Exception)
            {
                // A missing or corrupt language file must never stop packet handling: the fallback is shown instead.
                return null;
            }
        }
    }
}
