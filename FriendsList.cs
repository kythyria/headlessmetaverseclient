using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GridClient = OpenMetaverse.GridClient;
using Numeric = HeadlessSlClient.Irc.Numeric;

namespace HeadlessSlClient
{
    class FriendsList : Irc.IRawMessageHandler
    {

        private object syncRoot = new object();

        private GridClient client;
        private Irc.IMessageSink downstream;
        private IIdentityMapper mapper;

        public FriendsList(GridClient client, IIdentityMapper mapper, Irc.IMessageSink downstream)
        {
            this.downstream = downstream;
            this.client = client;
            this.mapper = mapper;
            client.Friends.FriendOnline += GridClient_FriendPresenceChanged;
            client.Friends.FriendOffline += GridClient_FriendPresenceChanged;
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
        }

        #endregion
    }
}
