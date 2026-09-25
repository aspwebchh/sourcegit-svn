using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class SvnAdd : SvnCommand
    {
        public SvnAdd(string wc, List<string> paths)
        {
            WorkingDirectory = wc;
            Context = wc;
            _paths = paths;
        }

        public async Task<bool> RunAsync()
        {
            var targets = WriteTargetsFile(_paths);
            Args = $"add --force --depth infinity --targets {targets.Quoted()}";

            var succ = await ExecAsync().ConfigureAwait(false);
            DeleteTempFile(targets);
            return succ;
        }

        private readonly List<string> _paths;
    }
}
