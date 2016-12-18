using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessMetaverseClient
{
    class SyncAsyncMapper : IIdentityMapper
    {
        const int TIMEOUT = 50000;
        IAsyncIdentityMapper asyncMapper;

        public SyncAsyncMapper(IAsyncIdentityMapper mapper)
        {
            asyncMapper = mapper;
        }

        public MappedIdentity MapUser(string IrcName)
        {
           var task = asyncMapper.MapAgent(IrcName);
           if(task.Wait(TIMEOUT))
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
            return asyncMapper.MapAgent(SlId, SlName).WaitOrDefault(TIMEOUT);
        }

        public MappedIdentity MapObject(OpenMetaverse.UUID SlId, string SlName = null)
        {
            return asyncMapper.MapObject(SlId, SlName).WaitOrDefault(TIMEOUT);
        }

        public OpenMetaverse.UUID MapChannelName(string IrcName)
        {
            return asyncMapper.MapGroup(IrcName).WaitOrDefault(TIMEOUT);
        }

        public string MapGroup(OpenMetaverse.UUID group)
        {
            return asyncMapper.MapGroup(group).WaitOrDefault(TIMEOUT);
        }

        public string MapGroup(OpenMetaverse.Group group)
        {
            return asyncMapper.MapGroup(group).WaitOrDefault(TIMEOUT);
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
