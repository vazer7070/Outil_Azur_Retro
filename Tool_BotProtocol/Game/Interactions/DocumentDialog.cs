using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Tool_BotProtocol.Game.Accounts;

namespace Tool_BotProtocol.Game.Interactions
{
    /// <summary>Chapitre d'un livre : titre, page de texte (index dans <see cref="BotDocument.Pages"/>), options du client.</summary>
    public sealed class DocumentChapter
    {
        public string Title { get; internal set; }
        public int PageIndex { get; internal set; }
        /// <summary>Le chapitre commence sur une page de droite (le client insère une page blanche).</summary>
        public bool StartsOnRightPage { get; internal set; }
        /// <summary>Le titre du chapitre est répété en tête de sa page.</summary>
        public bool ShowsTitle { get; internal set; }
    }

    /// <summary>
    /// Document du client (<c>data/docs/&lt;langue&gt;_&lt;id&gt;_&lt;date&gt;.swf</c>) exporté par <c>tools/client-analysis/docs2xml.py</c>
    /// dans <c>ressources/Bot/BotDocs/&lt;id&gt;_&lt;date&gt;.xml</c> : type (<c>book</c>, <c>parchment</c>, <c>roadsignleft</c>,
    /// <c>roadsignright</c>), titre, sous-titre, auteur, chapitres et pages en HTML simplifié.
    /// </summary>
    public sealed class BotDocument
    {
        private static readonly string[] NoPages = new string[0];

        public string Key { get; private set; }
        public int Id { get; private set; }
        public string Version { get; private set; }
        public string Type { get; private set; } = "book";
        public int Style { get; private set; }
        public string Title { get; private set; } = string.Empty;
        public string Subtitle { get; private set; } = string.Empty;
        public string Author { get; private set; } = string.Empty;
        public IReadOnlyList<string> Pages { get; private set; } = NoPages;
        public IReadOnlyList<DocumentChapter> Chapters { get; private set; } = new DocumentChapter[0];
        /// <summary>Livre (pages tournées deux par deux) ; les autres types n'ont qu'une page, comme dans le client.</summary>
        public bool IsBook => Type == "book";

