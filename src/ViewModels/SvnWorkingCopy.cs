using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Local changes of a SVN working copy. SVN has no index, so the second list only records the changes
    ///     that will be included in the next commit (it is kept in memory).
    /// </summary>
    public class SvnWorkingCopy : ObservableObject
    {
        public SvnRepository Repository
        {
            get => _repo;
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    VisibleChanges = GetVisibleChanges(_changes);
                    VisibleIncluded = GetVisibleChanges(_included);
                    SelectedChanges = new(null);
                    SelectedIncluded = new(null);
                }
            }
        }

        public int Count
        {
            get => _cached.Count;
        }

        public List<Models.Change> VisibleChanges
        {
            get => _visibleChanges;
            private set
            {
                if (SetProperty(ref _visibleChanges, value))
                    OnPropertyChanged(nameof(ChangesCountInfo));
            }
        }

        public List<Models.Change> VisibleIncluded
        {
            get => _visibleIncluded;
            private set
            {
                if (SetProperty(ref _visibleIncluded, value))
                    OnPropertyChanged(nameof(IncludedCountInfo));
            }
        }

        public bool HasIncluded
        {
            get => _included.Count > 0;
        }

        public string ChangesCountInfo
        {
            get => string.IsNullOrEmpty(_filter) ? $"({_changes.Count})" : $"({_visibleChanges.Count}/{_changes.Count})";
        }

        public string IncludedCountInfo
        {
            get => string.IsNullOrEmpty(_filter) ? $"({_included.Count})" : $"({_visibleIncluded.Count}/{_included.Count})";
        }

        public ChangeSelection SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetProperty(ref _selectedChanges, value))
                {
                    if (value is { Count: > 0 } && _selectedIncluded is { Count: > 0 })
                    {
                        _isLoadingData = true;
                        SelectedIncluded = new(null);
                        _isLoadingData = false;
                    }

                    if (!_isLoadingData)
                        UpdateDetail();
                }
            }
        }

        public ChangeSelection SelectedIncluded
        {
            get => _selectedIncluded;
            set
            {
                if (SetProperty(ref _selectedIncluded, value))
                {
                    if (value is { Count: > 0 } && _selectedChanges is { Count: > 0 })
                    {
                        _isLoadingData = true;
                        SelectedChanges = new(null);
                        _isLoadingData = false;
                    }

                    if (!_isLoadingData)
                        UpdateDetail();
                }
            }
        }

        public object DetailContext
        {
            get => _detailContext;
            private set => SetProperty(ref _detailContext, value);
        }

        public string CommitMessage
        {
            get => _commitMessage;
            set => SetProperty(ref _commitMessage, value);
        }

        public bool IsCommitting
        {
            get => _isCommitting;
            private set => SetProperty(ref _isCommitting, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public SvnWorkingCopy(SvnRepository repo)
        {
            _repo = repo;
        }

        public void Cleanup()
        {
            _cached.Clear();
            _changes.Clear();
            _included.Clear();
            _visibleChanges.Clear();
            _visibleIncluded.Clear();
            _includedPaths.Clear();
            _detailContext = null;
        }

        public void ClearFilter()
        {
            Filter = string.Empty;
        }

        public void SetData(List<Models.SvnChange> changes)
        {
            _cached = changes;
            OnPropertyChanged(nameof(Count));
            Rebuild();
        }

        public void Include(List<Models.Change> changes)
        {
            foreach (var c in changes)
                _includedPaths.Add(c.Path);

            Rebuild();
        }

        public void Exclude(List<Models.Change> changes)
        {
            foreach (var c in changes)
                _includedPaths.Remove(c.Path);

            Rebuild();
        }

        public void IncludeAll()
        {
            foreach (var c in _visibleChanges)
                _includedPaths.Add(c.Path);

            Rebuild();
        }

        public void ExcludeAll()
        {
            _includedPaths.Clear();
            Rebuild();
        }

        public async Task AddAsync(List<Models.Change> changes)
        {
            var paths = new List<string>();
            foreach (var c in changes)
            {
                if (c is Models.SvnChange { IsUnversioned: true })
                    paths.Add(c.Path);
            }

            if (paths.Count == 0)
                return;

            IsBusy = true;
            using (_repo.LockWatcher())
            {
                var log = _repo.CreateLog("Add");
                await new Commands.SvnAdd(_repo.FullPath, paths) { Log = log }.RunAsync();
                log.Complete();
            }

            IsBusy = false;
            _repo.MarkWorkingCopyDirtyManually();
        }

        public async Task MarkAsDeletedAsync(List<Models.Change> changes)
        {
            var paths = new List<string>();
            foreach (var c in changes)
            {
                if (c is Models.SvnChange { IsMissing: true })
                    paths.Add(c.Path);
            }

            if (paths.Count == 0)
                return;

            IsBusy = true;
            using (_repo.LockWatcher())
            {
                var log = _repo.CreateLog("Delete");
                await new Commands.SvnDelete(_repo.FullPath, paths) { Log = log }.RunAsync();
                log.Complete();
            }

            IsBusy = false;
            _repo.MarkWorkingCopyDirtyManually();
        }

        public void Revert(List<Models.Change> changes)
        {
            var versioned = new List<Models.Change>();
            foreach (var c in changes)
            {
                if (c is Models.SvnChange { IsUnversioned: false })
                    versioned.Add(c);
            }

            if (versioned.Count > 0 && _repo.CanCreatePopup())
                _repo.ShowPopup(new SvnRevert(_repo, versioned));
        }

        public void DeleteUnversioned(List<Models.Change> changes)
        {
            var unversioned = new List<Models.Change>();
            foreach (var c in changes)
            {
                if (c is Models.SvnChange { IsUnversioned: true })
                    unversioned.Add(c);
            }

            if (unversioned.Count > 0 && _repo.CanCreatePopup())
                _repo.ShowPopup(new SvnDeleteUnversioned(_repo, unversioned));
        }

        public async Task CommitAsync()
        {
            if (_isCommitting)
                return;

            if (string.IsNullOrWhiteSpace(_commitMessage))
            {
                _repo.SendNotification(App.Text("Svn.Commit.EmptyMessage"), true);
                return;
            }

            var included = CollectChangesToCommit();
            if (included.Count == 0)
            {
                _repo.SendNotification(App.Text("Svn.Commit.NothingToCommit"), true);
                return;
            }

            IsCommitting = true;
            var succ = true;
            var log = _repo.CreateLog("Commit");

            using (_repo.LockWatcher())
            {
                var targets = new List<string>();
                var unversioned = new List<string>();
                var unversionedDirs = new List<string>();
                var missing = new List<string>();
                foreach (var c in included)
                {
                    targets.Add(c.Path);

                    if (c.IsUnversioned)
                    {
                        unversioned.Add(c.Path);
                        if (Directory.Exists(Path.Combine(_repo.FullPath, c.Path)))
                            unversionedDirs.Add($"{c.Path}/");
                    }
                    else if (c.IsMissing)
                    {
                        missing.Add(c.Path);
                    }
                }

                if (unversioned.Count > 0)
                    succ = await new Commands.SvnAdd(_repo.FullPath, unversioned) { Log = log }.RunAsync();

                // Contents of newly added directories should also be committed (we commit with `--depth empty`).
                if (succ && unversionedDirs.Count > 0)
                {
                    var changes = await new Commands.SvnQueryStatus(_repo.FullPath).GetResultAsync();
                    foreach (var c in changes)
                    {
                        if (c.WorkTree != Models.ChangeState.Added)
                            continue;

                        foreach (var dir in unversionedDirs)
                        {
                            if (c.Path.StartsWith(dir, StringComparison.Ordinal))
                            {
                                targets.Add(c.Path);
                                break;
                            }
                        }
                    }
                }

                if (succ && missing.Count > 0)
                    succ = await new Commands.SvnDelete(_repo.FullPath, missing) { Log = log }.RunAsync();

                if (succ)
                {
                    var commit = new Commands.SvnCommit(_repo.FullPath, targets, _commitMessage) { Log = log };
                    succ = await commit.RunAsync();
                    if (!succ && commit.IsAuthenticationFailed)
                        _repo.RequestAuthentication();
                }
            }

            log.Complete();

            if (succ)
            {
                foreach (var c in included)
                    _includedPaths.Remove(c.Path);

                CommitMessage = string.Empty;
                _repo.RefreshLog();
            }

            _repo.MarkWorkingCopyDirtyManually();
            IsCommitting = false;
        }

        private List<Models.SvnChange> CollectChangesToCommit()
        {
            var included = new List<Models.SvnChange>();
            var added = new HashSet<string>();
            foreach (var c in _cached)
            {
                if (_includedPaths.Contains(c.Path))
                {
                    included.Add(c);
                    added.Add(c.Path);
                }
            }

            // Parent folders that are not in repository yet must be committed together with their children.
            var count = included.Count;
            for (var i = 0; i < count; i++)
            {
                var dir = Path.GetDirectoryName(included[i].Path)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(dir))
                {
                    if (!added.Contains(dir))
                    {
                        var parent = _cached.Find(x => x.Path.Equals(dir, StringComparison.Ordinal));
                        if (parent is { WorkTree: Models.ChangeState.Added or Models.ChangeState.Untracked })
                        {
                            included.Add(parent);
                            added.Add(dir);
                        }
                    }

                    dir = Path.GetDirectoryName(dir)?.Replace('\\', '/');
                }
            }

            return included;
        }

        private void Rebuild()
        {
            var lastSelectedChanges = CollectPaths(_selectedChanges);
            var lastSelectedIncluded = CollectPaths(_selectedIncluded);

            var exists = new HashSet<string>();
            var changes = new List<Models.Change>();
            var included = new List<Models.Change>();
            foreach (var c in _cached)
            {
                exists.Add(c.Path);
                if (_includedPaths.Contains(c.Path))
                    included.Add(c);
                else
                    changes.Add(c);
            }

            // Forget the paths that are not changed any more.
            _includedPaths.RemoveWhere(x => !exists.Contains(x));

            var visibleChanges = GetVisibleChanges(changes);
            var visibleIncluded = GetVisibleChanges(included);

            _isLoadingData = true;
            _changes = changes;
            _included = included;
            VisibleChanges = visibleChanges;
            VisibleIncluded = visibleIncluded;
            OnPropertyChanged(nameof(ChangesCountInfo));
            OnPropertyChanged(nameof(IncludedCountInfo));
            OnPropertyChanged(nameof(HasIncluded));
            SelectedChanges = new(visibleChanges.FindAll(x => lastSelectedChanges.Contains(x.Path)));
            SelectedIncluded = new(visibleIncluded.FindAll(x => lastSelectedIncluded.Contains(x.Path)));
            _isLoadingData = false;

            UpdateDetail();
        }

        private void UpdateDetail()
        {
            Models.Change change = null;
            if (_selectedChanges is { Count: 1, HasFolder: false })
                change = _selectedChanges.Changes[0];
            else if (_selectedIncluded is { Count: 1, HasFolder: false })
                change = _selectedIncluded.Changes[0];

            if (change is Models.SvnChange svnChange)
                DetailContext = new SvnDiffContext(_repo.FullPath, svnChange, _detailContext as SvnDiffContext);
            else
                DetailContext = null;
        }

        private List<Models.Change> GetVisibleChanges(List<Models.Change> changes)
        {
            if (string.IsNullOrEmpty(_filter))
                return changes;

            return changes.FindAll(x => x.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        }

        private static HashSet<string> CollectPaths(ChangeSelection selection)
        {
            var set = new HashSet<string>();
            if (selection is { Count: > 0 })
            {
                foreach (var c in selection.Changes)
                    set.Add(c.Path);
            }

            return set;
        }

        private readonly SvnRepository _repo = null;
        private bool _isLoadingData = false;
        private bool _isCommitting = false;
        private bool _isBusy = false;
        private List<Models.SvnChange> _cached = [];
        private List<Models.Change> _changes = [];
        private List<Models.Change> _included = [];
        private List<Models.Change> _visibleChanges = [];
        private List<Models.Change> _visibleIncluded = [];
        private readonly HashSet<string> _includedPaths = [];
        private ChangeSelection _selectedChanges = new(null);
        private ChangeSelection _selectedIncluded = new(null);
        private object _detailContext = null;
        private string _filter = string.Empty;
        private string _commitMessage = string.Empty;
    }
}
