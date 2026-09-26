using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Log of a SVN working copy.
    /// </summary>
    public class SvnHistories : ObservableObject
    {
        public const int PAGE_SIZE = 100;

        public SvnRepository Repository
        {
            get => _repo;
        }

        public List<Models.SvnRevision> Revisions
        {
            get => _revisions;
            private set => SetProperty(ref _revisions, value);
        }

        public Models.SvnRevision SelectedRevision
        {
            get => _selectedRevision;
            set
            {
                if (SetProperty(ref _selectedRevision, value))
                {
                    DiffContext = null;
                    Changes = value?.ToChanges(GetBaseDir()) ?? [];
                    SelectedChanges = new(null);
                }
            }
        }

        public List<Models.Change> Changes
        {
            get => _changes;
            private set => SetProperty(ref _changes, value);
        }

        public ChangeSelection SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetProperty(ref _selectedChanges, value))
                    UpdateDiffContext();
            }
        }

        public SvnDiffContext DiffContext
        {
            get => _diffContext;
            private set => SetProperty(ref _diffContext, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public bool HasMore
        {
            get => _hasMore;
            private set => SetProperty(ref _hasMore, value);
        }

        public string LoadError
        {
            get => _loadError;
            private set => SetProperty(ref _loadError, value);
        }

        public SvnHistories(SvnRepository repo)
        {
            _repo = repo;
        }

        public void Cleanup()
        {
            _cancellation?.Cancel();
            _revisions = [];
            _changes = [];
            _selectedRevision = null;
            _diffContext = null;
        }

        public void Reload()
        {
            Load(0, false);
        }

        public void LoadMore()
        {
            if (_isLoading || !_hasMore || _revisions.Count == 0)
                return;

            var last = _revisions[^1].Revision;
            if (last <= 1)
            {
                HasMore = false;
                return;
            }

            Load(last - 1, true);
        }

        /// <summary>
        ///     Converts a displayed path (of a change or a folder in changes tree) to the path relative to repository root.
        ///     NOTE: paths outside the opened folder are displayed with the leading '/'. The empty folder is its root.
        /// </summary>
        public string GetRepositoryPath(string displayPath)
        {
            if (string.IsNullOrEmpty(displayPath) || displayPath.StartsWith('/'))
                return displayPath?.TrimStart('/') ?? string.Empty;

            var baseDir = GetBaseDir();
            return string.IsNullOrEmpty(baseDir) ? displayPath : $"{baseDir}/{displayPath}";
        }

        /// <summary>
        ///     Returns the local path of a displayed path, or null if it is outside the opened folder.
        /// </summary>
        public string GetLocalPath(string displayPath)
        {
            if (string.IsNullOrEmpty(displayPath) || displayPath.StartsWith('/'))
                return null;

            return Native.OS.GetAbsPath(_repo.FullPath, displayPath);
        }

        /// <summary>
        ///     Returns the (URL-decoded) URL of the given path relative to repository root.
        /// </summary>
        public string GetUrl(string repoPath)
        {
            var root = _repo.Info?.RepositoryRoot;
            if (string.IsNullOrEmpty(root))
                return string.Empty;

            root = Uri.UnescapeDataString(root).TrimEnd('/');
            return string.IsNullOrEmpty(repoPath) ? root : $"{root}/{repoPath}";
        }

        public void RevertToRevision(Models.SvnRevision revision)
        {
            if (_repo.CanCreatePopup())
                _repo.ShowPopup(new SvnRevertToRevision(_repo, revision));
        }

        public async Task OpenRevisionFileAsync(Models.SvnRevisionChange change, Models.ExternalTool tool)
        {
            var root = _repo.Info?.RepositoryRoot;
            if (_selectedRevision == null || string.IsNullOrEmpty(root))
                return;

            var revision = _selectedRevision.Revision;
            var fileName = Path.GetFileNameWithoutExtension(change.RepositoryPath) ?? "";
            var fileExt = Path.GetExtension(change.RepositoryPath) ?? "";
            var tmpFile = Path.Combine(Path.GetTempPath(), $"{fileName}~r{revision}{fileExt}");

            var url = Commands.SvnCommand.BuildRepositoryUrl(root, change.RepositoryPath, revision);
            var succ = await new Commands.SvnSaveRevisionFile(_repo.FullPath, url)
                .SaveAsync(tmpFile)
                .ConfigureAwait(false);
            if (!succ)
                return;

            if (tool == null)
                Native.OS.OpenWithDefaultEditor(tmpFile);
            else
                tool.Launch(tmpFile.Quoted());
        }

        public async Task SaveChangesAsPatchAsync(List<Models.Change> changes, string saveTo)
        {
            var root = _repo.Info?.RepositoryRoot;
            if (_selectedRevision == null || string.IsNullOrEmpty(root))
                return;

            var revisionChanges = new List<Models.SvnRevisionChange>();
            foreach (var c in changes)
            {
                if (c is Models.SvnRevisionChange rc)
                    revisionChanges.Add(rc);
            }

            var succ = await new Commands.SvnSaveChangesAsPatch(_repo.FullPath, root, _selectedRevision.Revision, revisionChanges)
                .SaveAsync(saveTo);
            if (succ)
                _repo.SendNotification(App.Text("SaveAsPatchSuccess"));
        }

        private void Load(long start, bool append)
        {
            _cancellation?.Cancel();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            IsLoading = true;

            Task.Run(async () =>
            {
                var query = new Commands.SvnQueryLog(_repo.FullPath, start, PAGE_SIZE) { CancellationToken = token };
                var revisions = await query.GetResultAsync().ConfigureAwait(false);
                if (token.IsCancellationRequested)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    IsLoading = false;
                    LoadError = query.QueryError;

                    if (!string.IsNullOrEmpty(query.QueryError))
                    {
                        if (Commands.SvnCommand.IsAuthError(query.QueryError))
                            _repo.RequestAuthentication();
                        return;
                    }

                    HasMore = revisions.Count >= PAGE_SIZE;

                    var selected = _selectedRevision?.Revision ?? -1;
                    if (append)
                    {
                        var all = new List<Models.SvnRevision>(_revisions);
                        all.AddRange(revisions);
                        Revisions = all;
                        return;
                    }

                    Revisions = revisions;
                    if (selected >= 0)
                        SelectedRevision = revisions.Find(x => x.Revision == selected);
                });
            }, token);
        }

        private void UpdateDiffContext()
        {
            var root = _repo.Info?.RepositoryRoot;
            if (_selectedRevision == null ||
                string.IsNullOrEmpty(root) ||
                _selectedChanges is not { Count: 1, HasFolder: false } ||
                _selectedChanges.Changes[0] is not Models.SvnRevisionChange change)
            {
                DiffContext = null;
                return;
            }

            DiffContext = new SvnDiffContext(_repo.FullPath, root, _selectedRevision.Revision, change, _diffContext);
        }

        /// <summary>
        ///     Path of the opened folder relative to repository root. Changed files inside it are shown relatively.
        /// </summary>
        private string GetBaseDir()
        {
            var relative = _repo.Info?.RelativeUrl;
            if (string.IsNullOrEmpty(relative) || !relative.StartsWith("^/", StringComparison.Ordinal))
                return string.Empty;

            return relative.Substring(2).Trim('/');
        }

        private readonly SvnRepository _repo = null;
        private CancellationTokenSource _cancellation = null;
        private List<Models.SvnRevision> _revisions = [];
        private Models.SvnRevision _selectedRevision = null;
        private List<Models.Change> _changes = [];
        private ChangeSelection _selectedChanges = new(null);
        private SvnDiffContext _diffContext = null;
        private bool _isLoading = false;
        private bool _hasMore = false;
        private string _loadError = string.Empty;
    }
}
