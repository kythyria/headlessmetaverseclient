using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HeadlessMetaverseClient
{
    class Argv
    {
        static readonly Regex namedArgMatcher = new Regex("^--([^=]+)(=(.*))?$");

        public List<string> PositionalArgs = new List<string>();
        public Dictionary<string, List<string>> NamedArgs = new Dictionary<string, List<string>>();

        public Argv(IEnumerable<string> argv)
        {
            bool namedEnabled = true;
            foreach (var i in argv)
            {
                if (i == "--")
                {
                    namedEnabled = false;
                    continue;
                }

                if (namedEnabled)
                {
                    var m = namedArgMatcher.Match(i);
                    if (m.Success)
                    {
                        var key = m.Groups[1].Value;
                        var value = m.Groups[2].Success ? m.Groups[3].Value : "true";

                        if (!NamedArgs.ContainsKey(key))
                        {
                            NamedArgs.Add(key, new List<string>());
                        }
                        NamedArgs[key].Add(value);
                        continue;
                    }
                }

                PositionalArgs.Add(i);
            }
        }

        public bool BooleanArg(string name, bool defaultValue)
        {
            bool result = defaultValue;
            List<string> args;
            if(NamedArgs.TryGetValue(name, out args))
            {
                foreach(var i in args)
                {
                    if(i == "false" || i == "off" || i == "no" || i == "0")
                    {
                        result = false;
                    }
                    else { result = true; }
                }
            }
            return result;
        }
    }

    class Configuration
    {
        public bool PresenceNotices { get; set; }
        
        public Configuration()
        {
            PresenceNotices = false;
        }

        public Configuration(IEnumerable<string> argv) : this()
        {
            var parsed = new Argv(argv);
            PresenceNotices = parsed.BooleanArg("presence-notices", PresenceNotices);
        }
    }
}
