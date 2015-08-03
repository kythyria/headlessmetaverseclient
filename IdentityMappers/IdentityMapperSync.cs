using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Regex = System.Text.RegularExpressions.Regex;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessSlClient
{
    class IdentityMapper : IIdentityMapper
    {
        const string AGENTHOST = "agent.grid.sl";
        const string OBJECTHOST = "object.grid.sl";
        static readonly Regex reNonChannelChars = new Regex(@"[^-A-Za-z0-9\._ ]");

        string gridName;
        GridClient client;
        MappedIdentity gridIdentity;
        MappedIdentity clientIdentity;
        Dictionary<string, UUID> UsernameToKeyCache;
        Dictionary<UUID, string> KeyToUsernameCache;
        Dictionary<string, UUID> IrcToGroupCache;
        Dictionary<UUID, string> GroupToIrcCache;

        public IdentityMapper(string gridName, GridClient client)
        {
            this.client = client;
            this.gridName = gridName.CamelCase();
            this.UsernameToKeyCache = new Dictionary<string, UUID>();
            this.KeyToUsernameCache = new Dictionary<UUID, string>();
            this.IrcToGroupCache = new Dictionary<string, UUID>();
            this.GroupToIrcCache = new Dictionary<UUID, string>();

            gridIdentity = new MappedIdentity(IdentityCategory.System);
            gridIdentity.AvatarID = UUID.Zero;
            gridIdentity.IrcNick = "**SYSTEM**";
            gridIdentity.IrcFullId = gridIdentity.IrcNick + "!" + gridName + "@grid.sl";
            gridIdentity.SlName = gridName;

            clientIdentity = new MappedIdentity(IdentityCategory.System);
            clientIdentity.AvatarID = UUID.Zero;
            clientIdentity.IrcFullId = "local.sl";
        }

        public MappedIdentity MapUser(MappedIdentity Identity)
        {
            return Identity;
        }

        public MappedIdentity MapUser(string IrcName)
        {
            var identity = new MappedIdentity(IdentityCategory.Agent);
            identity.IrcNick = IrcName;
            identity.IrcFullId = IrcName + "!agent@" + AGENTHOST;
            identity.SlName = String.Join(" ", IrcName.Split('.'));
            
            if(UsernameToKeyCache.ContainsKey(IrcName))
            {
                identity.AvatarID = UsernameToKeyCache[identity.SlName];
            }
            else
            {
                identity.AvatarID = ResolveIdFromName(identity.SlName);
            }
            return identity;
        }

        public MappedIdentity MapUser(OpenMetaverse.UUID SlId, string SlName = null)
        {
            var identity = new MappedIdentity(IdentityCategory.Agent);
            identity.AvatarID = SlId;
            
            if(SlName != null && !UsernameToKeyCache.ContainsKey(SlName))
            {
                identity.SlName = SlName;
                UsernameToKeyCache[SlName] = SlId;
                KeyToUsernameCache[SlId] = SlName;
            }

            if(KeyToUsernameCache.ContainsKey(SlId))
            {
                identity.SlName = KeyToUsernameCache[SlId];
            }
            else
            {
                identity.SlName = ResolveNameFromId(SlId);
            }

            identity.IrcNick = MakeIrcName(identity.SlName, ".");
            identity.IrcFullId = identity.IrcNick + "!"+ SlId.ToString() +"@" + AGENTHOST;

            return identity;
        }

        public MappedIdentity MapObject(OpenMetaverse.UUID SlId, string SlName)
        {
            var identity = new MappedIdentity(IdentityCategory.Object);
            identity.SlName = SlName;
            identity.AvatarID = SlId;
            identity.IrcNick = MakeIrcName(SlName, ".");
            identity.IrcFullId = identity.IrcNick + "!object@" + OBJECTHOST;

            return identity;
        }

        public UUID MapChannelName(string IrcName)
        {
            return IrcToGroupCache[IrcName];
        }

        public string MapGroup(UUID group)
        {
            if (GroupToIrcCache.ContainsKey(group))
            {
                return GroupToIrcCache[group];
            }

            var result = group.ToString();
            var waiter = new System.Threading.AutoResetEvent(false);
            EventHandler<GroupNamesEventArgs> handler = null;
            handler = delegate(object o, GroupNamesEventArgs a)
            {
                if(a.GroupNames.ContainsKey(group))
                {
                    client.Groups.GroupNamesReply -= handler;
                    result = "#" + MakeIrcName(a.GroupNames[group]);
                    GroupToIrcCache[group] = result;
                    waiter.Set();
                }
            };

            client.Groups.GroupNamesReply += handler;
            client.Groups.RequestGroupName(group);
            waiter.WaitOne(20000);
            return result;
        }

        public string MapGroup(Group group)
        {
            if(GroupToIrcCache.ContainsKey(group.ID))
            {
                return GroupToIrcCache[group.ID];
            }
            var ircname = "#" + MakeIrcName(group.Name);
            this.GroupToIrcCache[group.ID] = ircname;
            this.IrcToGroupCache[ircname] = group.ID;
            return ircname;
        }

        public MappedIdentity Grid
        {
            get
            {
                return gridIdentity;
            }
        }

        public MappedIdentity Client { get { return clientIdentity; } }

        private string MakeIrcName(string input, string joiner = "")
        {
            var preparedInput = reNonChannelChars.Replace(input, "");
            return preparedInput.CamelCase(joiner);
        }

        private UUID ResolveIdFromName(string name)
        {
            UUID rid = UUID.Random();
            UUID result = UUID.Zero;

            var waiter = new System.Threading.AutoResetEvent(false);
            
            EventHandler<AvatarPickerReplyEventArgs> handler = null;
            handler = delegate(object o, AvatarPickerReplyEventArgs a)
            {
                if (a.QueryID == rid)
                {
                    client.Avatars.AvatarPickerReply -= handler;
                    foreach(var i in a.Avatars)
                    {
                        UsernameToKeyCache[i.Value] = i.Key;
                        KeyToUsernameCache[i.Key] = i.Value;
                    }
                    result = UsernameToKeyCache[name];
                    waiter.Set();
                }
            };
            client.Avatars.AvatarPickerReply += handler;

            client.Avatars.RequestAvatarNameSearch(name, rid);
            waiter.WaitOne(20000);
            client.Avatars.AvatarPickerReply -= handler;
            return result;
        }

        private string ResolveNameFromId(UUID key)
        {
            string result = key.ToString();

            var waiter = new System.Threading.AutoResetEvent(false);

            EventHandler<UUIDNameReplyEventArgs> handler = null;
            handler = delegate(object o, UUIDNameReplyEventArgs a)
            {
                if (a.Names.ContainsKey(key))
                {
                    client.Avatars.UUIDNameReply -= handler;
                    result = a.Names[key];
                    UsernameToKeyCache[result] = key;
                    KeyToUsernameCache[key] = result;
                    waiter.Set();
                }
            };
            client.Avatars.UUIDNameReply += handler;

            client.Avatars.RequestAvatarName(key);
            waiter.WaitOne(20000);
            client.Avatars.UUIDNameReply -= handler;
            return result;
        }
    }
}
