using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tool_BotProtocol.Utils.Logger
{
    public  class LogsMessages
    {
        public string reference { get; }
        public string message { get; }
        public Exception exception { get; }
        public bool es_Exception => exception != null;

        public LogsMessages(string _reference, string _message, Exception _exception)
        {
            reference = _reference;
            message = _message;
            exception = _exception;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            string _reference = string.IsNullOrEmpty(reference) ? "" : $"({reference})";
            sb.Append($"[{DateTime.Now.ToString("HH:mm")}] {_reference} {message}");

            if (es_Exception)
                sb.Append($"{Environment.NewLine}- An exception has occured : {exception}");

            return sb.ToString();
        }
    }
}
