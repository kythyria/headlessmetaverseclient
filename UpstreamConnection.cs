using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessMetaverseClient
{
    class UpstreamConnection : IUpstreamConnection
    {
        const string CHANNEL = "HeadlessSlClient";
        const string VERSION = "0.1";

        private IIdentityMapper mapper;
        private string loginUri;
        private Dictionary<UUID, GroupChannel> channels;
        private LocalChannel localChannel;
        private GridClient client;
        private Dictionary<UUID,Group> groups;
        private Dictionary<UUID, UUID> oneOnOnesessions;

        public event ChannelListLoadedHandler ChannelListLoaded;
        public event FriendsListLoadedHandler FriendsListLoaded;
        public IIdentityMapper Mapper { get { return mapper; } }

        public UpstreamConnection(string loginUri, string gridName)
        {
            this.loginUri = loginUri;
            this.client = new GridClient();
            client.Settings.LOGIN_SERVER = loginUri;
            client.Settings.MULTIPLE_SIMS = false;

            mapper = new SyncAsyncMapper(new IdentityMapperAsync(client, gridName));
            oneOnOnesessions = new Dictionary<UUID, UUID>();
        }

        public IEnumerable<IChannel> GetChannels()
        {
            if(groups == null)
            {
                yield break;
            }

            yield return localChannel.DownstreamChannel;
            foreach (var i in channels.OrderBy(j => j.Value.SlName))
            {
                yield return i.Value.DownstreamChannel;
            }
        }

        private void InitChannels()
        {
            channels = channels ?? new Dictionary<UUID, GroupChannel>();
            foreach(var i in groups)
            {
                if (!channels.ContainsKey(i.Key))
                {
                    var c = new GroupChannel(client, mapper, i.Value);
                    channels[i.Value.ID] = c;
                }
            }
        }

        public FriendListEntry GetFriendsList()
        {
            throw new NotImplementedException();
        }

        public void SendMessageTo(MappedIdentity destination, IntermediateMessage msg)
        {
            UUID session;
            if(!oneOnOnesessions.TryGetValue(destination.AvatarID, out session))
            {
                oneOnOnesessions[destination.AvatarID] = client.Self.AgentID ^ destination.AvatarID;
            }

            client.Self.InstantMessage(client.Self.Name, destination.AvatarID, msg.Payload,
                session, InstantMessageDialog.MessageFromAgent, InstantMessageOnline.Online,
                client.Self.SimPosition, client.Network.CurrentSim.ID, null);
        }

        public event ReceiveMessageHandler ReceiveMessage;

        public bool Connect(string firstname, string lastname, string password)
        {
            client.Network.LoginProgress += OnLoginProgress;
            client.Network.SimConnecting += OnSimConnecting;
            client.Network.SimConnected += OnSimConnected;
            client.Network.SimDisconnected += OnSimDisconnected;

            var success = client.Network.Login(firstname, lastname, password, CHANNEL, VERSION);

            if (success)
            {
                localChannel = new LocalChannel(client, mapper);
                client.Self.IM += OnIM;
                client.Self.ChatSessionMemberAdded += OnChatSessionMemberAdded;
                client.Self.ChatSessionMemberLeft += OnChatSessionMemeberLeft;

                client.Groups.CurrentGroups += OnGroupListLoaded;
            }

            return success;
        }

        private void OnChatSessionMemeberLeft(object sender, ChatSessionMemberLeftEventArgs e)
        {
            GroupChannel chan;
            if(channels.TryGetValue(e.SessionID, out chan))
            {
                chan.OnMemberLeft(e.AgentID);
            }
        }

        private void OnChatSessionMemberAdded(object sender, ChatSessionMemberAddedEventArgs e)
        {
            GroupChannel chan;
            if (channels.TryGetValue(e.SessionID, out chan))
            {
                chan.OnMemberJoined(e.AgentID);
            }
        }

        private void OnGroupListLoaded(object sender, CurrentGroupsEventArgs e)
        {
            groups = e.Groups;
            InitChannels();

            if(this.ChannelListLoaded != null)
            {
                ChannelListLoaded(this.GetChannels());
            }
        }

        public void Disconnect()
        {
            client.Network.Logout();
        }

        public GridClient Client { get { return client; } }

        private void OnIM(object o, InstantMessageEventArgs e)
        {
            switch (e.IM.Dialog)
            {
                // Ones we can't ever see or don't care about, so ignore them:
                case InstantMessageDialog.AcceptTeleport:
                case InstantMessageDialog.DenyTeleport:
                case InstantMessageDialog.FriendshipAccepted:
                case InstantMessageDialog.FriendshipDeclined:
                case InstantMessageDialog.TaskInventoryAccepted:
                case InstantMessageDialog.TaskInventoryDeclined:
                case InstantMessageDialog.Lure911:
                case InstantMessageDialog.Session911Start:
                case InstantMessageDialog.InventoryAccepted:
                case InstantMessageDialog.InventoryDeclined:
                case InstantMessageDialog.GroupInvitationAccept:
                case InstantMessageDialog.GroupInvitationDecline:
                case InstantMessageDialog.GroupNoticeRequested:
                case InstantMessageDialog.NewUserDefault:
                case InstantMessageDialog.StartTyping:
                case InstantMessageDialog.StopTyping:
                    break;

                //Handle properly
                case InstantMessageDialog.GroupNotice:
                    break;
                case InstantMessageDialog.MessageBox:
                    break;

                // One-on-one IM
                case InstantMessageDialog.BusyAutoResponse:
                case InstantMessageDialog.MessageFromAgent:
                    OnMessageFromAgent(e);
                    break;

                //These are functionally messages from an object:
                case InstantMessageDialog.GotoUrl:
                case InstantMessageDialog.MessageFromObject:
                    OnMessageFromObject(e);
                    break;

                //Autoreply
                case InstantMessageDialog.FriendshipOffered:
                    break;
                case InstantMessageDialog.TaskInventoryOffered:
                    break;
                case InstantMessageDialog.InventoryOffered:
                    break;
                case InstantMessageDialog.GroupInvitation:
                    break;

                //Reject unsupported
                case InstantMessageDialog.GodLikeRequestTeleport:
                case InstantMessageDialog.RequestLure:
                case InstantMessageDialog.RequestTeleport:
                    client.Self.InstantMessage(e.IM.FromAgentID,
                        string.Format("[FAILURE]: This message type ({0}) is not supported by this client.", e.IM.Dialog.ToString()));
                    break;

                // Session stuff, unsure how to implement
                case InstantMessageDialog.SessionAdd:
                case InstantMessageDialog.SessionCardlessStart:
                case InstantMessageDialog.SessionDrop:
                case InstantMessageDialog.SessionGroupStart:
                case InstantMessageDialog.SessionOfflineAdd:
                    Console.WriteLine(e.IM.Dialog.ToString() + " " + e.IM.FromAgentName + ": " + e.IM.Message);
                    break;
                case InstantMessageDialog.SessionSend:
                    if(groups.ContainsKey(e.IM.IMSessionID))
                    {
                        OnMessageFromAgent(e);
                    }
                    break;
                default:
                    break;
            }
        }

        private void OnMessageFromObject(InstantMessageEventArgs e)
        {
            var msg = new IntermediateMessage();
            msg.Sender = mapper.MapObject(e.IM.FromAgentID, e.IM.FromAgentName);
            msg.Timestamp = e.IM.Timestamp;
            msg.Type = MessageType.ObjectIM;
            if(e.IM.Dialog == InstantMessageDialog.BusyAutoResponse)
            {
                msg.Type = MessageType.AwayMessage;
            }
            msg.Payload = e.IM.Message;

            localChannel.SendMessageDownstream(msg);
        }

        private void OnMessageFromAgent(InstantMessageEventArgs e)
        {
            var msg = new IntermediateMessage();
            msg.Sender = mapper.MapUser(e.IM.FromAgentID, e.IM.FromAgentName);
            msg.Timestamp = e.IM.Timestamp;
            msg.Type = MessageType.IM;
            msg.Payload = e.IM.Message;

            UUID sid = e.IM.IMSessionID;
            if(groups.ContainsKey(sid))
            {
                channels[sid].SendMessageDownstream(msg);
                return;
            }

            oneOnOnesessions[e.IM.FromAgentID] = sid;

            if(this.ReceiveMessage != null)
            {
                ReceiveMessage(null, msg);
            }
        }

        private void OnSimDisconnected(object sender, SimDisconnectedEventArgs e)
        {
            var msg = new IntermediateMessage(mapper.Grid, MessageType.ClientNotice);
            msg.Payload = "Disconnected from " + e.Simulator.Name;

            if (ReceiveMessage != null)
            {
                ReceiveMessage(null, msg);
            }
        }

        private void OnSimConnected(object sender, SimConnectedEventArgs e)
        {
            var msg = new IntermediateMessage(mapper.Grid, MessageType.ClientNotice);
            msg.Payload = "Connected to " + e.Simulator.Name;
            if (ReceiveMessage != null)
            {
                ReceiveMessage(null, msg);
            }

        }

        private void OnSimConnecting(object sender, SimConnectingEventArgs e)
        {
            var msg = new IntermediateMessage(mapper.Grid, MessageType.ClientNotice);
            msg.Payload = "Connecting to simulator";
            if (ReceiveMessage != null)
            {
                ReceiveMessage(null, msg);
            }
        }

        private void OnLoginProgress(object sender, LoginProgressEventArgs e)
        {
            IntermediateMessage msg;
            if (e.Status != LoginStatus.Failed)
            {
                msg = new IntermediateMessage(mapper.Grid, MessageType.ClientNotice);
                msg.Payload = e.Status.ToString() + (e.Message != "" ? ": " + e.Message : "");
            }
            else
            {
                msg = new IntermediateMessage(mapper.Grid, MessageType.ClientError);
                msg.Payload = e.Status.ToString() + (e.Message != "" ? ": " + e.Message : "");
                msg.Payload += " (" + e.FailReason + ")";
            }

            if (ReceiveMessage != null)
            {
                ReceiveMessage(null, msg);
            }
        }

        private void LoadGroups()
        {
            var msg = IntermediateMessage.ClientNotice(mapper.Grid, "Loading group list");
            if (ReceiveMessage != null)
            {
                ReceiveMessage(null, msg);
            }

            var signal = new System.Threading.AutoResetEvent(false);
            EventHandler<CurrentGroupsEventArgs> handler = null;
            handler = delegate(object o, CurrentGroupsEventArgs e)
            {
                groups = e.Groups;
                client.Groups.CurrentGroups -= handler;
            };
            client.Groups.CurrentGroups += handler;
            client.Groups.RequestCurrentGroups();

            if (!signal.WaitOne(20000))
            {
                msg = IntermediateMessage.ClientNotice(mapper.Grid, "Group load failed!");
                if (ReceiveMessage != null)
                {
                    ReceiveMessage(null, msg);
                }
            }
        }
    }
}
