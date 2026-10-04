using System;
using System.Globalization;
using Tool_BotProtocol.Game.Actions;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Textes d'interface du chat : clé de <c>lang_fr</c> (<see cref="LangData.Text"/>) quand le fichier de langue est
    /// chargé, sinon une formulation propre au bot (paramètres <c>%1</c>…<c>%n</c>). Ne lève jamais d'exception.
    /// </summary>
    internal static class ChatUiText
    {
        internal static string Get(string key, string fallback, params string[] args)
        {
            try
            {
                string value = MapActionTexts.Get(key, fallback, args);
                if (!string.IsNullOrEmpty(value) && !(value.StartsWith("!", StringComparison.Ordinal) && value.EndsWith("!", StringComparison.Ordinal) && value.Length > 2))
                    return value;
            }
            catch (Exception) { /* Fichier de langue absent ou illisible : formulation du bot. */ }
            string text = fallback ?? key ?? string.Empty;
            if (args != null)
                for (int i = 0; i < args.Length; i++) text = text.Replace("%" + (i + 1).ToString(CultureInfo.InvariantCulture), args[i] ?? string.Empty);
            return text;
        }
    }

    /// <summary>
    /// Branche les textes du chat de la couche protocole (<see cref="ChatTexts"/>, lot C1) sur les fichiers de langue du
    /// client (<see cref="LangData"/>, lot D4) : noms et raccourcis des émotes (<c>emotes_fr</c>), nombre de smileys et
    /// messages <c>M1</c> (<c>SRV_MSG_&lt;id&gt;</c>). Chaque résolveur n'est posé que lorsque sa famille est chargée : sans
    /// <c>emotes.xml</c>, <see cref="ChatTexts.EmoteName"/> reste nul et la liste <c>eL</c> garde toutes ses émotes (le
    /// protocole écarte une émote dont le nom manque dès qu'un résolveur existe). Un résolveur déjà fourni (par exemple par
    /// un test) est conservé. L'appel est répété sans risque (création du volet, rafraîchissement).
    /// </summary>
    public static class ChatLangBridge
    {
        private static readonly object sync = new object();

        public static void Install()
        {
            try
            {
                lock (sync)
                {
                    if (LangData.IsLoaded("emotes"))
                    {
                        if (ChatTexts.EmoteName == null)
                            ChatTexts.EmoteName = id => LangData.Raw("emotes", "emote", id.ToString(CultureInfo.InvariantCulture)) != null
                                ? LangData.Emote.Name(id) : null;
                        if (ChatTexts.EmoteShortcut == null)
                            ChatTexts.EmoteShortcut = command => LangData.Emote.IdFromCommand(command);
                    }
                    if (ChatTexts.SmileyCount == null)
                        ChatTexts.SmileyCount = () => LangData.Smiley.Count;
                    if (LangData.IsLoaded("lang") && ChatTexts.ServerPopup == null)
                        ChatTexts.ServerPopup = (id, args) =>
                        {
                            string key = "SRV_MSG_" + id.ToString(CultureInfo.InvariantCulture);
                            return LangData.Text.Has(key) ? LangData.Text.Get(key, args) : null;
                        };
                }
            }
            catch (Exception) { /* Données de langue indisponibles : le chat garde ses numéros. */ }
        }
    }
}
