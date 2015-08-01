using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;
using System.Net.Sockets;
using System.IO;

namespace HeadlessSlClient
{

    class Server
    {
        enum ConnectionState {
            REGISTRATION,
            CONNECTED,
            QUIT,
            FAILED
        }

        const string LOCAL = "local.sl";
        const string SYSMSG = "*system!system@grid.sl";
        const string GRID_LOGIN = "http://localhost:9000";

        private Socket connection;
        private GridClient slClient;

        private string username = null;
        private string password = null;
        private string clienthostmask = null;

        private ConnectionState state;

        private Irc.IMessageParser parser;
        private StreamWriter writer;

        public Server(Socket connection)
        {
            this.connection = connection;
            slClient = new GridClient();
        }

        internal void Run()
        {
            state = ConnectionState.REGISTRATION;
            
            using(var stream = new NetworkStream(connection))
            {
                using(var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false)))
                {
                    using (writer = new StreamWriter(stream))
                    {
                        parser = new Irc.Rfc1459Parser();
                        while (state != ConnectionState.QUIT)
                        {
                            string str = reader.ReadLine();
                            if (str == null)
                            {
                                Console.WriteLine("Unclean client disconnect");
                                Disconnect();
                                continue;
                            }
                            Console.WriteLine("IRC recv: " + str);
                            Irc.Message msg = parser.Parse(str);
                            DispatchMessage(msg);
                        }
                    }
                }
            }
            connection.Close();
        }

        private void DispatchMessage(Irc.Message msg)
        {
 	        switch(state)
            {
                case ConnectionState.REGISTRATION:
                    OnRegistrationMessage(msg);
                    break;
                case ConnectionState.CONNECTED:
                    OnConnectedMessage(msg);
                    break;
            }
        }

        private void OnConnectedMessage(Irc.Message msg)
        {
            if (msg.Command == "PRIVMSG")
            {
                DoPrivmsg(msg);
            }
            else if(msg.Command == "QUIT")
            {
                Disconnect();
            }
            else if(msg.Command == "PING")
            {
                Send(new Irc.Message(LOCAL, "PONG", msg.Argv.ToArray()));
            }
            else
            {
                Send(new Irc.Message(LOCAL, "421", "Not implemented!"));
            }
        }

        private void DoPrivmsg(Irc.Message msg)
        {
            if (msg.Argv[0] == "#local")
            {
                if(msg.Argv[1].StartsWith("\x01"+"ACTION "))
                {
                    slClient.Self.Chat("/me " + msg.Argv[1].Substring(8, msg.Argv[1].Length - 9), 0, ChatType.Normal);
                }
                slClient.Self.Chat(msg.Argv[1], 0, ChatType.Normal);
            }
            else
            {
                Send(new Irc.Message(LOCAL, "NOTICE", username, "Unsupported target"));
            }
        }

        private void Disconnect()
        {
            slClient.Network.Logout();
            state = ConnectionState.QUIT;
        }

        private void OnRegistrationMessage(Irc.Message msg)
        {
 	        if(msg.Command == "NICK")
            {
                username = msg.Argv[0];
                clienthostmask = username + "!user@local.sl";
            }
            else if(msg.Command == "PASS")
            {
                password = msg.Argv[0];
            }

            if(username != null && password != null)
            {
                DoLogin();
            }
        }

        private void DoLogin()
        {
            var names = username.Split('.');
            string firstname = names[0];
            string lastname = names.Count() == 1 ? "resident" : names[1];

            slClient.Self.ChatFromSimulator += OnIncomingMessage;
            slClient.Network.LoginProgress += OnLoginProgress;

            slClient.Settings.LOGIN_SERVER = GRID_LOGIN;

            var success = slClient.Network.Login(firstname, lastname, password, "HeadlessSlClient", "0.1");
            if (success)
            {
                Send(new Irc.Message(LOCAL, 1, username, string.Format("Welcome to Second Life {0}", clienthostmask, LOCAL)));
                Send(new Irc.Message(LOCAL, 2, username, "Your host is sl.local, running HeadlessSlClient 0.1"));
                Send(new Irc.Message(LOCAL, 5, username, "NICKLEN=63"));
                Send(new Irc.Message(clienthostmask, "JOIN", "#local"));
                state = ConnectionState.CONNECTED;
            }
            else
            {
                Send(new Irc.Message(LOCAL, 464, slClient.Network.LoginMessage));
            }
        }

        private void OnLoginProgress(object sender, LoginProgressEventArgs e)
        {
            Console.WriteLine("Login status: " + e.Status.ToString() + ":" + e.Message);
        }

        void OnIncomingMessage(object sender, ChatEventArgs e)
        {
            if(e.SourceType == ChatSourceType.System)
            {
                Send(new Irc.Message(SYSMSG, "PRIVMSG", "#local", e.Message));
            }
            else
            {
                if (e.Type == ChatType.StartTyping || e.Type == ChatType.StopTyping )
                {
                    return;
                }

                var source = IdentFromSlName(e.FromName);
                var payload = e.Message;

                if (e.Type == ChatType.Shout)
                {
                    payload = "\x01"+"ACTION shouts: " + payload + "\x01";
                }
                else if (e.Type == ChatType.Whisper)
                {
                    payload = "\x01" + "ACTION whispers: " + payload + "\x01";
                }
                else if (e.Type == ChatType.RegionSay)
                {
                    payload = "\x01" + "ACTION says to region: " + payload + "\x01";
                }
                else if (e.Type == ChatType.OwnerSay)
                {
                    source = "~" + source;
                }
                else if (e.Type == ChatType.Debug)
                {
                    source = "*Debug!system@grid.sl";
                    payload = "\x01" + "ACTION " + e.FromName + ": " + payload;
                }

                Send(new Irc.Message(IdentFromSlName(e.FromName), "PRIVMSG", "#local", payload));
            }
        }

        private string IdentFromSlName(string p)
        {
            return NickFromSlName(p) + "!entity@grid.sl";
        }

        private string NickFromSlName(string p)
        {
            var parts = p.ToLower().Split(' ').ToList();
            if (parts.Count == 2 && parts[1] == "resident")
            {
                parts.RemoveAt(parts.Count - 1);
            }

            for(int i = 0; i < parts.Count; ++i )
            {
                parts[i] = parts[i].Substring(0, 1).ToUpper() + parts[i].Substring(1);
            }

                return string.Join(".", parts);
        }

        private void Send(Irc.Message message)
        {
            var data = parser.Emit(message);
            Console.WriteLine("IRC send: " + data);
            writer.Write(data + "\r\n");
            writer.Flush();
        }
    }
}
