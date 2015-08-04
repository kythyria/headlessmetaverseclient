using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient
{
    class SyncAsyncMapper : IIdentityMapper
    {
        IAsyncIdentityMapper asyncMapper;

        public SyncAsyncMapper(IAsyncIdentityMapper mapper)
        {
            asyncMapper = mapper;
        }

        public MappedIdentity MapUser(string IrcName)
        {
           var task = asyncMapper.MapAgent(IrcName);
           if(task.Wait(10000))
           {
               return task.Result;
           }
           else
           {
               return null;
           }
        }

        public MappedIdentity MapUser(OpenMetaverse.UUID SlId, string SlName = null)
        {
            return asyncMapper.MapAgent(SlId, SlName).WaitOrDefault(10000);
        }

        public MappedIdentity MapObject(OpenMetaverse.UUID SlId, string SlName = null)
        {
            return asyncMapper.MapObject(SlId, SlName).WaitOrDefault(10000);
        }

        public OpenMetaverse.UUID MapChannelName(string IrcName)
        {
            return asyncMapper.MapGroup(IrcName).WaitOrDefault(10000);
        }

        public string MapGroup(OpenMetaverse.UUID group)
        {
            return asyncMapper.MapGroup(group).WaitOrDefault(10000);
        }

        public string MapGroup(OpenMetaverse.Group group)
        {
            return asyncMapper.MapGroup(group).WaitOrDefault(10000);
        }

        public MappedIdentity Grid
        {
            get { return asyncMapper.Grid; }
        }

        public MappedIdentity Client
        {
            get { return asyncMapper.Client; }
        }
    }
}
