using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class SvnRevert : Popup
    {
        public string Description
        {
            get;
        }

        public SvnRevert(SvnRepository repo, List<Models.Change> changes)
        {
            _repo = repo;
            _changes = changes;
            Description = changes.Count == 1 ? changes[0].Path : App.Text("Svn.Revert.Total", changes.Count);
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = $"Revert total {_changes.Count} changes ...";

            var log = _repo.CreateLog("Revert");
            Use(log);

            var paths = new List<string>();
            foreach (var c in _changes)
                paths.Add(c.Path);

            await new Commands.SvnRevert(_repo.FullPath, paths) { Log = log }.RunAsync();
            log.Complete();

            _repo.MarkWorkingCopyDirtyManually();
            return true;
        }

        private readonly SvnRepository _repo;
        private readonly List<Models.Change> _changes;
    }
}
