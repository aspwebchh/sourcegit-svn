using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Runs `svn update` and shows every item updated during the progress.
    /// </summary>
    public class SvnUpdateProgress : ObservableObject, Models.ICommandLogReceiver
    {
        public string Url
        {
            get => Uri.UnescapeDataString(_repo.Info?.Url ?? string.Empty);
        }

        public string TargetRevisionName
        {
            get => _revision > 0 ? $"r{_revision}" : "HEAD";
        }

        public ObservableCollection<Models.SvnUpdateEntry> Entries
        {
            get;
        } = new ObservableCollection<Models.SvnUpdateEntry>();

        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        public bool IsFailed
        {
            get => _isFailed;
            private set => SetProperty(ref _isFailed, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string Summary
        {
            get => App.Text("Svn.UpdateProgress.Summary", _addedCount, _deletedCount, _updatedCount, _conflictedCount);
        }

        public bool HasConflicts
        {
            get => _conflictedCount > 0;
        }

        public SvnUpdateProgress(SvnRepository repo, long revision)
        {
            _repo = repo;
            _revision = revision;
        }

        public async Task StartAsync()
        {
            if (_isStarted)
                return;

            _isStarted = true;
            IsRunning = true;
            StatusMessage = App.Text("Svn.UpdateProgress.Running");

            var log = _repo.CreateLog("Update");
            log.Subscribe(this);

            var cmd = new Commands.SvnUpdate(_repo.FullPath, _revision)
            {
                RaiseError = false,
                CancellationToken = _cancellation.Token,
            };

            var succ = await _repo.ExecUpdateAsync(cmd, log);
            if (_cancellation.IsCancellationRequested)
            {
                IsFailed = true;
                StatusMessage = App.Text("Svn.UpdateProgress.Canceled");
            }
            else if (succ)
            {
                var revision = _finalRevision >= 0 ? $"r{_finalRevision}" : TargetRevisionName;
                StatusMessage = App.Text(_fileCount > 0 ? "Svn.UpdateProgress.Succeeded" : "Svn.UpdateProgress.UpToDate", revision);
            }
            else
            {
                if (!_hasErrorEntry && !string.IsNullOrEmpty(cmd.Error))
                {
                    foreach (var line in cmd.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        Entries.Add(new Models.SvnUpdateEntry() { Action = Models.SvnUpdateAction.Error, Path = line.Trim() });
                }

                IsFailed = true;
                StatusMessage = cmd.IsAuthenticationFailed ? App.Text("Svn.Authenticate.Required") : App.Text("Svn.UpdateProgress.Failed");
            }

            IsRunning = false;
        }

        public void Cancel()
        {
            if (_isRunning && !_cancellation.IsCancellationRequested)
            {
                _cancellation.Cancel();
                StatusMessage = App.Text("Svn.UpdateProgress.Canceling");
            }
        }

        public void OnReceiveCommandLog(string line)
        {
            var entry = Models.SvnUpdateEntry.Parse(line);
            if (entry == null)
                return;

            switch (entry.Action)
            {
                case Models.SvnUpdateAction.Added:
                    _addedCount++;
                    break;
                case Models.SvnUpdateAction.Deleted:
                    _deletedCount++;
                    break;
                case Models.SvnUpdateAction.Conflicted:
                    _conflictedCount++;
                    OnPropertyChanged(nameof(HasConflicts));
                    break;
                case Models.SvnUpdateAction.Skipped:
                    break;
                case Models.SvnUpdateAction.Message:
                    var rev = Models.SvnUpdateEntry.ParseFinalRevision(entry.Path);
                    if (rev >= 0)
                        _finalRevision = rev;
                    break;
                case Models.SvnUpdateAction.Error:
                    _hasErrorEntry = true;
                    break;
                default:
                    _updatedCount++;
                    break;
            }

            if (entry.IsFile)
            {
                _fileCount++;
                OnPropertyChanged(nameof(Summary));
            }

            Entries.Add(entry);
        }

        private readonly SvnRepository _repo;
        private readonly long _revision;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _isStarted = false;
        private bool _isRunning = false;
        private bool _isFailed = false;
        private bool _hasErrorEntry = false;
        private string _statusMessage = string.Empty;
        private long _finalRevision = -1;
        private int _fileCount = 0;
        private int _addedCount = 0;
        private int _deletedCount = 0;
        private int _updatedCount = 0;
        private int _conflictedCount = 0;
    }
}