        /// <summary>Lit l'élément racine <c>BotDoc</c> ; lève <see cref="FormatException"/> s'il est illisible.</summary>
        public static BotDocument Parse(XElement root)
        {
            if (root == null || root.Name.LocalName != "BotDoc") throw new FormatException("Élément BotDoc attendu.");
            if (!int.TryParse((string)root.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
                throw new FormatException("Identifiant de document invalide.");
            string version = (string)root.Attribute("date") ?? string.Empty;
            string type = (string)root.Attribute("type") ?? "book";
            int.TryParse((string)root.Attribute("style"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int style);
            string[] pages = root.Elements("page").Select(page => page.Value).ToArray();
            if (type != "book" && pages.Length > 1) pages = new[] { pages[0] };
            var chapters = new List<DocumentChapter>();
            foreach (XElement chapter in root.Elements("chapitre"))
            {
                if (!int.TryParse((string)chapter.Attribute("page"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int page)) continue;
                chapters.Add(new DocumentChapter
                {
                    Title = (string)chapter.Attribute("titre") ?? string.Empty, PageIndex = page,
                    StartsOnRightPage = (string)chapter.Attribute("droite") == "true", ShowsTitle = (string)chapter.Attribute("titreVisible") == "true"
                });
            }
            return new BotDocument
            {
                Key = id.ToString(CultureInfo.InvariantCulture) + (version.Length == 0 ? string.Empty : "_" + version),
                Id = id, Version = version, Type = type, Style = style,
                Title = ((string)root.Element("titre") ?? string.Empty).Trim(),
                Subtitle = ((string)root.Element("soustitre") ?? string.Empty).Trim(),
                Author = ((string)root.Element("auteur") ?? string.Empty).Trim(),
                Pages = pages, Chapters = chapters
            };
        }

        /// <summary>Paragraphes de la page (HTML simplifié), chapitre éventuel en tête comme dans <c>Document.initialize</c>.</summary>
        public IReadOnlyList<DocumentParagraph> PageParagraphs(int index)
        {
            if (index < 0 || index >= Pages.Count) return new DocumentParagraph[0];
            var paragraphs = new List<DocumentParagraph>();
            DocumentChapter chapter = Chapters.FirstOrDefault(entry => entry.PageIndex == index && entry.ShowsTitle);
            if (chapter != null) paragraphs.Add(new DocumentParagraph("chapter", new[] { new DocumentRun(chapter.Title, bold: true) }));
            paragraphs.AddRange(DocumentText.Parse(Pages[index]));
            return paragraphs;
        }
    }

    /// <summary>Morceau de texte d'un paragraphe de document.</summary>
    public sealed class DocumentRun
    {
        public DocumentRun(string text, bool bold = false, bool italic = false, bool underline = false, bool link = false, bool image = false)
        {
            Text = text ?? string.Empty; Bold = bold; Italic = italic; Underline = underline; IsLink = link; IsImage = image;
        }
        public string Text { get; }
        public bool Bold { get; }
        public bool Italic { get; }
        public bool Underline { get; }
        /// <summary>Lien du client (<c>asfunction:onHref,…</c>) : affiché, jamais exécuté.</summary>
        public bool IsLink { get; }
        /// <summary>Illustration (<c>&lt;img&gt;</c>) remplacée par une mention : les images des documents ne sont pas exportées.</summary>
        public bool IsImage { get; }
    }

    /// <summary>Paragraphe (<c>&lt;p class='…'&gt;</c>) : classe CSS du client (n, m, s, t1, t2, chapter) et morceaux de texte.</summary>
    public sealed class DocumentParagraph
    {
        public DocumentParagraph(string cssClass, IEnumerable<DocumentRun> runs) { Class = cssClass ?? "n"; Runs = (runs ?? Enumerable.Empty<DocumentRun>()).ToArray(); }
        public string Class { get; }
        public IReadOnlyList<DocumentRun> Runs { get; }
        public string PlainText => string.Concat(Runs.Select(run => run.Text));
        public bool IsTitle => Class == "t1" || Class == "t2" || Class == "chapter";
    }

    /// <summary>
    /// HTML des documents réduit à des paragraphes : <c>p</c>, <c>br</c>, <c>b</c>/<c>strong</c>, <c>i</c>/<c>em</c>, <c>u</c>,
    /// <c>a</c> et <c>img</c> ; les autres balises sont ignorées et les entités décodées. Tolère un HTML mal fermé.
    /// </summary>
    public static class DocumentText
    {
        public const string ImagePlaceholder = "[illustration]";
        private static readonly Regex Token = new Regex("<(/?)([A-Za-z][A-Za-z0-9]*)([^>]*)>|([^<]+)|<", RegexOptions.CultureInvariant);
        private static readonly Regex ClassAttribute = new Regex("class\\s*=\\s*['\"]([^'\"]*)['\"]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static IReadOnlyList<DocumentParagraph> Parse(string html)
        {
            var paragraphs = new List<DocumentParagraph>();
            var runs = new List<DocumentRun>();
            string cssClass = "n";
            int bold = 0, italic = 0, underline = 0, link = 0;
            void Flush()
            {
                // Paragraphe vide (souvent seulement une image alignée) : gardé s'il porte une illustration.
                if (runs.Any(run => run.IsImage || run.Text.Trim().Length > 0)) paragraphs.Add(new DocumentParagraph(cssClass, TrimEnd(runs)));
                runs = new List<DocumentRun>();
            }
            foreach (Match match in Token.Matches(html ?? string.Empty))
            {
                if (match.Groups[4].Success)
                {
                    string text = WebUtility.HtmlDecode(match.Groups[4].Value).Replace("\r", string.Empty).Replace("\n", " ");
                    if (text.Length > 0) runs.Add(new DocumentRun(text, bold > 0, italic > 0, underline > 0, link > 0));
                    continue;
                }
                if (!match.Groups[2].Success) { runs.Add(new DocumentRun("<", bold > 0, italic > 0, underline > 0, link > 0)); continue; }
                bool closing = match.Groups[1].Value == "/";
                string tag = match.Groups[2].Value.ToLowerInvariant();
                int delta = closing ? -1 : 1;
                switch (tag)
                {
                    case "p":
                        Flush();
                        if (!closing)
                        {
                            Match css = ClassAttribute.Match(match.Groups[3].Value);
                            cssClass = css.Success ? css.Groups[1].Value : "n";
                        }
                        else cssClass = "n";
                        break;
                    case "br": runs.Add(new DocumentRun("\n")); break;
                    case "b": case "strong": bold = Math.Max(0, bold + delta); break;
                    case "i": case "em": italic = Math.Max(0, italic + delta); break;
                    case "u": underline = Math.Max(0, underline + delta); break;
                    case "a": link = Math.Max(0, link + delta); break;
                    case "img": if (!closing) runs.Add(new DocumentRun(ImagePlaceholder, image: true)); break;
                }
            }
            Flush();
            return paragraphs;
        }

        private static IEnumerable<DocumentRun> TrimEnd(List<DocumentRun> runs)
        {
            int last = runs.Count;
            while (last > 0 && runs[last - 1].Text == "\n") last--;
            return runs.Take(last);
        }
    }

    /// <summary>
    /// Fenêtre de document selon <c>dofus.aks.Documents</c> : <c>dCK&lt;id&gt;_&lt;date&gt;</c> ouvre le document (StarLoco l'envoie
    /// quand le personnage s'arrête sur la cellule d'une pancarte, <c>InteractiveObject.getSignIO</c>), <c>dV</c> le ferme ;
    /// le bot envoie <c>dV</c>. Le document est lu dans <see cref="DocumentsPath"/> sur le thread réseau, jamais sur celui de l'interface.
    /// </summary>
    public sealed class DocumentDialog : InteractionWindow
    {
        private const long MaxDocumentBytes = 1024 * 1024;
        /// <summary>Clé reçue : « &lt;id&gt;_&lt;date&gt; » chez StarLoco ; « &lt;id&gt; » seul est toléré (dernière version exportée).</summary>
        private static readonly Regex KeyFormat = new Regex("^([0-9]{1,9})(?:_([0-9]{1,14}))?$", RegexOptions.CultureInvariant);
        private static readonly Regex FileFormat = new Regex("^([0-9]{1,9})_([0-9]{1,14})$", RegexOptions.CultureInvariant);

        public static string DefaultPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ressources", "Bot", "BotDocs");
        /// <summary>Dossier des documents exportés (un test y met ses propres fichiers).</summary>
        public string DocumentsPath { get; set; } = DefaultPath;
        /// <summary>Clé reçue dans <c>dCK</c> (« &lt;id&gt;_&lt;date&gt;. »), vide sans fenêtre.</summary>
        public string RequestedKey { get; private set; } = string.Empty;
        /// <summary>Document affiché, ou <c>null</c> s'il est introuvable (la fenêtre reste ouverte pour pouvoir envoyer <c>dV</c>).</summary>
        public BotDocument Current { get; private set; }

        protected override string Reference => "DOCUMENT";
        protected override AccountStates OpenState => AccountStates.DIALOG;

        internal DocumentDialog(Accounts.Accounts account) : base(account) { }

        /// <summary>Fermeture : <c>dV</c> ; le serveur répond par <c>dV</c>.</summary>
        public Task<InteractionResult> LeaveAsync()
        {
            if (!IsOpen) return Task.FromResult(Refuse("Aucun document n'est ouvert."));
            return SendAsync("dV", "Fermeture du document demandée.");
        }

        /// <summary>
        /// Cherche <c>&lt;id&gt;_&lt;date&gt;.xml</c>, sinon la version la plus récente du même numéro (tri sur la date
        /// <c>aammjjhhmm</c> du nom). La clé reçue du serveur n'est jamais utilisée comme chemin sans être validée
        /// (chiffres et « _ » seulement).
        /// </summary>
        public static BotDocument Load(string folder, string key, out string message)
        {
            message = null;
            Match parts = KeyFormat.Match(key ?? string.Empty);
            if (!parts.Success) { message = "Identifiant de document invalide : " + key; return null; }
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { message = "Dossier des documents introuvable : " + folder; return null; }
            try
            {
                string file = Path.Combine(folder, key + ".xml");
                if (!File.Exists(file))
                {
                    file = Directory.EnumerateFiles(folder, parts.Groups[1].Value + "_*.xml")
                        .Where(path => FileFormat.IsMatch(Path.GetFileNameWithoutExtension(path)))
                        .OrderByDescending(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal).FirstOrDefault();
                    if (file == null) { message = "Document " + key + " absent des ressources du bot."; return null; }
                    if (parts.Groups[2].Success) message = "Version " + Path.GetFileNameWithoutExtension(file) + " affichée à la place de " + key + ".";
                }
                if (new FileInfo(file).Length > MaxDocumentBytes) { message = "Document " + key + " trop volumineux."; return null; }
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(file, settings)) return BotDocument.Parse(XElement.Load(reader));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is XmlException || error is FormatException)
            {
                message = "Document " + key + " illisible : " + error.Message;
                return null;
            }
        }

        /// <summary><c>dCK&lt;id&gt;_&lt;date&gt;</c> (sans « dCK »).</summary>
        internal void OnCreate(string key)
        {
            string requested = (key ?? string.Empty).Trim();
            BotDocument document = Load(DocumentsPath, requested, out string message);
            RequestedKey = requested;
            Current = document;
            MarkOpen();
            if (document == null) LogError(message ?? "Document introuvable.");
            else Log(message ?? "Document « " + (document.Title.Length > 0 ? document.Title : document.Key) + " » ouvert.");
            Notify();
        }

        /// <summary><c>dV</c> : fermeture.</summary>
        internal void OnLeave()
        {
            bool wasOpen = IsOpen;
            Reset();
            MarkClosed();
            if (wasOpen) Log("Document fermé.");
            Notify();
        }

        protected override void Reset()
        {
            RequestedKey = string.Empty;
            Current = null;
        }
    }
}
