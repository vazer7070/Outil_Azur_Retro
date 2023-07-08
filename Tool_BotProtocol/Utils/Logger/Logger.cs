using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;

namespace Tool_BotProtocol.Utils.Logger
{
    public class Logger
    {
        public event Action<LogsMessages, string> log_event;
        public event Action<LogsMessages, string> log_eventChat;

        private void log_Final(string reference, string message, string color, Exception ex = null)
        {
            try
            {
                LogsMessages log_Message = new LogsMessages(reference, message, ex);
                log_event?.Invoke(log_Message, color);
            }
            catch (Exception e)
            {
                log_Final("LOGGER", "An error occured while registering the event", LogTypes.ERROR, e);
            }
        }
        private void log_Chats(string reference, string message, string color, Exception ex = null)
        {
            try
            {
                LogsMessages log_Message = new LogsMessages(reference, message, ex);
                log_eventChat?.Invoke(log_Message, color);
            }
            catch (Exception e)
            {
                log_Chat("LOGGER", "An error occured while registering the event", LogTypes.ERROR, e);
            }
        }
        private void log_Chat(string reference, string message, LogTypes color, Exception ex = null)
        {
            if (color == LogTypes.DEBUG)
                return;
            log_Chats(reference, message, ((int)color).ToString("X"), ex);
        }
        private void log_Final(string reference, string message, LogTypes color, Exception ex = null)
        {
            if (color == LogTypes.DEBUG)
                return;
            log_Final(reference, message, ((int)color).ToString("X"), ex);
        }

        public void LogError(string reference, string message) => log_Final(reference, message, LogTypes.ERROR);
        public void LogException(string reference, Exception e) => log_Final(reference, e.Message, LogTypes.ERROR);
        public void LogDanger(string reference, string message) => log_Final(reference, message, LogTypes.WARNING);
        public void LogInfo(string reference, string message) => log_Final(reference, message, LogTypes.INFORMATION);
        public void log_normal(string reference, string message) => log_Chat(reference, message, LogTypes.NORMAL);
        public void LogRecruitTchat(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATRECRUIT);
        public void LogCommerceTchat(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATCOMMERCE);
        public void LogAdminTchat(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATADMIN);
        public void LogTchatPrivate(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATPRIVATE);
        public void LogTchatGuild(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATGUILD);
        public void LogTchatGroupe(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATGROUP);
        public void LogTchatTeam(string reference, string message) => log_Chat(reference, message, LogTypes.TCHATTEAM);
    }
}
