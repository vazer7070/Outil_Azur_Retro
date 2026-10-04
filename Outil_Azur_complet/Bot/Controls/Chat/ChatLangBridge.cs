using System;
using System.Globalization;
using System.Threading;
using Tool_BotProtocol.Game.Chat;
using Tool_BotProtocol.Game.Data;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>
    /// Textes d'interface du chat : clé de <c>lang_fr</c> (<see cref="LangData.Text"/>) quand le fichier de langue est
    /// chargé, sinon une formulation propre au bot. Ne lève jamais d'exception.
    /// </summary>
    internal static class ChatUiText
    {
        internal static string Get(string key, string fallback)
        {
            try
            {
                if (LangData.Text.Has(key))
                {
                    string value = LangData.Text.Plain(LangData.Text.Get(key)).Trim();
                    if (value.Length > 0 && !(value.StartsWith("!", StringComparison.Ordinal) && value.EndsWith("!", StringComparison.Ordinal)))
                        return value;
                }
            }
            catch (Exception) { /* Fichier de langue absent ou illisible : formulation du bot. */ }
            return fallback;
        }
    }

    /// <summary>
    /// Branche les textes du chat de la couche protocole (<see cref="ChatTexts"/>, lot C1) sur les fichiers de langue du
    /// client (<see cref="LangData"/>, lot D4) : noms et raccourcis des émotes (<c>emotes_fr</c>), nombre de smileys et
    /// messages <c>M1</c> (<c>SRV_MSG_&lt;id&gt;</c>). Un résolveur déjà fourni (par exemple par un test) est conservé ;
    /// sans fichier de langue chargé, chaque résolveur renvoie null et le chat garde ses numéros.
    /// </summary>
    public static class ChatLangBridge
    {
        private static int installed;

        public static void Install()
        {
            if (Interlocked.Exchange(ref installed, 1) == 1) return;
            if (ChatTexts.EmoteName == null)
                ChatTexts.EmoteName = id => LangData.IsLoaded("emotes") && LangData.Raw("emotes", "emote", id.ToString(CultureInfo.InvariantCulture)) != null
                    ? LangData.Emote.Name(id) : null;
            if (ChatTexts.EmoteShortcut == null)
                ChatTexts.EmoteShortcut = command => LangData.IsLoaded("emotes") ? LangData.Emote.IdFromCommand(command) : null;
            if (ChatTexts.SmileyCount == null)
                ChatTexts.SmileyCount = () => LangData.Smiley.Count;
            if (ChatTexts.ServerPopup == null)
                ChatTexts.ServerPopup = (id, args) =>
                {
                    string key = "SRV_MSG_" + id.ToString(CultureInfo.InvariantCulture);
                    return LangData.Text.Has(key) ? LangData.Text.Get(key, args) : null;
                };
        }
    }
}
