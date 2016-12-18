using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMetaverse;

namespace HeadlessMetaverseClient
{
    static class AsyncWrappers
    {
        private static Dictionary<UUID, List<TaskCompletionSource<bool>>> pendingGroupchatJoins
            = new Dictionary<UUID, List<TaskCompletionSource<bool>>>();
        public static Task<bool> JoinGroupChatAsync(this AgentManager self, UUID GroupId)
        {
            var task = new TaskCompletionSource<bool>();
            lock(pendingGroupchatJoins)
            {
                List<TaskCompletionSource<bool>> list = null;
                if(!pendingGroupchatJoins.TryGetValue(GroupId, out list))
                {
                    list = new List<TaskCompletionSource<bool>>();
                   pendingGroupchatJoins[GroupId] = list;
                }
                list.Add(task);

                self.GroupChatJoined -= OnJoinGroupChat;
                self.GroupChatJoined += OnJoinGroupChat;
            }

            self.RequestJoinGroupChat(GroupId);

            return task.Task;

        }

        private static void OnJoinGroupChat(object o, GroupChatJoinedEventArgs e)
        {
            lock(pendingGroupchatJoins)
            {
                List<TaskCompletionSource<bool>> list;
                if(pendingGroupchatJoins.TryGetValue(e.SessionID, out list))
                {
                    list.ForEach(i=> i.SetResult(e.Success));
                    pendingGroupchatJoins.Remove(e.SessionID);
                }
            }
        }
    }
}
