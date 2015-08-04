using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient.Irc
{
    class ClientConnection
    {
        enum ConnectionState
        {
            REGISTRATION,
            CONNECTED,
            CLOSED
        }

        const string LOCALHOST = "local.sl";
        const string GROUPHOST = "group.grid.sl";

        const int RPL_TOPIC = 332;
        const int RPL_CHANNELMODEIS = 324;

        Socket connection;
        StreamWriter writer;
        StreamReader reader;
        Irc.IMessageParser parser;

        IUpstreamConnection upstream;
        IIdentityMapper mapper;

        ConnectionState state;
        string username = null;
        string password = null;
        bool userMsgSent;

        MappedIdentity selfId;
        Dictionary<string, IChannel> channels;

        public ClientConnection(Socket connection, IUpstreamConnection upstream, IIdentityMapper mapper)
        {
            this.connection = connection;
            this.upstream = upstream;
            this.mapper = mapper;
            this.channels = new Dictionary<string, IChannel>();

            upstream.ReceiveMessage += ReceiveUpstreamMessage;
            upstream.ChannelListLoaded += ChannelListLoaded;
        }

        public void Run()
        {
            state = ConnectionState.REGISTRATION;

            using(var stream = new NetworkStream(connection))
            using(reader = new StreamReader(stream, new UTF8Encoding(false)))
            using(writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                parser = new Irc.Rfc1459Parser();
                while (state != ConnectionState.CLOSED)
                {
                    string input;
                    try
                    {
                        input = reader.ReadLine();
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        break;
                    }

                    if(input == null)
                    {
                        Console.WriteLine("Client socket went away");
                        upstream.Disconnect();
                        break;
                    }
                    var msg = parser.Parse(input);
                    DispatchMessage(msg);
                }
            }
        }

        private void DispatchMessage(Message msg)
        {
            switch (state)
            {
                case ConnectionState.REGISTRATION:
                    OnRegistrationMessage(msg);
                    break;
                case ConnectionState.CONNECTED:
                    OnConnectedMessage(msg);
                    break;
            }
        }

        private void OnConnectedMessage(Message msg)
        {
            switch(msg.Command)
            {
                case "PING":
                    SendFromServer("PONG", msg.Argv.ToArray());
                    break;
                case "QUIT":
                    upstream.Disconnect();
                    state = ConnectionState.CLOSED;
                    break;
                case "JOIN":
                    break;
                case "PART":
                    if (msg.Argv.Count < 1)
                    {
                        SendFromServer(461, "PART", "Not enough parameters");
                        break;
                    }
                    SendFromClient("JOIN", msg.Argv[0]);
                    break;
                case "PRIVMSG":
                    OnPrivmsg(msg);
                    break;
                case "WHO":
                    OnWho(msg);
                    break;
                case "NAMES":
                    if (msg.Argv.Count < 1) { SendFromServer(461, "NAMES", "Not enough parameters"); break; }
                    OnNames(msg.Argv[0]);
                    break;
                case "MODE":
                    OnMode(msg);
                    break;
                case "TOPIC":
                    if (msg.Argv.Count < 1)
                    {
                        SendFromServer(461, "TOPIC", "Not enough parameters");
                        break;
                    }
                    if (msg.Argv.Count > 1)
                    {
                        SendFromServer(482, "You can't set topics");
                        break;
                    }
                    OnTopic(msg);
                    break;
                default:
                    SendFromServer("421", username, msg.Command, "Not implemented!");
                    break;
            }
        }

        private void OnPrivmsg(Message msg)
        {
            if (msg.Argv.Count < 2)
            {
                SendFromServer(461, "PRIVMSG", "Not enough parameters");
                return;
            }

            var outmsg = new IntermediateMessage();
            outmsg.Timestamp = DateTime.UtcNow;
            outmsg.Sender = selfId;

            if (msg.Argv[1].StartsWith("\x01" + "ACTION "))
            {
                outmsg.Payload = "/me " + msg.Argv[1].Substring(8).TrimEnd('\x01');
            }
            else { outmsg.Payload = msg.Argv[1]; }

            if (msg.Argv[0].StartsWith("#"))
            {
                outmsg.Type = MessageType.Talk;
                channels[msg.Argv[0]].SendMessage(outmsg);
            }
            else
            {
                outmsg.Type = MessageType.IM;
                upstream.SendMessageTo(mapper.MapUser(msg.Argv[0]), outmsg);
            }
        }

        private void OnTopic(Message msg)
        {
            SendFromServer(RPL_TOPIC, msg.Argv[0], channels[msg.Argv[0]].Topic);
        }

        private void OnMode(Message msg)
        {
            if (msg.Argv.Count < 1) { SendFromServer(461, username, "MODE", "Not enough parameters"); return; }
            if (!channels.ContainsKey(msg.Argv[0]))
            {
                SendFromServer(502, "Don't touch their modes!");
                return;
            }
            if (msg.Argv.Count > 1) {
                if (msg.Argv[1] == "+b")
                {
                    SendFromServer(368, username, msg.Argv[0], "End of ban list");
                }
                else
                {
                    SendFromServer(482, msg.Argv[0], username, "Chanop functions are not supported");
                }
                return;
            }

            SendFromServer(RPL_CHANNELMODEIS, "MODE", username, msg.Argv[0], "+t");
        }

        private void OnNames(string chan)
        {
            if (channels.ContainsKey(chan))
            {
                var namelist = channels[chan].Members;
                foreach (var i in namelist.ChunkTrivialBetter(512 / 63 - 1))
                {
                    var reply = new List<string>();
                    foreach (var j in i)
                    {
                        if (j.IsOperator)
                        {
                            reply.Add("@" + j.Subject.IrcNick);
                        }
                        else if (j.Position <= PositionCategory.Talk)
                        {
                            reply.Add("+" + j.Subject.IrcNick);
                        }
                        else
                        {
                            reply.Add(j.Subject.IrcNick);
                        }
                    }
                    SendFromServer(353, username, "=", chan, String.Join(" ", reply));
                }
            }
            SendFromServer(366, username, chan, "End of NAMES list");
        }

        private void OnWho(Message msg)
        {
            if (msg.Argv.Count < 1) { SendFromServer(461, "WHO", "Not enough parameters"); return; }

            var key = msg.Argv[0];
            if (channels.ContainsKey(key))
            {
                foreach(var i in channels[key].Members)
                {
                    var reply = new List<string>(new string[]{ username, key, "user", "agent.grid.sl"});
                    reply.Add(i.Subject.IrcNick);
                    if (i.IsOperator) { reply.Add("H@"); }
                    else if (i.Position <= PositionCategory.Talk) { reply.Add("H+");}
                    else { reply.Add("H"); }
                    SendFromServer(352, reply.ToArray());
                }
            }
            SendFromServer(315);
        }

        private void OnRegistrationMessage(Message msg)
        {
            switch(msg.Command)
            {
                case "USER":
                    userMsgSent = true;
                    break;
                case "PASS":
                    password = msg.Argv[0];
                    break;
                case "NICK":
                    username = msg.Argv[0];
                    break;
            }

            if (username != null && password != null && userMsgSent)
            {
                var names = username.Split('.');
                string firstname = names[0];
                string lastname = names.Count() == 1 ? "resident" : names[1];

                if (upstream.Connect(firstname, lastname, password))
                {
                    selfId = mapper.MapUser(username);
                    SendFromServer(1, username, string.Format("Welcome to the Headless SL Client {0}", selfId.IrcFullId));
                    SendFromServer(2, username, "Your host is sl.local, running HeadlessSlClient 0.1");
                    SendFromServer(5, username, "NICKLEN=63 MODES=,,,t PREFIX=(ov)@+");
                    state = ConnectionState.CONNECTED;
                }
            }
        }

        private void ChannelListLoaded(IEnumerable<IChannel> channels)
        {
            foreach (var i in channels)
            {
                if (!this.channels.ContainsKey(i.IrcName))
                {
                    RegisterChannelHandlers(i);
                    this.channels[i.IrcName] = i;
                    SendFromClient("JOIN", i.IrcName);
                    //SendFromServer(RPL_CHANNELMODEIS, i.IrcName, "+t");
                    SendFromServer(RPL_TOPIC, username, i.IrcName, i.Topic);
                    OnNames(i.IrcName);
                }
            }
        }

        private void RegisterChannelHandlers(IChannel i)
        {
            i.ReceiveMessage += ReceiveChannelMessage;
            i.MembersChanged += ChannelMembersChanged;
        }

        private void ChannelMembersChanged(IChannel target, ChannelMemberChangeEventArgs args)
        {
            foreach(var addition in args.NewMembers)
            {
                Send(addition.Subject.IrcFullId, "JOIN", target.IrcName);
                
                if(addition.IsOperator)
                {
                    SendFromServer("MODE", target.IrcName, "+o", addition.Subject.IrcNick);
                }
                if(addition.NewPosition <= PositionCategory.Talk)
                {
                    SendFromServer("MODE", target.IrcName, "+v", addition.Subject.IrcNick);
                }
            }

            foreach(var change in args.ChangedMembers)
            {
                if(change.IsOperator && !change.WasOperator)
                {
                    SendFromServer("MODE", target.IrcName, "+o", change.Subject.IrcNick);
                }
                else if (!change.IsOperator && change.WasOperator)
                {
                    SendFromServer("MODE", target.IrcName, "-o", change.Subject.IrcNick);
                }

                if(change.NewPosition <= PositionCategory.Talk && change.OldPosition > PositionCategory.Talk)
                {
                    SendFromServer("MODE", target.IrcName, "+v", change.Subject.IrcNick);
                }
                else if (change.NewPosition > PositionCategory.Talk && change.OldPosition <= PositionCategory.Talk)
                {
                    SendFromServer("MODE", target.IrcName, "-v", change.Subject.IrcNick);
                }
            }

            foreach(var deletion in args.RemovedMembers)
            {
                Send(deletion.Subject.IrcFullId, "PART", target.IrcName);
            }
        }

        private void ReceiveChannelMessage(IChannel target, IntermediateMessage msg)
        {
            var payload = msg.Payload;
            var isAction = false;
            var isEmote = false;

            if(msg.Payload.StartsWith("/me "))
            {
                isAction = true;
                isEmote = true;
                payload = payload.Substring(4);
            }

            if(msg.Type == MessageType.OwnerSay)
            {
                payload = "[OS] " + payload;
            }
            else if(msg.Type == MessageType.ObjectIM)
            {
                payload = "[IM] " + payload;
            }
            else if (msg.Type == MessageType.RegionSay)
            {
                payload = "[RS] " + payload;
            }
            else if(msg.Type == MessageType.Whisper && !isEmote)
            {
                payload = "whispers: " + payload;
                isAction = true;
            }
            else if(msg.Type == MessageType.Shout && !isEmote)
            {
                payload = "shouts: " + payload;
                isAction = true;
            }

            if(isAction)
            {
                payload = "\x01" + "ACTION " + payload + "\x01";
            }

            var outmsg = new Irc.Message(msg.Sender.IrcFullId, "PRIVMSG", target.IrcName, payload);
            Send(outmsg);
        }

        private void ReceiveUpstreamMessage(IChannel target, IntermediateMessage msg)
        {
            if(msg.Type == MessageType.ClientNotice)
            {
                SendFromServer("NOTICE", username, msg.Payload);
            }
            else if(msg.Type == MessageType.ClientError)
            {
                SendFromServer("KILL", username, msg.Payload);
                connection.Close();
                state = ConnectionState.CLOSED;
            }
            else if(msg.Type == MessageType.System)
            {
                Send(msg.Sender.IrcFullId, "NOTICE", username, "[SYS] " + msg.Payload);
            }
            else if(msg.Type == MessageType.AwayMessage)
            {
                Send(msg.Sender.IrcFullId, 301, username, msg.Sender.IrcNick, msg.Payload);
            }
            else if(msg.Type == MessageType.IM)
            {
                var payload = msg.Payload;
                if (msg.Payload.StartsWith("/me "))
                {
                    payload = "\x01" + "ACTION " + payload.Substring(4) + "\x01";
                }
                Send(msg.Sender.IrcFullId, "PRIVMSG", username, payload);
            }
        }

        private void Send(Message message)
        {
            var data = parser.Emit(message);
            lock (connection)
            {
                writer.Write(data + "\r\n");
                writer.Flush();
            }
        }

        private void Send(string sender, string command, params string[] argv)
        {
            Send(new Message(sender, command, argv));
        }

        private void Send(string sender, int numeric, params string[] argv)
        {
            Send(new Message(sender, numeric, argv));
        }

        private void SendFromServer(int numeric, params string[] argv)
        {
            SendFromServer(numeric.ToString("000"), argv);
        }

        private void SendFromServer(string command, params string[] argv)
        {
            Send(new Message(LOCALHOST, command, argv));
        }

        private void SendFromClient(string command, params string[] argv)
        {
            Send(new Message(selfId.IrcFullId, command, argv));
        }
    }
}
