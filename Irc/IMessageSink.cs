using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HeadlessSlClient.Irc
{
    interface IMessageSink
    {
        void Send(Message msg);
        void Send(IEnumerable<Message> messages);

        void Send(string sender, string command, params string[] argv);
        void Send(string sender, Numeric numeric, params string[] argv);

        void SendFromClient(string command, params string[] argv);
        void SendFromServer(string command, params string[] argv);
        void SendFromServer(Numeric numeric, params string[] argv);

        void SendNumeric(Numeric numeric, params string[] argv);
        void SendNeedMoreParams(string command);

        string ClientNick { get; }
    }

    interface IRawMessageHandler
    {
        IEnumerable<string> SupportedMessages { get; }
        IEnumerable<string> SupportTokens { get; }
        IEnumerable<string> Caps { get; }

        void HandleMessage(Message msg);
    }
}
