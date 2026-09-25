using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class SvnCommit : SvnCommand
    {
        public SvnCommit(string wc, List<string> paths, string message)
        {
            WorkingDirectory = wc;
            Context = wc;
            _paths = paths;
            _message = message;
        }

        public async Task<bool> RunAsync()
        {
            var targets = WriteTargetsFile(_paths);
            var messageFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(messageFile, _message, new UTF8Encoding(false)).ConfigureAwait(false);

            // `--depth empty` makes sure that only the selected items are committed.
            Args = $"commit --depth empty -F {messageFile.Quoted()} --encoding utf-8 --targets {targets.Quoted()}";

            var succ = await ExecAsync().ConfigureAwait(false);
            DeleteTempFile(targets);
            DeleteTempFile(messageFile);
            return succ;
        }

        private readonly List<string> _paths;
        private readonly string _message;
    }
}
