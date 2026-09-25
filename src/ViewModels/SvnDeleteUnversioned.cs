using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Deletes unversioned files/folders from disk.
    /// </summary>
    public class SvnDeleteUnversioned : Popup
    {
        public string Description
        {
            get;
        }

        public SvnDeleteUnversioned(SvnRepository repo, List<Models.Change> changes)
        {
            _repo = repo;
            _changes = changes;
            Description = changes.Count == 1 ? changes[0].Path : App.Text("Svn.DeleteUnversioned.Total", changes.Count);
        }

        public override Task<bool> Sure()
        {
            ProgressDescription = $"Delete total {_changes.Count} unversioned items ...";

            return Task.Run(() =>
            {
                using var lockWatcher = _repo.LockWatcher();
                foreach (var c in _changes)
                {
                    var fullpath = Path.Combine(_repo.FullPath, c.Path);

                    try
                    {
                        if (Directory.Exists(fullpath))
                            Directory.Delete(fullpath, true);
                        else if (File.Exists(fullpath))
                            File.Delete(fullpath);
                    }
                    catch (System.Exception e)
                    {
                        _repo.SendNotification($"Failed to delete '{c.Path}': {e.Message}", true);
                    }
                }

                _repo.MarkWorkingCopyDirtyManually();
                return true;
            });
        }

        private readonly SvnRepository _repo;
        private readonly List<Models.Change> _changes;
    }
}
