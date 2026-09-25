using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Schedules the given versioned paths (usually the missing files) for deletion.
    /// </summary>
    public class SvnDelete : SvnCommand
    {
        public SvnDelete(string wc, List<string> paths)
        {
            WorkingDirectory = wc;
            Context = wc;
            _paths = paths;
        }

        public async Task<bool> RunAsync()
        {
            var targets = WriteTargetsFile(_paths);
            Args = $"delete --targets {targets.Quoted()}";

            var succ = await ExecAsync().ConfigureAwait(false);
            DeleteTempFile(targets);
            return succ;
        }

        private readonly List<string> _paths;
    }
}
