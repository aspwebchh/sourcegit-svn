using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     A SVN working copy (or one of its sub-folders) opened in a launcher page.
    /// </summary>
    public class SvnRepository : ObservableObject
    {
        public string FullPath
        {
            get;
        }

        public Models.SvnInfo Info
        {
            get => _info;
            private set
            {
                if (SetProperty(ref _info, value))
                {
                    OnPropertyChanged(nameof(RelativeUrl));
                    OnPropertyChanged(nameof(RevisionName));
                }
            }
        }

        public string RelativeUrl
        {
            get => _info?.RelativeUrl ?? string.Empty;
        }

        public string RevisionName
        {
            get => _info != null ? $"r{_info.Revision}" : string.Empty;
        }

        public int SelectedViewIndex
        {
            get => _selectedViewIndex;
            set
            {
                if (SetProperty(ref _selectedViewIndex, value))
                {
                    OnPropertyChanged(nameof(IsHistoriesVisible));
                    OnPropertyChanged(nameof(IsWorkingCopyVisible));
                }
            }
        }

        public bool IsHistoriesVisible
        {
            get => _selectedViewIndex == 0;
        }

        public bool IsWorkingCopyVisible
        {
            get => _selectedViewIndex == 1;
        }

        public bool IsUpdating
        {
            get => _isUpdating;
            private set => SetProperty(ref _isUpdating, value);
        }

        public SvnHistories Histories
        {
            get;
            private set;
        }

        public SvnWorkingCopy WorkingCopy
        {
            get;
            private set;
        }

        public SvnRepository(string path)
        {
            FullPath = path;
        }

        public void Open()
        {
            Histories = new SvnHistories(this);
            WorkingCopy = new SvnWorkingCopy(this);

            try
            {
                var wcRoot = Commands.SvnQueryInfo.FindWorkingCopyRoot(FullPath) ?? FullPath;
                _watcher = new Models.SvnWatcher(FullPath, wcRoot, RefreshWorkingCopyChanges, OnDatabaseChanged);
            }
            catch (Exception ex)
            {
                SendNotification($"Failed to start watcher for SVN working copy: '{FullPath}'. You may need to press 'F5' to refresh manually!\n{ex.Message}", true);
                _watcher = null;
            }

            _selectedViewIndex = 1;
            RefreshAll();
        }

        public void Close()
        {
            _watcher?.Dispose();
            _watcher = null;

            _cancellationRefreshInfo?.Cancel();
            _cancellationRefreshWorkingCopy?.Cancel();
            Histories?.Cleanup();
            WorkingCopy?.Cleanup();
        }

        public void RefreshAll()
        {
            RefreshInfo();
            RefreshWorkingCopyChanges();
            Histories?.Reload();
        }

        public void RefreshInfo()
        {
            _cancellationRefreshInfo?.Cancel();
            _cancellationRefreshInfo = new CancellationTokenSource();
            var token = _cancellationRefreshInfo.Token;

            Task.Run(async () =>
            {
                var info = await new Commands.SvnQueryInfo(FullPath) { CancellationToken = token }
                    .GetResultAsync()
                    .ConfigureAwait(false);

                if (token.IsCancellationRequested)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (info != null)
                        Info = info;
                });
            }, token);
        }

        public void RefreshWorkingCopyChanges()
        {
            _cancellationRefreshWorkingCopy?.Cancel();
            _cancellationRefreshWorkingCopy = new CancellationTokenSource();
            var token = _cancellationRefreshWorkingCopy.Token;

            Task.Run(async () =>
            {
                _watcher?.IgnoreDatabaseChanges(2);

                var changes = await new Commands.SvnQueryStatus(FullPath) { CancellationToken = token }
                    .GetResultAsync()
                    .ConfigureAwait(false);

                _watcher?.IgnoreDatabaseChanges(1);

                if (token.IsCancellationRequested)
                    return;

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    WorkingCopy?.SetData(changes);
                    GetOwnerPage()?.ChangeDirtyState(Models.DirtyState.HasLocalChanges, changes.Count == 0);
                });
            }, token);
        }

        public void RefreshLog()
        {
            Histories?.Reload();
        }

        public IDisposable LockWatcher()
        {
            return _watcher?.Lock();
        }

        /// <summary>
        ///     Called after an operation that changes the working copy (commit, update, revert...) finished.
        /// </summary>
        public void MarkWorkingCopyDirtyManually()
        {
            RefreshInfo();
            RefreshWorkingCopyChanges();
        }

        public void SendNotification(string message, bool isError = false)
        {
            Models.Notification.Send(FullPath, message, isError);
        }

        public bool CanCreatePopup()
        {
            var page = GetOwnerPage();
            return page != null && page.CanCreatePopup();
        }

        public void ShowPopup(Popup popup)
        {
            var page = GetOwnerPage();
            if (page != null)
                page.Popup = popup;
        }

        public async Task ShowAndStartPopupAsync(Popup popup)
        {
            var page = GetOwnerPage();
            if (page == null)
                return;

            page.Popup = popup;
            if (popup.CanStartDirectly())
                await page.ProcessPopupAsync();
        }

        /// <summary>
        ///     Updates the working copy to HEAD with a progress dialog, or shows a popup to choose the target revision.
        /// </summary>
        public async Task UpdateAsync(bool chooseRevision)
        {
            if (chooseRevision)
            {
                if (CanCreatePopup())
                    ShowPopup(new SvnUpdate(this));
                return;
            }

            await ShowUpdateProgressAsync(0);
        }

        /// <summary>
        ///     Shows a dialog that runs `svn update` (revision 0 means HEAD) and lists the updated items.
        /// </summary>
        public async Task ShowUpdateProgressAsync(long revision)
        {
            if (_isUpdating)
                return;

            IsUpdating = true;
            await App.ShowDialogAsync(new SvnUpdateProgress(this, revision));
            IsUpdating = false;
        }

        /// <summary>
        ///     Runs the given `svn update` command and refreshes the page. The authentication popup is shown if
        ///     credentials are required.
        /// </summary>
        public async Task<bool> ExecUpdateAsync(Commands.SvnUpdate cmd, CommandLog log)
        {
            using var lockWatcher = LockWatcher();

            cmd.Log = log;
            var succ = await cmd.ExecAsync();
            log.Complete();

            MarkWorkingCopyDirtyManually();
            RefreshLog();

            if (!succ && cmd.IsAuthenticationFailed)
                RequestAuthentication();

            return succ;
        }

        public void Authenticate()
        {
            if (CanCreatePopup())
                ShowPopup(new SvnAuthenticate(this));
        }

        /// <summary>
        ///     Shows the authentication popup after the current popup (if any) is closed.
        /// </summary>
        public void RequestAuthentication()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (CanCreatePopup())
                    ShowPopup(new SvnAuthenticate(this));
                else
                    SendNotification(App.Text("Svn.Authenticate.Required"), true);
            });
        }

        public void OpenInFileManager()
        {
            Native.OS.OpenInFileManager(FullPath);
        }

        public void OpenInTerminal()
        {
            Native.OS.OpenTerminal(FullPath);
        }

        public CommandLog CreateLog(string name)
        {
            return new CommandLog(name);
        }

        private void OnDatabaseChanged()
        {
            RefreshInfo();
            RefreshWorkingCopyChanges();
        }

        private LauncherPage GetOwnerPage()
        {
            var launcher = App.GetLauncher();
            if (launcher == null)
                return null;

            foreach (var page in launcher.Pages)
            {
                if (page.Node.Id.Equals(FullPath))
                    return page;
            }

            return null;
        }

        private Models.SvnInfo _info = null;
        private int _selectedViewIndex = 1;
        private bool _isUpdating = false;
        private Models.SvnWatcher _watcher = null;
        private CancellationTokenSource _cancellationRefreshInfo = null;
        private CancellationTokenSource _cancellationRefreshWorkingCopy = null;
    }
}
