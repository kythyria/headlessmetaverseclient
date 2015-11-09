using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Regex = System.Text.RegularExpressions.Regex;
using OpenMetaverse;

namespace HeadlessMetaverseClient
{
    class LocalChannel : IChannel, IUpstreamChannel
    {
        const float WHISPERDISTANCE = 5.0f;
        const float TALKDISTANCE = 20.0f;
        const float SHOUTDISTANCE = 100.0f;

        private object syncRoot = new object();
        private TaskCompletionSource<bool> joinTask = new TaskCompletionSource<bool>();

        private OpenMetaverse.GridClient client;
        private IIdentityMapper mapper;
        private int chatChannel = 0;
        private ChannelState state = ChannelState.Unconnected;

        private Dictionary<string, Dictionary<UUID, ChannelMembership>> nearby;

        public string SlName
        {
            get { return "Nearby Chat"; }
        }

        public string IrcName
        {
            get { return "#local"; }
        }

        public string Topic
        {
            get { return "Nearby Chat"; }
        }

        public IEnumerable<ChannelMembership> Members
        {
            get
            {
                return nearby.SelectMany(i => i.Value.Values);
            }
        }

        public event ChannelMemberChangeHandler MembersChanged;

        public void SendMessage(IntermediateMessage msg)
        {
            client.Self.Chat(msg.Payload, chatChannel, ChatType.Normal);
        }

        public event ReceiveMessageHandler ReceiveMessage;

        public IChannel DownstreamChannel
        {
            get { return this; }
        }

        public LocalChannel(OpenMetaverse.GridClient client, IIdentityMapper mapper)
        {
            this.client = client;
            this.mapper = mapper;
            this.nearby = new Dictionary<string, Dictionary<UUID, ChannelMembership>>();

            var selfmember = new ChannelMembership();
            selfmember.IsOperator = false;
            selfmember.Position = PositionCategory.Whisper;
            selfmember.Subject = mapper.MapUser(client.Self.AgentID, client.Self.Name);

            var thissimlist = new Dictionary<UUID, ChannelMembership>();
            thissimlist.Add(client.Self.AgentID, selfmember);
            nearby.Add(client.Network.CurrentSim.Name, thissimlist);

            client.Grid.CoarseLocationUpdate += OnLocationUpdate;
            client.Self.ChatFromSimulator += OnLocalChat;
        }

        public void OnLocationUpdate(object sender, CoarseLocationUpdateEventArgs e)
        {
            var evt = new ChannelMemberChangeEventArgs();
            Dictionary<UUID, ChannelMembership> nearbyAvatars;

            if (e.Simulator.Name != client.Network.CurrentSim.Name) return;

            if(!nearby.TryGetValue(e.Simulator.Name, out nearbyAvatars))
            {
                nearbyAvatars = new Dictionary<UUID, ChannelMembership>();
                nearby.Add(e.Simulator.Name, nearbyAvatars);
            }
            
            foreach(var i in e.NewEntries)
            {
                if (i == client.Self.AgentID) { continue; }

                var detail = new ChannelMemberChangeEventArgs.ChangeDetails();
                detail.Subject = mapper.MapUser(i);
                detail.IsOperator = false;
                detail.WasOperator = false;
                detail.NewPosition = CategorisePosition(i, e);
                detail.OldPosition = PositionCategory.Distant;
                evt.NewMembers.Add(detail);

                var membership = new ChannelMembership();
                membership.Subject = detail.Subject;
                membership.IsOperator = false;
                membership.Position = CategorisePosition(i, e);
                nearbyAvatars.Add(i,membership);
            }

            foreach(var i in e.RemovedEntries)
            {
                if (i == client.Self.AgentID) { continue; }

                var detail = new ChannelMemberChangeEventArgs.ChangeDetails();
                if (nearbyAvatars.ContainsKey(i))
                {
                    detail.Subject = nearbyAvatars[i].Subject;
                    detail.IsOperator = false;
                    detail.WasOperator = false;
                    detail.NewPosition = PositionCategory.Distant;
                    detail.OldPosition = nearbyAvatars[i].Position;
                    evt.RemovedMembers.Add(detail);

                    nearbyAvatars.Remove(i);
                }
            }

            foreach(var i in nearbyAvatars)
            {
                if (i.Key == client.Self.AgentID) { continue; }

                var newpos = CategorisePosition(i.Key, e);
                if (i.Value.Position != newpos)
                {
                    var detail = new ChannelMemberChangeEventArgs.ChangeDetails();
                    detail.Subject = nearbyAvatars[i.Key].Subject;
                    detail.IsOperator = false;
                    detail.WasOperator = false;
                    detail.NewPosition = newpos;
                    detail.OldPosition = nearbyAvatars[i.Key].Position;
                    evt.ChangedMembers.Add(detail);
                    nearbyAvatars[i.Key].Position = newpos;
                }
            }

            if (!evt.HasChanges) { return; }

            lock (syncRoot)
            {
                if (state != ChannelState.Connected)
                {
                    state = ChannelState.Connected;
                    joinTask.SetResult(true);
                }
            }

            if(this.MembersChanged != null)
            {
                MembersChanged(this, evt);
            }
        }

