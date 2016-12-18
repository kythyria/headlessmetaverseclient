using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using OpenMetaverse;

namespace HeadlessMetaverseClient
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new Configuration(args);

            Socket socket = Listen();

            while(true)
            {
                var connection = socket.Accept();

                var us = new UpstreamConnection("https://login.agni.lindenlab.com/cgi-bin/login.cgi", "agni.lindenlab.com");
                var ds = new Irc.ClientConnection(connection, us, us.Mapper);
                var friendlist = new FriendsList(us, ds, config);
                ds.Run();
            }

        }

        private static Socket Listen()
        {
            var e = new IPEndPoint(new IPAddress(new Byte[] {127,0,0,1}), 6668);
            var socket = new Socket(e.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(e);
            socket.Listen(1);
            return socket;
        }
    }
}
