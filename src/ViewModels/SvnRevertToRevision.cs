using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Reverts the working copy to the given revision by a reverse merge. The result is kept as local modifications.
    /// </summary>
    public class SvnRevertToRevision : Popup
    {
        public string CurrentRevision
        {
            get => $"r{_from}";
        }

        public Models.SvnRevision Revision
        {
            get;
        }

        public bool HasLocalChanges
        {
            get;
        }

        public SvnRevertToRevision(SvnRepository repo, Models.SvnRevision revision)
        {
            _repo = repo;
            _from = repo.Info?.Revision ?? 0;
            Revision = revision;
            HasLocalChanges = repo.WorkingCopy is { Count: > 0 };
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = $"Reverting to {Revision.RevisionName} ...";

            var log = _repo.CreateLog("Revert to Revision");
            Use(log);

            var cmd = new Commands.SvnMerge(_repo.FullPath, _from, Revision.Revision) { Log = log };
            var succ = await cmd.ExecAsync();
            log.Complete();

            _repo.MarkWorkingCopyDirtyManually();

            if (succ)
            {
                _repo.SelectedViewIndex = 1;
                _repo.SendNotification(App.Text("Svn.RevertToRevision.Success", Revision.RevisionName));
            }
            else if (cmd.IsAuthenticationFailed)
            {
                _repo.RequestAuthentication();
            }

            return true;
        }

        private readonly SvnRepository _repo;
        private readonly long _from;
    }
}
