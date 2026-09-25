using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Saves the changes of the given files in a revision as a patch (git format, paths are relative to repository root).
    /// </summary>
    public class SvnSaveChangesAsPatch : SvnCommand
    {
        public SvnSaveChangesAsPatch(string wc, string repositoryRoot, long revision, List<Models.SvnRevisionChange> changes)
        {
            WorkingDirectory = wc;
            Context = wc;

            _repositoryRoot = repositoryRoot;
            _revision = revision;
            _changes = changes;
        }

        public async Task<bool> SaveAsync(string saveTo)
        {
            try
            {
                await using var writer = File.Create(saveTo);
                foreach (var change in _changes)
                {
                    var peg = change.WorkTree == Models.ChangeState.Deleted ? _revision - 1 : _revision;
                    var url = BuildRepositoryUrl(_repositoryRoot, change.RepositoryPath, peg);
                    Args = $"diff --git --internal-diff -c {_revision} {url.Quoted()}";

                    var (succ, data, err) = await ReadBytesAsync().ConfigureAwait(false);
                    if (!succ)
                    {
                        Models.Notification.Send(Context, string.IsNullOrWhiteSpace(err) ? "Failed to save changes as patch" : err.Trim(), true);
                        return false;
                    }

                    await writer.WriteAsync(data).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Models.Notification.Send(Context, $"Failed to save changes as patch: {e.Message}", true);
                return false;
            }

            return true;
        }

        private readonly string _repositoryRoot;
        private readonly long _revision;
        private readonly List<Models.SvnRevisionChange> _changes;
    }
}
