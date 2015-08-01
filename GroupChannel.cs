﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessSlClient
{
    class GroupChannel : IChannel, IUpstreamChannel
    {
        string slName, ircName, topic;
        IIdentityMapper mapper;
        GridClient client;
        Group group;

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
                /*List<ChatSessionMember> rawmembers;
                if(!client.Self.GroupChatSessions.TryGetValue(group.ID, out rawmembers))
                {
                    JoinGroupchat();
                    rawmembers = client.Self.GroupChatSessions[group.ID];
                }

                var outlist = new List<ChannelMembership>();
                foreach (var i in rawmembers)
                {
                    var item = new ChannelMembership();
                    item.IsOperator = i.IsModerator;
                    item.Subject = mapper.MapUser(i.AvatarKey);
                    outlist.Add(item);
                }
                return outlist;*/
                var cm = new ChannelMembership();
                cm.IsOperator = false;
                cm.Position = PositionCategory.Talk;
                cm.Subject = mapper.MapUser(client.Self.AgentID);
                var list = new List<ChannelMembership>();
                list.Add(cm);
                return list;
            }
                
        }

        private void BeginSession()
        {
            throw new NotImplementedException();
        }

        public event ChannelMemberChangeHandler MembersChanged;

        public void SendMessage(IntermediateMessage msg)
        {
            if(!client.Self.GroupChatSessions.ContainsKey(group.ID))
            {
                bool success = JoinGroupchat();
                if(!success)
                {
                    SendMessageDownstream(IntermediateMessage.ClientNotice(mapper.Client, "Could not join groupchat"));
                    return;
                }
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
            if(msg.Sender.AvatarID == client.Self.AgentID)
            {
                return;
            }
            if(ReceiveMessage != null)
            {
                ReceiveMessage(this, msg);
            }
        }

        private bool JoinGroupchat()
        {
            var waiter = new System.Threading.AutoResetEvent(false);

            EventHandler<GroupChatJoinedEventArgs> handler;
            handler = delegate(object o, GroupChatJoinedEventArgs e)
            {
                if(e.SessionID == group.ID)
                {
                    waiter.Set();
                }
            };
            client.Self.GroupChatJoined += handler;
            client.Self.RequestJoinGroupChat(group.ID);
            bool success = waiter.WaitOne(10000);
            client.Self.GroupChatJoined -= handler;
            return success;
        }
    }
}