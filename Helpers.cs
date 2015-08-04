using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient
{
    static class Helpers
    {
        public static IEnumerable<IEnumerable<T>> ChunkTrivialBetter<T>(this IEnumerable<T> source, int chunksize)
        {
            var pos = 0;
            while (source.Skip(pos).Any())
            {
                yield return source.Skip(pos).Take(chunksize);
                pos += chunksize;
            }
        }

        public static String CamelCase(this string source, string joiner = "")
        {
            var components = source.Split(' ');
            return components.CamelCase(joiner);
        }

        public static string CamelCase(this IEnumerable<string> source, string joiner = "")
        {
            var words = source.Where(i=>i.Length > 0).Select(i => i.Substring(0, 1).ToUpper() + i.Substring(1).ToLower());
            return string.Join(joiner, words);
        }

        public static T WaitOrDefault<T>(this Task<T> self, int millisecondsTimeOut)
        {
            if(self.Wait(millisecondsTimeOut))
            {
                return self.Result;
            }
            else
            {
                return default(T);
            }
        }
    }
}
