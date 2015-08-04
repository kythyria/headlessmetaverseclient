using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using OpenMetaverse;

namespace HeadlessSlClient
{
    class Program
    {
        static void Main(string[] args)
        {
            Socket socket = Listen();

            while(true)
            {
                var connection = socket.Accept();

                //var us = new UpstreamConnection("http://localhost:9000", "localhost");
                var us = new UpstreamConnection("https://login.agni.lindenlab.com/cgi-bin/login.cgi", "agni.lindenlab.com");
                var ds = new Irc.ClientConnection(connection, us, us.Mapper);
                ds.Run();
                /*var server = new Server(connection);
                server.Run();*/
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
