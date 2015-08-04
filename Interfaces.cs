using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessSlClient
{
    delegate void ReceiveMessageHandler(IChannel target, IntermediateMessage msg);

    class ChannelMemberChangeEventArgs
    {
        public class ChangeDetails
        {
            public MappedIdentity Subject;
            
            public PositionCategory OldPosition;
            public PositionCategory NewPosition;
            
            public bool IsOperator;
            public bool WasOperator;
        }

        public List<ChangeDetails> NewMembers = new List<ChangeDetails>();
        public List<ChangeDetails> ChangedMembers = new List<ChangeDetails>();
        public List<ChangeDetails> RemovedMembers = new List<ChangeDetails>();

        public bool HasChanges
        {
            get
            {
                return (NewMembers.Count + ChangedMembers.Count + RemovedMembers.Count) > 0;
            }
        }
    }

    delegate void ChannelMemberChangeHandler(IChannel target, ChannelMemberChangeEventArgs args);

    enum MessageType
    {
        System,
        OwnerSay,
        RegionSay,
        Shout,
        Talk,
        Whisper,
        IM,
        AwayMessage,
        ClientNotice,
        ClientError,
        ObjectIM
    }

    class IntermediateMessage
    {
        public string Payload;
        public MessageType Type;
        public DateTime Timestamp;
        public MappedIdentity Sender;

        public IntermediateMessage() { }

        public IntermediateMessage(MappedIdentity sender, MessageType messageType)
        {
            this.Sender = sender;
            this.Type = messageType;
        }

        internal static IntermediateMessage ClientNotice(MappedIdentity sender, string payload)
        {
            var msg = new IntermediateMessage(sender, MessageType.ClientNotice);
            msg.Payload = payload;
            return msg;
        }
    }

    class ChannelMembership
    {
        public MappedIdentity Subject;
        public PositionCategory Position;
        public bool IsOperator;
    }

    interface IChannel
    {
        string SlName { get; }
        string IrcName { get;  }
        string Topic { get; }

        IEnumerable<ChannelMembership> Members { get; }
        event ChannelMemberChangeHandler MembersChanged;
        void SendMessage(IntermediateMessage msg);
        event ReceiveMessageHandler ReceiveMessage;
    }

    interface IIdentityMapper
    {
        MappedIdentity MapUser(string IrcName);
        MappedIdentity MapUser(UUID SlId, string SlName = null);

        MappedIdentity MapObject(UUID SlId, string SlName = null);

        UUID MapChannelName(string IrcName);
        string MapGroup(UUID group);
        string MapGroup(Group group);

        MappedIdentity Grid { get; }
        MappedIdentity Client { get; }
    }

    struct FriendListEntry
    {
        MappedIdentity Friend;
        bool Online;
    }

    delegate void ChannelListLoadedHandler(IEnumerable<IChannel> channels);
    delegate void FriendsListLoadedHandler(IEnumerable<FriendListEntry> friends);

    interface IUpstreamConnection
    {
        IEnumerable<IChannel> GetChannels();
        FriendListEntry GetFriendsList();

        void SendMessageTo(MappedIdentity destination, IntermediateMessage msg);
        event ReceiveMessageHandler ReceiveMessage;

        bool Connect(string firstname, string lastname, string password);
        void Disconnect();

        event ChannelListLoadedHandler ChannelListLoaded;
        event FriendsListLoadedHandler FriendsListLoaded;
    }

    interface IUpstreamChannel
    {
        IChannel DownstreamChannel { get; }

        void OnLocalChat(object o, ChatEventArgs e);
        void SendMessageDownstream(IntermediateMessage msg);
    }
}
