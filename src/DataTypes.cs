using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessMetaverseClient
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

    class MappedIdentity : IFormattable
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

        override public string ToString()
        {
            var output = new StringBuilder("{");
            switch(Type)
            {
                case IdentityCategory.Agent:
                    output.Append("AgentIdentity");
                    break;
                case IdentityCategory.Object:
                    output.Append("ObjectIdentity");
                    break;
                case IdentityCategory.System:
                    output.Append("SystemIdentity}");
                    return output.ToString();
            }
            output.Append(":");
            output.Append((String.IsNullOrWhiteSpace(SlName) ? AvatarID.ToString() : SlName));
            return output.ToString();
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (String.IsNullOrEmpty(format)) { return ToString(); }

            switch(format.ToLowerInvariant())
            {
                case "ircnick":
                    return IrcNick;
                case "slname":
                    return SlName;
                default:
                    return ToString();
            }
        }
    }
}
