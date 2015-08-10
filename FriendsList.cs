using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GridClient = OpenMetaverse.GridClient;
using Numeric = HeadlessMetaverseClient.Irc.Numeric;

namespace HeadlessMetaverseClient
{
    class FriendsList : Irc.IRawMessageHandler
    {

        private object syncRoot = new object();

        private GridClient client;

        private Irc.IRawMessageSink downstream;
        private IIdentityMapper mapper;

        public FriendsList(IUpstreamConnection connection, Irc.IRawMessageConnection downstream)
        {
            this.downstream = downstream;
            this.client = connection.Client;
            this.mapper = connection.Mapper;
            client.Friends.FriendOnline += GridClient_FriendPresenceChanged;
            client.Friends.FriendOffline += GridClient_FriendPresenceChanged;

            downstream.Register(this);
        }

        private void GridClient_FriendPresenceChanged(object sender, OpenMetaverse.FriendInfoEventArgs e)
        {
            var mappedFriend = mapper.MapUser(e.Friend.UUID, e.Friend.Name);

            if(e.Friend.IsOnline)
            {
                downstream.SendNumeric(Numeric.RPL_MONONLINE, mappedFriend.IrcNick);
            }
            else
            {
                downstream.SendNumeric(Numeric.RPL_MONOFFLINE, mappedFriend.IrcNick);
            }
        }

        #region Irc.IRawMessageHandler

        public IEnumerable<string> SupportedMessages { get { return new string[] { "MONITOR" }; } }
        public IEnumerable<string> SupportTokens { get { return new string[] { "MONITOR" }; } }
        public IEnumerable<string> Caps { get { return new string[] { }; } }

        public void HandleMessage(Irc.Message msg)
        {
            if (msg.Argv.Count < 1)
            {
                downstream.SendNeedMoreParams("MONITOR");
            }

            switch(msg.Argv[0].ToUpperInvariant())
            {
                case "+":
                case "-":
                case "C":
                    downstream.SendNumeric(Numeric.ERR_UNKNOWNSUBCOMMAND, "MONITOR", msg.Argv[0], "Use FriendServ for that");
                    break;
                case "L":
                    var idlist = new List<MappedIdentity>();
                    client.Friends.FriendList.ForEach(i => idlist.Add(mapper.MapUser(i.Key, i.Value.Name)));
                    downstream.Send(idlist.ChunkTrivialBetter(512 / 63).Select(i =>
                    {
                        var m = new Irc.Message(downstream.ServerName, Numeric.RPL_MONLIST, downstream.ClientNick);
                        m.Argv.Add(String.Join(",", i.Select(j=>j.IrcNick)));
                        return m;
                    }).AppendSingle(new Irc.Message(downstream.ServerName, Numeric.RPL_ENDOFMONLIST, "End of monitor list")));
                    break;
                case "S":
                    var msglist = new List<Irc.Message>();
                    client.Friends.FriendList.ForEach(i =>
                    {
                        var id = mapper.MapUser(i.Key, i.Value.Name);
                        var num = i.Value.IsOnline ? Numeric.RPL_MONONLINE : Numeric.RPL_MONOFFLINE;
                        var response = new Irc.Message(downstream.ServerName, num, "*", id.IrcNick);
                        msglist.Add(response);
                    });
                    downstream.Send(msglist);
                    break;
            }
        }

        #endregion
    }
}
