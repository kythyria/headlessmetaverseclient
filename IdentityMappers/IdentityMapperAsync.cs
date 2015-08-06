using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using Regex = System.Text.RegularExpressions.Regex;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessSlClient
{
    interface IAsyncIdentityMapper
    {
        Task<MappedIdentity> MapAgent(UUID AgentId, string SlName = null);
        Task<MappedIdentity> MapAgent(string IrcNick);
        Task<IEnumerable<MappedIdentity>> MapAgents(IEnumerable<UUID> AgentIds);

        Task<MappedIdentity> MapObject(UUID slId, string SlName = null);

        Task<UUID> MapGroup(string IrcName);
        Task<string> MapGroup(Group group);
        Task<string> MapGroup(UUID group);

        MappedIdentity Client { get; }
        MappedIdentity Grid { get; }
    }

    class IdentityMapperAsync : IAsyncIdentityMapper
    {
        class PendingRequest
        {
            public TaskCompletionSource<MappedIdentity> TaskSource;
            public UUID? AgentId;
            public string SlName;

            public PendingRequest(UUID AgentId)
            {
                this.TaskSource = new TaskCompletionSource<MappedIdentity>();
                this.AgentId = AgentId;
                this.SlName = null;
            }

            public PendingRequest(string SlName)
            {

                this.TaskSource = new TaskCompletionSource<MappedIdentity>();
                this.SlName = SlName;
                this.AgentId = null;
            }
        }

        GridClient client;
        string gridDomain;
        const string agentSubdomain = "agent";

        List<PendingRequest> agentRequests = new List<PendingRequest>();

        Dictionary<UUID, MappedIdentity> AgentsByUuid = new Dictionary<UUID,MappedIdentity>();
        Dictionary<string, MappedIdentity> AgentsByIrcNick = new Dictionary<string,MappedIdentity>();
        object agentCacheLock = new object();

        Dictionary<UUID, TaskCompletionSource<string>> groupRequests = new Dictionary<UUID, TaskCompletionSource<string>>();

        Dictionary<string, UUID> GroupIdByIrcName = new Dictionary<string, UUID>();
        Dictionary<UUID, string> GroupIrcNameByUuid = new Dictionary<UUID, string>();
        object groupCacheLock = new object();

        MappedIdentity grid;
        System.Lazy<MappedIdentity> clientIdentity;

        public IdentityMapperAsync(GridClient Client, string GridDomain)
        {
            client = Client;
            gridDomain = GridDomain;

            grid = new MappedIdentity(IdentityCategory.System);
            grid.AvatarID = UUID.Zero;
            grid.IrcDomain = gridDomain;
            grid.SlName = gridDomain;
            grid.IrcNick = gridDomain;

            clientIdentity = new System.Lazy<MappedIdentity>(() =>
            {
                return MakeAgentIdentity(client.Self.AgentID, client.Self.Name);
            });

            client.Avatars.UUIDNameReply += Avatars_UUIDNameReply;
            client.Groups.GroupNamesReply += Groups_GroupNamesReply;
        }

        public MappedIdentity Grid
        {
            get
            {
                return grid;
            }
        }

        public MappedIdentity Client
        {
            get
            {
                return clientIdentity.Value;
            }
        }

        public Task<MappedIdentity> MapAgent(UUID AgentId, string SlName = null)
        {
            MappedIdentity identity;
            lock (agentCacheLock)
            {
                if (AgentsByUuid.TryGetValue(AgentId, out identity))
                {
                    return Task.FromResult(identity);
                }
            }

            if(SlName != null)
            {
                return Task.FromResult(MakeAgentIdentity(AgentId, SlName));
            }

            PendingRequest request;
            lock(agentRequests)
            {
                request = new PendingRequest(AgentId);
                agentRequests.Add(request);
            }
            client.Avatars.RequestAvatarName(AgentId);
            return request.TaskSource.Task;
        }

        public Task<MappedIdentity> MapAgent(string IrcNick)
        {
            MappedIdentity identity;
            lock (agentCacheLock)
            {
                if (AgentsByIrcNick.TryGetValue(IrcNick, out identity))
                {
                    return Task.FromResult(identity);
                }
            }

            string slName = IrcNick.Replace('.', ' ');
            PendingRequest request;
            lock (agentRequests)
            {
                request = new PendingRequest(slName);
                agentRequests.Add(request);
            }
            client.Avatars.RequestAvatarNameSearch(slName, UUID.Random());
            return request.TaskSource.Task;
        }

        /// <summary>
        /// Resolve a whole bunch of IDs.
        /// </summary>
        /// <param name="AgentIds"></param>
        /// <returns></returns>
        public async Task<IEnumerable<MappedIdentity>> MapAgents(IEnumerable<UUID> AgentIds)
        {
            var newRequests = new List<Task<MappedIdentity>>();
            lock (agentCacheLock)
            {
                foreach (var i in AgentIds)
                {
                    MappedIdentity found;
                    if (AgentsByUuid.TryGetValue(i, out found))
                    {
                        newRequests.Add(Task.FromResult(found));
                    }
                    else
                    {
                        var req = new PendingRequest(i);
                        agentRequests.Add(req);
                        newRequests.Add(req.TaskSource.Task);
                    }
                }
            }
            client.Avatars.RequestAvatarNames(AgentIds.ToList());

            return await Task.WhenAll(newRequests);
        }

        public Task<MappedIdentity> MapObject(UUID slId, string SlName)
        {
            var identity = new MappedIdentity(IdentityCategory.Object);
            identity.SlName = SlName ?? "Object";
            identity.AvatarID = slId;
            identity.IrcNick = String.Join(".", SlName.Split(' ')); ;
            identity.IrcIdent = slId.ToString();
            identity.IrcDomain = "object." + gridDomain;

            return Task.FromResult(identity);
        }

        public Task<string> MapGroup(Group group)
        {
            lock (groupCacheLock)
            {
                string ircname;
                if (GroupIrcNameByUuid.TryGetValue(group.ID, out ircname))
                {
                    return Task.FromResult(GroupIrcNameByUuid[group.ID]);
                }
                ircname = "#" + MakeIrcName(group.Name);
                this.GroupIrcNameByUuid[group.ID] = ircname;
                this.GroupIdByIrcName[ircname] = group.ID;
                return Task.FromResult(ircname);
            }
        }

        public Task<UUID> MapGroup(string IrcName)
        {
            lock(groupCacheLock)
            {
                return Task.FromResult(GroupIdByIrcName[IrcName]);
            }
        }

        public Task<string> MapGroup(UUID group)
        {
            lock (groupCacheLock)
            {
                string groupName;
                if(GroupIrcNameByUuid.TryGetValue(group, out groupName))
                {
                    return Task.FromResult(groupName);
                }
            }

            lock(groupRequests)
            {
                groupRequests.Add(group, new TaskCompletionSource<string>());
                client.Groups.RequestGroupName(group);
                return groupRequests[group].Task;
            }
        }

        /// <summary>
        /// Make a MappedIdentity for an agent, completing any tasks that this fulfills.
        /// </summary>
        /// <param name="AgentId"></param>
        /// <param name="SlName"></param>
        /// <returns></returns>
        private MappedIdentity MakeAgentIdentity(UUID AgentId, string SlName)
        {
            var queries = new Dictionary<UUID, string>();
            queries.Add(AgentId, SlName);
            return MakeAgentIdentities(queries).First();
        }

        private IEnumerable<MappedIdentity> MakeAgentIdentities(Dictionary<UUID, string> queries)
        {
            var results = new List<MappedIdentity>();
            lock(agentCacheLock) {
                foreach(var i in queries)
                {
                    MappedIdentity newid;

                    if (!AgentsByUuid.TryGetValue(i.Key, out newid))
                    {
                        newid = new MappedIdentity(IdentityCategory.Agent);
                        newid.AvatarID = i.Key;
                        newid.SlName = i.Value;
                        newid.IrcNick = String.Join(".", i.Value.Split(' '));
                        newid.IrcIdent = i.Key.ToString();
                        newid.IrcDomain = agentSubdomain + "." + gridDomain;

                        AgentsByIrcNick[newid.IrcNick] = newid;
                        AgentsByUuid[i.Key] = newid;
                    }

                    results.Add(newid);
                }
            }

            lock (agentRequests)
            {
                var fulfilledRequests = from req in agentRequests
                                        from res in results
                                        where req.AgentId == res.AvatarID || req.SlName == res.SlName
                                        select new { Request = req, Response = res };
                foreach (var i in fulfilledRequests.ToList())
                {
                    agentRequests.Remove(i.Request);
                    i.Request.TaskSource.SetResult(i.Response);
                }
            }
            return results;
        }

        private void Avatars_UUIDNameReply(object sender, UUIDNameReplyEventArgs e)
        {
            MakeAgentIdentities(e.Names);
        }

        private void Groups_GroupNamesReply(object sender, GroupNamesEventArgs e)
        {
            lock(groupCacheLock)
            {
                foreach(var i in e.GroupNames)
                {
                    var name = "#" + MakeIrcName(i.Value);
                    GroupIdByIrcName[name] = i.Key;
                    GroupIrcNameByUuid[i.Key] = name;
                }

                lock (groupRequests)
                {
                    foreach(var i in groupRequests)
                    {
                        string name;
                        if(GroupIrcNameByUuid.TryGetValue(i.Key, out name))
                        {
                            i.Value.SetResult(name);
                            groupRequests.Remove(i.Key);
                        }
                    }
                }
            }
        }

        static readonly Regex reNonChannelChars = new Regex(@"[^-A-Za-z0-9\._ ]");
        private string MakeIrcName(string input, string joiner = "")
        {
            var preparedInput = reNonChannelChars.Replace(input, "");
            return preparedInput.CamelCase(joiner);
        }
    }
}
