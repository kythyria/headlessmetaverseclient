using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient.Irc
{
    enum Numeric
    {
        RPL_WELCOME           = 001,
        RPL_YOURHOST          = 002,
        RPL_ISUPPORT          = 005,
        
        RPL_AWAY              = 301,
        RPL_USERHOST          = 302,
        RPL_ENDOFWHO          = 315,
        RPL_TOPIC             = 332,
        RPL_CHANCREATED       = 333,
        RPL_CHANNELMODEIS     = 324,
        RPL_WHOREPLY          = 352,
        RPL_NAMREPLY          = 353,
        RPL_ENDOFNAMES        = 366,
        RPL_ENDOFBANLIST      = 368,

        ERR_UNKNOWNSUBCOMMAND = 420,
        ERR_UNKNOWNCOMMAND    = 421,                     
        ERR_NEEDMOREPARAMS    = 461,
        ERR_CHANOPRIVSNEEDED  = 482,

        ERR_USERSDONTMATCH    = 502,

        RPL_MONONLINE         = 730,
        RPL_MONOFFLINE        = 731,
        RPL_MONLIST           = 732,
        RPL_ENDOFMONLIST      = 733,
        RPL_MONLISTFULL       = 734,
    }

}
