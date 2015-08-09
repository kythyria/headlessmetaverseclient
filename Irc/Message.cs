using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient.Irc
{
    class Message
    {
        private string command;

        public Dictionary<string, string> Tags { get; set; }
        public string Sender { get; set; }
        public string Command { get { return command; } set { command = value.ToUpperInvariant(); } }
        public List<string> Argv { get; set; }

        public Message()
        {
            Tags = new Dictionary<string,string>();
            Argv = new List<string>();
            Sender = "";
        }

        public Message(string sender, string command, params string[] argv) : this()
        {
            Sender = sender;
            Command = command.ToUpper();
            Argv.AddRange(argv);
        }

        public Message(string sender, Numeric command, params string[] argv) : this(sender, command.ToString("000"), argv)
        {

        }
    }
}
