using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Watches a SVN working copy. Changes in working copy files and in the working copy database (`.svn/wc.db`,
    ///     updated by `svn` itself or by other clients such as TortoiseSVN) trigger a refresh after a short delay.
    /// </summary>
    public class SvnWatcher : IDisposable
    {
        public class LockContext : IDisposable
        {
            public LockContext(SvnWatcher target)
            {
                _target = target;
                Interlocked.Increment(ref _target._lockCount);
            }

            public void Dispose()
            {
                Interlocked.Decrement(ref _target._lockCount);
            }

            private SvnWatcher _target;
        }

        /// <param name="fullpath">The opened folder (the working copy root, or one of its sub-folders)</param>
        /// <param name="wcRoot">The root of the working copy, which contains the `.svn` folder</param>
        public SvnWatcher(string fullpath, string wcRoot, Action onWorkingCopyChanged, Action onDatabaseChanged)
        {
            _onWorkingCopyChanged = onWorkingCopyChanged;
            _onDatabaseChanged = onDatabaseChanged;

            _watcher = new FileSystemWatcher();
            _watcher.Path = fullpath;
            _watcher.Filter = "*";
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.FileName;
            _watcher.IncludeSubdirectories = true;
            _watcher.Created += OnChanged;
            _watcher.Renamed += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.EnableRaisingEvents = false;

            // When a sub-folder is opened, the `.svn` folder is outside of it and must be watched separately.
            var normalizedFull = Path.GetFullPath(fullpath).TrimEnd('\\', '/');
            var normalizedRoot = Path.GetFullPath(wcRoot).TrimEnd('\\', '/');
            var dbDir = Path.Combine(normalizedRoot, ".svn");
            if (!normalizedFull.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(dbDir))
            {
                _dbWatcher = new FileSystemWatcher();
                _dbWatcher.Path = dbDir;
                _dbWatcher.Filter = "wc.db";
                _dbWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
                _dbWatcher.IncludeSubdirectories = false;
                _dbWatcher.Created += OnDatabaseFileChanged;
                _dbWatcher.Renamed += OnDatabaseFileChanged;
                _dbWatcher.Changed += OnDatabaseFileChanged;
                _dbWatcher.EnableRaisingEvents = false;
            }

            _timer = new Timer(Tick, null, 100, 100);

            // Starts filesystem watcher in another thread to avoid UI blocking
            Task.Run(() =>
            {
                try
                {
                    _watcher.EnableRaisingEvents = true;
                    if (_dbWatcher != null)
                        _dbWatcher.EnableRaisingEvents = true;
                }
                catch
                {
                    // Ignore exceptions. This may occur while `Dispose` is called.
                }
            });
        }

        public IDisposable Lock()
        {
            return new LockContext(this);
        }

        /// <summary>
        ///     Ignores changes of `.svn/wc.db` for a while. `svn status` may touch it.
        /// </summary>
        public void IgnoreDatabaseChanges(double seconds)
        {
            Interlocked.Exchange(ref _ignoreDatabaseUntil, DateTime.Now.AddSeconds(seconds).ToFileTime());
        }

        public void Dispose()
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();

            if (_dbWatcher != null)
            {
                _dbWatcher.EnableRaisingEvents = false;
                _dbWatcher.Dispose();
            }

            _timer.Dispose();
        }

        private void Tick(object sender)
        {
            if (Interlocked.Read(ref _lockCount) > 0)
                return;

            var now = DateTime.Now.ToFileTime();

            var oldUpdateDB = Interlocked.Exchange(ref _updateDB, -1);
            if (oldUpdateDB > 0)
            {
                if (now > oldUpdateDB)
                {
                    Interlocked.Exchange(ref _updateWC, -1);
                    _onDatabaseChanged?.Invoke();
                    return;
                }

                Interlocked.CompareExchange(ref _updateDB, oldUpdateDB, -1);
            }

            var oldUpdateWC = Interlocked.Exchange(ref _updateWC, -1);
            if (oldUpdateWC > 0)
            {
                if (now > oldUpdateWC)
                    _onWorkingCopyChanged?.Invoke();
                else
                    Interlocked.CompareExchange(ref _updateWC, oldUpdateWC, -1);
            }
        }

        private void OnChanged(object o, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
                return;

            var name = e.Name.Replace('\\', '/').TrimEnd('/');
            if (name.Equals(".svn", StringComparison.Ordinal))
                return;

            if (name.StartsWith(".svn/", StringComparison.Ordinal))
            {
                if (name.Equals(".svn/wc.db", StringComparison.Ordinal))
                    MarkDatabaseChanged();

                return;
            }

            if (name.Contains("/.svn/", StringComparison.Ordinal) || name.EndsWith("/.svn", StringComparison.Ordinal))
                return;

            // Changes inside nested git repositories never change the result of `svn status`.
            if (name.StartsWith(".git/", StringComparison.Ordinal) || name.Contains("/.git/", StringComparison.Ordinal))
                return;

            Interlocked.Exchange(ref _updateWC, DateTime.Now.AddSeconds(0.5).ToFileTime());
        }

        private void OnDatabaseFileChanged(object o, FileSystemEventArgs e)
        {
            MarkDatabaseChanged();
        }

        private void MarkDatabaseChanged()
        {
            if (DateTime.Now.ToFileTime() < Interlocked.Read(ref _ignoreDatabaseUntil))
                return;

            Interlocked.Exchange(ref _updateDB, DateTime.Now.AddSeconds(1).ToFileTime());
        }

        private readonly Action _onWorkingCopyChanged;
        private readonly Action _onDatabaseChanged;
        private readonly FileSystemWatcher _watcher;
        private readonly FileSystemWatcher _dbWatcher;
        private readonly Timer _timer;

        private long _lockCount;
        private long _updateWC;
        private long _updateDB;
        private long _ignoreDatabaseUntil;
    }
}
