using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Tool_BotProtocol.Game.Chat;

namespace Outil_Azur_complet.Bot.Controls.Chat
{
    /// <summary>Touche d'envoi du client : Entrée, Maj + Entrée (équipe), Ctrl + Entrée (guilde).</summary>
    public enum ChatSendMode
    {
        Normal,
        /// <summary><c>TEAM_MESSAGE</c> : la ligne part dans <c>/t</c>.</summary>
        Team,
        /// <summary><c>GUILD_MESSAGE</c> : la ligne part dans <c>/g</c>.</summary>
        Guild,
    }

    /// <summary>Ligne validée dans la saisie du chat.</summary>
    public sealed class ChatSubmitEventArgs : EventArgs
    {
        public ChatSubmitEventArgs(ChatSendMode mode) { Mode = mode; }
        public ChatSendMode Mode { get; }
    }

    /// <summary>
    /// Saisie du chat (<c>_tiInput</c> de la console du client) : 250 caractères tapés au plus (200 envoyés,
    /// <c>MAX_MESSAGE_LENGTH</c> + marge), préfixe de canal choisi dans le menu des canaux, raccourcis du client :
    /// Entrée, Maj + Entrée (équipe), Ctrl + Entrée (guilde), Haut/Bas (historique), Maj + Haut/Bas (chuchotements reçus),
    /// Ctrl + Droite (complétion du nom d'un joueur présent). Ne fait aucun envoi : <see cref="ChatPanel"/> lit
    /// <see cref="Submitted"/> et transmet la ligne au service de chat.
    /// </summary>
    public sealed class ChatInput : TextBox
    {
        public ChatInput()
        {
            MaxLength = ChatService.MaxInputLength;
            BorderStyle = BorderStyle.FixedSingle; BackColor = BotUi.PaperLight; ForeColor = BotUi.Ink;
            Font = BotFonts.Get(8.25f); Multiline = false; WordWrap = false;
            AccessibleName = "Message du chat";
        }

        /// <summary>Préfixe du canal choisi (« /g », « /t »…) ; vide ou « /s » pour le canal général.</summary>
        public string Prefix { get; set; } = string.Empty;
        /// <summary>Historique de saisie (touches Haut/Bas).</summary>
        public ChatHistory History { get; set; }
        /// <summary>Chuchotements reçus (Maj + Haut/Bas) : « /w nom ».</summary>
        public ChatHistory WhisperHistory { get; set; }
        /// <summary>Noms des joueurs présents pour Ctrl + Droite.</summary>
        public Func<IEnumerable<string>> Names { get; set; }

        public event EventHandler<ChatSubmitEventArgs> Submitted;
        /// <summary>Message local à afficher (complétion sans résultat, liste des noms possibles).</summary>
        public event Action<string, bool> Notice;

        /// <summary>
        /// Ligne envoyée au service pour un texte et un mode, comme <c>getChatCommand</c> puis <c>TEAM_MESSAGE</c> /
        /// <c>GUILD_MESSAGE</c> du client. Chaîne vide s'il n'y a rien à envoyer.
        /// </summary>
        public static string BuildLine(string text, string prefix, ChatSendMode mode)
        {
            text = (text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ");
            if (text.Trim().Length == 0) return string.Empty;
            string command = text[0] == '/' ? text : string.IsNullOrEmpty(prefix) || prefix == "/s" ? text : prefix + " " + text;
            if (mode == ChatSendMode.Normal) return command;
            if (command[0] == '/')
            {
                int space = command.IndexOf(' ');
                command = space < 0 ? string.Empty : command.Substring(space + 1);
            }
            if (command.Trim().Length == 0) return string.Empty;
            return (mode == ChatSendMode.Team ? "/t " : "/g ") + command;
        }

        /// <summary>Texte du message dans une ligne (après « /g », après « /w nom ») ; la ligne entière sans commande.</summary>
        public static string MessageBody(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '/') return line ?? string.Empty;
            int space = line.IndexOf(' ');
            if (space < 0) return string.Empty;
            string command = line.Substring(1, space - 1).ToUpperInvariant();
            string rest = line.Substring(space + 1);
            if (command == "W" || command == "MSG" || command == "WHISPER")
            {
                rest = rest.TrimStart(' ');
                int next = rest.IndexOf(' ');
                return next < 0 ? string.Empty : rest.Substring(next + 1);
            }
            return rest;
        }

        /// <summary>Remplace le texte et place le curseur à la fin.</summary>
        public void SetText(string text)
        {
            Text = text ?? string.Empty;
            SelectionStart = Text.Length; SelectionLength = 0;
        }

        /// <summary>
        /// Raccourcis du client pour une touche (aussi utilisé par les tests) ; vrai si la touche a été traitée.
        /// </summary>
        public bool HandleKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) == Keys.Shift, control = (keyData & Keys.Control) == Keys.Control;
            switch (key)
            {
                case Keys.Enter:
                    if (shift && control) return true;
                    Raise(shift ? ChatSendMode.Team : control ? ChatSendMode.Guild : ChatSendMode.Normal);
                    return true;
                case Keys.Up:
                case Keys.Down:
                    if (control) return false;
                    ChatHistory history = shift ? WhisperHistory : History;
                    if (history == null) return true;
                    SetText(key == Keys.Up ? history.Up() : history.Down());
                    return true;
                case Keys.Right:
                    if (!control || shift) return false;
                    Complete();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// <c>autoCompletion</c> du client : complète le dernier mot avec les noms des joueurs présents. Un seul nom : il
        /// remplace le mot, suivi d'une espace ; plusieurs : leur début commun, et la liste est affichée ; aucun : message.
        /// </summary>
        public void Complete()
        {
            string text = Text ?? string.Empty;
            int start = text.LastIndexOf(' ') + 1;
            string word = text.Substring(start);
            if (word.Length == 0) return;
            List<string> names;
            try { names = (Names?.Invoke() ?? Enumerable.Empty<string>()).Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
            catch (Exception) { names = new List<string>(); }
            List<string> matches = names.Where(name => name.StartsWith(word, StringComparison.OrdinalIgnoreCase)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            if (matches.Count == 0) { Notice?.Invoke("Aucun joueur présent dont le nom commence par « " + word + " ».", true); return; }
            if (matches.Count == 1) { SetText(text.Substring(0, start) + matches[0] + " "); return; }
            string common = matches[0];
            foreach (string name in matches.Skip(1))
            {
                int length = 0;
                while (length < common.Length && length < name.Length && char.ToLowerInvariant(common[length]) == char.ToLowerInvariant(name[length])) length++;
                common = common.Substring(0, length);
            }
            if (common.Length > word.Length) SetText(text.Substring(0, start) + common);
            Notice?.Invoke(string.Join(", ", matches), false);
        }

        private void Raise(ChatSendMode mode)
        {
            try { Submitted?.Invoke(this, new ChatSubmitEventArgs(mode)); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { System.Diagnostics.Trace.WriteLine("Chat : " + error.Message); }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Enter || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (HandleKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // Entrée ne doit jamais faire biper ni insérer de caractère dans la ligne.
            if (e.KeyChar == '\r' || e.KeyChar == '\n') { e.Handled = true; return; }
            base.OnKeyPress(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Submitted = null; Notice = null; Names = null; }
            base.Dispose(disposing);
        }
    }
}
