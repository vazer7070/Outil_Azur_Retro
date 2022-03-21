using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tool_BotProtocol.Config;

namespace Tool_BotProtocol.Utils.Logger
{
    public class Logger
    {
        public event Action<LogsMessages, string> log_event;

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
        public void log_normal(string reference, string message) => log_Final(reference, message, LogTypes.NORMAL);
        public void log_privado(string reference, string message) => log_Final(reference, message, LogTypes.PRIVATE);
    }
}