        public ChannelState State
        {
            get
            {
                lock(syncRoot)
                {
                    return state;
                }
            }
        }

        public Task<bool> Join()
        {
            lock(syncRoot)
            {
                if(state != ChannelState.Connected)
                state = ChannelState.Joining;
            }
            return joinTask.Task;
        }

        private PositionCategory CategorisePosition(UUID i, CoarseLocationUpdateEventArgs e)
        {
            var mypos = client.Self.SimPosition;
            Vector3 otherpos;
            if(!e.Simulator.AvatarPositions.TryGetValue(i, out otherpos))
            {
                return PositionCategory.SameRegionGroup;
            }
            var distance = Vector3.Distance(mypos, otherpos);

            if(distance <= WHISPERDISTANCE)
            {
                return PositionCategory.Whisper;
            }
            else if (distance <= TALKDISTANCE)
            {
                return PositionCategory.Talk;
            }
            else if (distance <= SHOUTDISTANCE)
            {
                return PositionCategory.Shout;
            }
            else
            {
                return PositionCategory.SameRegionGroup;
            }
        }

        public void OnLocalChat(object sender, OpenMetaverse.ChatEventArgs e)
        {
            if (e.Type == ChatType.StartTyping || e.Type == ChatType.StopTyping || e.Type == ChatType.Debug)
            {
                return;
            }

            if (e.SourceID == client.Self.AgentID)
            {
                return;
            }

            var msg = new IntermediateMessage();
            msg.Payload = e.Message;
            
            if(e.SourceType == OpenMetaverse.ChatSourceType.Object)
            {
                msg.Sender = mapper.MapObject(e.SourceID, e.FromName);
            }
            else if(e.SourceType == OpenMetaverse.ChatSourceType.Agent)
            {
                msg.Sender = mapper.MapUser(e.SourceID, e.FromName);
            }
            else
            {
                msg.Sender = mapper.Grid;
            }

            msg.Timestamp = DateTime.UtcNow;



            if (e.SourceType == ChatSourceType.System)
            {
                msg.Type = MessageType.System;
                msg.Sender = mapper.Grid;
            }

            if(e.Type == OpenMetaverse.ChatType.Shout)
            {
                msg.Type = MessageType.Shout;
            }
            else if(e.Type == ChatType.Whisper)
            {
                msg.Type = MessageType.Whisper;
            }
            else if (e.Type == ChatType.RegionSay)
            {
                msg.Type = MessageType.RegionSay;
            }
            else if (e.Type == ChatType.OwnerSay)
            {
                msg.Type = MessageType.OwnerSay;
                if(Rlv(msg.Payload)) { return; }
            }

            SendMessageDownstream(msg);
        }

        public void SendMessageDownstream(IntermediateMessage msg)
        {
            if (this.ReceiveMessage != null)
            {
                ReceiveMessage(this, msg);
            }
        }

        static readonly Regex reRlvCommand = new Regex(@"@(?<command>[^:=]*)(?::(?<option>[^=]*))?=(?<param>.*)(?:,|$)");
        private bool Rlv(string str)
        {
            var match = reRlvCommand.Match(str);
            
            if(match.Success)
            {
                OpenMetaverse.Logger.Log(string.Format("RLV: @{0}:{1}={2}",match.Groups["command"].Value, match.Groups["option"].Value, match.Groups["param"].Value), OpenMetaverse.Helpers.LogLevel.Info);
                if(match.Groups["command"].Value == "sit" && match.Groups["option"].Success)
                {
                    client.Self.Stand();
                    client.Self.RequestSit(UUID.Parse(match.Groups["option"].Value), Vector3.Zero);
                }
                else if(match.Groups["command"].Value == "redirchat" && match.Groups["option"].Success)
                {
                    if (match.Groups["param"].Value == "n" || match.Groups["param"].Value == "add")
                    {
                        chatChannel = int.Parse(match.Groups["option"].Value);
                    }
                }
            }

            return match.Success;
        }
    }
}
