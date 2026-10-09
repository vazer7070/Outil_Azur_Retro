using System.Windows.Forms;
using Tool_BotProtocol.Game;
using Tool_BotProtocol.Utils.Logger;

namespace Outil_Azur_complet.Bot.Panels
{
    /// <summary>Journal de session : messages du compte, identifiants et clés masqués par <c>BotPacketRedactor</c>.</summary>
    public sealed class JournalPanel : GamePanel
    {
        private RichTextBox journal;
        private Logger logger;

        public override string Title => "Journal";

        protected override Control CreateView()
        {
            var page = Page();
            journal = MakeJournal(); page.Controls.Add(journal);
            return page;
        }

        protected override void OnBind(GameClass game)
        {
            logger = Account?.Logger;
            if (logger != null) logger.log_event += OnLog;
        }
        protected override void OnUnbind(GameClass game)
        {
            if (logger != null) logger.log_event -= OnLog;
            logger = null;
        }

        private void OnLog(LogsMessages message, string color)
        {
            var account = Account;
            string text = BotPacketRedactor.Redact(message.ToString(), account);
            OnUi(() => { if (journal != null && !journal.IsDisposed) BotUi.Append(journal, text, color); });
        }
    }
}
