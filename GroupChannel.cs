using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessMetaverseClient
{
    class GroupChannel : IChannel, IUpstreamChannel
    {
        private object syncRoot = new object();

        string slName, ircName, topic;
        IIdentityMapper mapper;
        GridClient client;
        Group group;
        ChannelState state = ChannelState.Unconnected;

        public GroupChannel(GridClient client, IIdentityMapper mapper, Group group)
        {
            slName = group.Name;
            ircName = mapper.MapGroup(group);
            topic = slName;
            this.mapper = mapper;
            this.client = client;
            this.group = group;
        }

        public string SlName
        {
            get { return slName; }
        }

        public string IrcName
        {
            get { return ircName; }
        }

        public string Topic
        {
            get { return topic; }
        }

        public IEnumerable<ChannelMembership> Members
        {
            get
            {
                var rawmembers = new List<ChatSessionMember>();

                if (!client.Self.GroupChatSessions.TryGetValue(group.ID, out rawmembers))
                {
                    if (State != ChannelState.Failed) { JoinGroupchat(); }

                    //TODO: More reliaaaable joins.
                    if (!client.Self.GroupChatSessions.TryGetValue(group.ID, out rawmembers))
                    {
                        rawmembers = new List<ChatSessionMember>();
                    }
                }

                System.Diagnostics.Debug.WriteLine("GroupChannel \"{0}\" has {1} member(s).", IrcName, rawmembers.Count);

                var outlist = new List<ChannelMembership>();

                client.Self.GroupChatSessions.Lock(d =>
                {
                    if (rawmembers.Count(i => i.AvatarKey == client.Self.AgentID) == 0)
                    {
                        var cm = new ChannelMembership();
                        cm.IsOperator = false;
                        cm.Position = PositionCategory.Talk;
                        cm.Subject = mapper.MapUser(client.Self.AgentID);
                        outlist.Add(cm);
                    }

                    foreach (var i in rawmembers)
                    {
                        var item = new ChannelMembership();
                        item.IsOperator = i.IsModerator;
                        item.Subject = mapper.MapUser(i.AvatarKey);
                        if(item.Subject != null)
                        {
                            outlist.Add(item);
                        }
                        else
                        {
                            var s = "ERROR: GroupChannel: Error getting name for {0}".Format(i.AvatarKey);
                            Console.WriteLine(s);
                            System.Diagnostics.Debug.WriteLine(s);
                        }
                    }
                });
                return outlist;
            }
                
        }

        public event ChannelMemberChangeHandler MembersChanged;

        public void SendMessage(IntermediateMessage msg)
        {
            if(!client.Self.GroupChatSessions.ContainsKey(group.ID))
            {
                bool success = JoinGroupchat();
                if(!success) return;
            }

            client.Self.InstantMessageGroup(msg.Sender.SlName, group.ID, msg.Payload);
        }

        public event ReceiveMessageHandler ReceiveMessage;

        public IChannel DownstreamChannel
        {
            get { return this; }
        }

        public void OnLocalChat(object o, OpenMetaverse.ChatEventArgs e)
        {
            throw new NotImplementedException();
        }

        public void SendMessageDownstream(IntermediateMessage msg)
        {
            if(msg.Sender.AvatarID == client.Self.AgentID && msg.Type != MessageType.ClientNotice && msg.Type != MessageType.ClientError)
            {
                return;
            }
            if(ReceiveMessage != null)
            {
                ReceiveMessage(this, msg);
            }
        }

        public ChannelState State
        {
            get
            {
                lock(syncRoot) return state;
            }
        }

        public void OnMemberJoined(UUID AgentId)
        {
            if (AgentId == client.Self.AgentID) return;
            var changes = new ChannelMemberChangeEventArgs();
            var newmember = new ChannelMemberChangeEventArgs.ChangeDetails();
            newmember.Subject = mapper.MapUser(AgentId);
            client.Self.GroupChatSessions.Lock(d =>
            {
                var cachedMembership = d[group.ID].FirstOrDefault(i => i.AvatarKey == AgentId);
                if (cachedMembership.AvatarKey == AgentId)
                {
                    newmember.IsOperator = cachedMembership.IsModerator;
                }
            });
            changes.NewMembers.Add(newmember);

            if (MembersChanged != null) { MembersChanged(this, changes); }
        }

        public void OnMemberLeft(UUID AgentId)
        {
            if (AgentId == client.Self.AgentID) return;
            var changes = new ChannelMemberChangeEventArgs();
            var oldmember = new ChannelMemberChangeEventArgs.ChangeDetails();
            oldmember.Subject = mapper.MapUser(AgentId);
            changes.RemovedMembers.Add(oldmember);
            if (MembersChanged != null) { MembersChanged(this, changes); }
        }

        private Task<bool> joinTask;
        private ChannelState oldState;
        public async Task<bool> Join()
        {
            lock(syncRoot)
            {
                if (joinTask == null)
                {
                    oldState = state;
                    state = ChannelState.Joining;
                    joinTask = client.Self.JoinGroupChatAsync(group.ID);
                }
            }

            var result = await joinTask;
            lock(syncRoot)
            {
                state = result ? ChannelState.Connected : ChannelState.Failed;
                if(!result && oldState == ChannelState.Unconnected)
                {
                    SendMessageDownstream(IntermediateMessage.ClientNotice(mapper.Client, "Could not join group " + SlName));
                }
            }
            return result;
        }

        private bool JoinGroupchat()
        {
            var task = Join();
            return task.Wait(5000) && task.Result;
        }
    }
}
