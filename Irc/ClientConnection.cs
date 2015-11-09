using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessMetaverseClient.Irc
{
    class ClientConnection : IRawMessageConnection
    {
        enum ConnectionState
        {
            REGISTRATION,
            CONNECTED,
            CLOSED
        }

        const string LOCALHOST = "local.sl";
        const string GROUPHOST = "group.grid.sl";
        const string LOCALNICK = "~SYSTEM~!client@local.sl";

        static readonly string[] supportTokens = new string[] {
            "NICKLEN=63",
            "MODES=,,,t",
            "PREFIX=(ov)@+"
        };

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

        delegate void RawMessageHandler(Message msg);

        HashSet<IRawMessageHandler> handlers;
        Dictionary<string, RawMessageHandler> handlerCallbacks;

        MappedIdentity selfId;
        ConcurrentDictionary<string, IChannel> channels;

        public ClientConnection(Socket connection, IUpstreamConnection upstream, IIdentityMapper mapper)
        {
            this.connection = connection;
            this.upstream = upstream;
            this.mapper = mapper;
            this.channels = new ConcurrentDictionary<string, IChannel>();
            this.handlers = new HashSet<IRawMessageHandler>();
            handlerCallbacks = new Dictionary<string, RawMessageHandler>();

            upstream.ReceiveMessage += ReceiveUpstreamMessage;
            upstream.ChannelListLoaded += ChannelListLoaded;
        }

        public void Register(IRawMessageHandler handler)
        {
            lock(handlers)
            {
                handlers.Add(handler);
                foreach(var i in handler.SupportedMessages)
                {
                    if(!handlerCallbacks.ContainsKey(i))
                    {
                        handlerCallbacks.Add(i, null);
                    }
                    handlerCallbacks[i] += handler.HandleMessage;
                }
            }
        }

        public void Unregister(IRawMessageHandler handler)
        {
            lock(handlers)
            {
                handlers.Remove(handler);
                foreach(var i in handler.SupportedMessages)
                {
                    if(handlerCallbacks.ContainsKey(i))
                    {
                        handlerCallbacks[i] -= handler.HandleMessage;
                    }
                }
            }
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
                    lock (handlers)
                    {
                        if(handlerCallbacks.ContainsKey(msg.Command))
                        {
                            handlerCallbacks[msg.Command](msg);
                            break;
                        }
                    }
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
                        SendNeedMoreParams("PART");
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
                    if (msg.Argv.Count < 1) { SendNeedMoreParams("NAMES"); break; }
                    OnNames(msg.Argv[0]);
                    break;
                case "MODE":
                    OnMode(msg);
                    break;
                case "TOPIC":
                    if (msg.Argv.Count < 1)
                    {
                        SendNeedMoreParams("TOPIC");
                        break;
                    }
                    if (msg.Argv.Count > 1)
                    {
                        SendFromServer(Numeric.ERR_CHANOPRIVSNEEDED, "You can't set topics");
                        break;
                    }
                    OnTopic(msg);
                    break;
                case "USERHOST":
                    if (msg.Argv.Count < 1)
                    {
                        SendNeedMoreParams("USERHOST");
                        break;
                    }
                    var id = mapper.MapUser(msg.Argv[0]);
                    SendFromServer(Numeric.RPL_USERHOST, username, String.Format("{0}=+{1}@{2}", id.IrcNick, id.IrcIdent, id.IrcDomain));
                    break;
                default:
                    SendFromServer(Numeric.ERR_UNKNOWNCOMMAND, username, msg.Command, "Not implemented!");
                    break;
            }
        }

        private void OnPrivmsg(Message msg)
        {
            if (msg.Argv.Count < 2)
            {
                SendNeedMoreParams("PRIVMSG");
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
            SendFromServer(Numeric.RPL_TOPIC, msg.Argv[0], channels[msg.Argv[0]].Topic);
        }

        private void OnMode(Message msg)
        {
            if (msg.Argv.Count < 1) { SendNeedMoreParams("MODE"); return; }
            if (!channels.ContainsKey(msg.Argv[0]))
            {
                SendFromServer(Numeric.ERR_USERSDONTMATCH, "Don't touch their modes!");
                return;
            }
            if (msg.Argv.Count > 1) {
                if (msg.Argv[1] == "+b")
                {
                    SendFromServer(Numeric.RPL_ENDOFBANLIST, username, msg.Argv[0], "End of ban list");
                }
                else
                {
                    SendFromServer(Numeric.ERR_CHANOPRIVSNEEDED, msg.Argv[0], username, "Chanop functions are not supported");
                }
                return;
            }

            SendFromServer(Numeric.RPL_CHANNELMODEIS, username, msg.Argv[0], "+t");
        }

        private IEnumerable<Message> NamesReply(string chan)
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
                    yield return new Message(LOCALHOST, Numeric.RPL_NAMREPLY, username, "=", chan, String.Join(" ", reply));
                }
            }
            yield return new Message(LOCALHOST, Numeric.RPL_ENDOFNAMES, username, chan, "End of NAMES list");
        }

        private void OnNames(string chan)
        {
            Send(NamesReply(chan));
        }

        private void OnWho(Message msg)
        {
            if (msg.Argv.Count < 1) { SendNeedMoreParams("WHO"); return; }

            var key = msg.Argv[0];
            if (channels.ContainsKey(key))
            {
                foreach(var i in channels[key].Members)
                {
                    var reply = new List<string>(new string[] { username, key, i.Subject.IrcIdent, i.Subject.IrcDomain });
                    reply.Add(mapper.Grid.IrcDomain);
                    reply.Add(i.Subject.IrcNick);
                    if (i.IsOperator) { reply.Add("H@"); }
                    else if (i.Position <= PositionCategory.Talk) { reply.Add("H+");}
                    else { reply.Add("H"); }
                    SendFromServer(Numeric.RPL_WHOREPLY, reply.ToArray());
                }
            }
            SendFromServer(Numeric.RPL_ENDOFWHO);
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

                SendFromServer("NOTICE", username, "Connecting to the grid");

                if (upstream.Connect(firstname, lastname, password))
                {
                    selfId = mapper.Client;
                    SendFromServer(Numeric.RPL_WELCOME, username, string.Format("Welcome to the Headless SL Client {0}", selfId.IrcFullId));
                    SendFromServer(Numeric.RPL_YOURHOST, username, "Your host is sl.local, running HeadlessSlClient 0.1");

                    var tokens = new List<string>(supportTokens);
                    foreach(var i in handlers)
                    {
                        tokens.AddRange(i.SupportTokens);
                    }

                    tokens.Add("are supported by this server");

                    SendNumeric(Numeric.RPL_ISUPPORT, tokens.ToArray());
                    state = ConnectionState.CONNECTED;
                }
            }
        }

        private void ChannelListLoaded(IEnumerable<IChannel> loadedChannels)
        {
            foreach (var i in loadedChannels)
            {
                if (!this.channels.ContainsKey(i.IrcName))
                {
                    i.Join().ContinueWith(t =>
                    {
                        RegisterChannelHandlers(i);
                        this.channels[i.IrcName] = i;
                        Send(ChannelJoinMessages(i));
                    });
                }
            }
        }

        private IEnumerable<Message> ChannelJoinMessages(IChannel channel)
        {
            yield return new Message(selfId.IrcFullId, "JOIN", channel.IrcName);
            yield return new Message(LOCALHOST, Numeric.RPL_TOPIC, username, channel.IrcName, channel.Topic);
            yield return new Message(LOCALHOST, Numeric.RPL_CHANCREATED, username, channel.IrcName, mapper.Grid.IrcDomain, "0");
            foreach (var i in NamesReply(channel.IrcName)) { yield return i; }
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
            if(msg.Type == MessageType.ClientNotice)
            {
                Send(LOCALNICK, "PRIVMSG", target.IrcName, msg.Payload);
                return;
            }

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
                Send(msg.Sender.IrcFullId, Numeric.RPL_AWAY, username, msg.Sender.IrcNick, msg.Payload);
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

        public void Send(IEnumerable<Message> messages)
        {
            lock(connection)
            {
                foreach(var i in messages)
                {
                    writer.Write(parser.Emit(i) + "\r\n");
                }
                writer.Flush();
            }
        }

        public void Send(Message message)
        {
            var data = parser.Emit(message);
            lock (connection)
            {
                writer.Write(data + "\r\n");
                writer.Flush();
            }
        }

        public void Send(string sender, string command, params string[] argv)
        {
            Send(new Message(sender, command, argv));
        }

        public void Send(string sender, Numeric numeric, params string[] argv)
        {
            Send(new Message(sender, numeric, argv));
        }

        public void SendFromServer(Numeric numeric, params string[] argv)
        {
            Send(new Message(LOCALHOST, numeric, argv));
        }

        public void SendFromServer(string command, params string[] argv)
        {
            Send(new Message(LOCALHOST, command, argv));
        }

        public void SendFromClient(string command, params string[] argv)
        {
            Send(new Message(selfId.IrcFullId, command, argv));
        }

        public void SendNumeric(Numeric numeric, params string[] argv)
        {
            var args = new List<string>();
            args.Add(username);
            args.AddRange(argv);
            SendFromServer(numeric, args.ToArray());
        }

        public void SendNeedMoreParams(string command)
        {
            SendFromServer(Numeric.ERR_NEEDMOREPARAMS, username, command, "Not enough parameters");
        }

        public string ClientNick { get { return username; } }
        public string ServerName { get { return LOCALHOST; } }
    }
}
