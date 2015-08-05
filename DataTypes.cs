using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient
{
    enum PositionCategory
    {
        Whisper,
        Talk,
        Shout,
        SameRegionGroup,
        Distant
    }

    enum IdentityCategory
    {
        Object,
        Agent,
        System
    }

    class MappedIdentity
    {
        public IdentityCategory Type;
        public OpenMetaverse.UUID AvatarID;
        public string SlName;
        public string IrcNick;
        public string IrcIdent;
        public string IrcDomain;
        public string IrcFullId
        {
            get
            {
                var s = new StringBuilder();
                if(IrcNick != null)
                {
                    s.Append(IrcNick).Append("!");
                }
                if(IrcIdent != null)
                {
                    s.Append(IrcIdent).Append("@");
                }
                s.Append(IrcDomain);
                return s.ToString();
            }
        }

        public MappedIdentity(IdentityCategory identityCategory)
        {
            this.Type = identityCategory;
        }
    }
}
