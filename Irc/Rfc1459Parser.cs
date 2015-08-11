using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HeadlessMetaverseClient.Irc
{
    interface IMessageParser
    {
        Message Parse(string input);
        string Emit(Message msg);
    }

    class Rfc1459Parser : IMessageParser
    {
        Regex reSender = new Regex(":([^ ]*) ");
        Regex reWord = new Regex("([^ ]*)( |$)");
        Regex reArgv = new Regex(@"(?::(?<trailing>.*)$)|(?:(?<middle>[^ ]+)( +|$))");

        public Message Parse(string input)
        {
            var msg = new Message();
            var scanner = new Strscan(input);

            var m = scanner.Match(reSender);
            if (m != null)
            {
                msg.Sender = m.Groups[1].Value;
            }

            m = scanner.Match(reWord);
            msg.Command = m.Groups[1].Value.ToUpperInvariant();

            while(!scanner.AtEnd)
            {
                m = scanner.Match(reArgv);
                if (m.Groups["trailing"].Success)
                {
                    msg.Argv.Add(m.Groups["trailing"].Value);
                    break;
                }
                else
                {
                    msg.Argv.Add(m.Groups["middle"].Value);
                }
            }

            return msg;
        }

        public string Emit(Message msg)
        {
            if (msg.Tags.Count > 0) { throw new NotImplementedException(); }

            var output = new StringBuilder();
            if(!string.IsNullOrEmpty(msg.Sender))
            {
                output.Append(":").Append(msg.Sender).Append(" ");
            }
            output.Append(msg.Command);

            var argcopy = new Queue<string>(msg.Argv);
            while(argcopy.Count > 0)
            {
                output.Append(" ");
                if (argcopy.Count == 1)
                {
                    if(String.IsNullOrEmpty(argcopy.Peek()) || argcopy.Peek().Contains(" ") || argcopy.Peek().First() == ':')
                    {
                        output.Append(":");
                    }
                }
                output.Append(argcopy.Dequeue().Replace("\n", "\\n").Replace("\r","\\r"));
            }
            return output.ToString();
        }
    }

    class Strscan
    {
        int position = 0;
        string source;

        public Strscan(string input)
        {
            source = input;
        }

        public Match Match(Regex re)
        {
            var m = re.Match(source, position);
            if (m.Success && m.Index == position)
            {
                position += m.Length;
                return m;
            }
            return null;
        }
        public bool AtEnd { get { return source.Length == position; } }
    }
}
