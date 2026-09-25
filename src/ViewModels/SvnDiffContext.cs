using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Diff of a SVN change. It is a simplified version of `DiffContext` (which only works with git).
    /// </summary>
    public class SvnDiffContext : ObservableObject
    {
        public string Title
        {
            get;
        }

        public bool IsTextDiff
        {
            get => _isTextDiff;
            private set => SetProperty(ref _isTextDiff, value);
        }

        public object Content
        {
            get => _content;
            private set => SetProperty(ref _content, value);
        }

        public int UnifiedLines
        {
            get => _unifiedLines;
            private set => SetProperty(ref _unifiedLines, value);
        }

        /// <summary>
        ///     Diff of a local change.
        /// </summary>
        public SvnDiffContext(string wc, Models.SvnChange change, SvnDiffContext previous = null)
            : this(change, previous, new Models.DiffOption("BASE", string.Empty, change),
                  (lines, ignoreWhitespace) => new Commands.SvnDiff(wc, change, lines, ignoreWhitespace))
        {
        }

        /// <summary>
        ///     Diff of a change in the given revision.
        /// </summary>
        public SvnDiffContext(string wc, string repositoryRoot, long revision, Models.SvnRevisionChange change, SvnDiffContext previous = null)
            : this(change, previous, new Models.DiffOption($"r{revision - 1}", $"r{revision}", change),
                  (lines, ignoreWhitespace) => new Commands.SvnDiff(wc, repositoryRoot, revision, change, lines, ignoreWhitespace))
        {
        }

        private SvnDiffContext(Models.Change change, SvnDiffContext previous, Models.DiffOption option, Func<int, bool, Commands.SvnDiff> createCommand)
        {
            _change = change;
            _option = option;
            _createCommand = createCommand;

            if (previous != null && previous._change.Path.Equals(change.Path, StringComparison.Ordinal))
            {
                _isTextDiff = previous._isTextDiff;
                _content = previous._content;
                _unifiedLines = previous._unifiedLines;
                _info = previous._info;
            }
            else if (previous != null)
            {
                _unifiedLines = previous._unifiedLines;
            }

            if (string.IsNullOrEmpty(change.OriginalPath))
                Title = change.Path;
            else
                Title = $"{change.OriginalPath} → {change.Path}";

            LoadContent();
        }

        public void IncrUnified()
        {
            UnifiedLines = _unifiedLines + 1;
            LoadContent();
        }

        public void DecrUnified()
        {
            UnifiedLines = Math.Max(4, _unifiedLines - 1);
            LoadContent();
        }

        public void CheckSettings()
        {
            var pref = Preferences.Instance;
            if (_info == null)
                return;

            if (Content is TextDiffContext ctx)
            {
                if ((pref.UseFullTextDiff && _info.UnifiedLines != ENTIRE_FILE_LINES) ||
                    (!pref.UseFullTextDiff && _info.UnifiedLines == ENTIRE_FILE_LINES) ||
                    (pref.IgnoreWhitespaceChangesInDiff != _info.IgnoreWhitespace))
                {
                    LoadContent();
                    return;
                }

                if (ctx.IsSideBySide() != pref.UseSideBySideDiff)
                    Content = ctx.SwitchMode();
            }
            else if (Content is Models.NoOrEOLChange)
            {
                if (pref.IgnoreWhitespaceChangesInDiff != _info.IgnoreWhitespace)
                    LoadContent();
            }
        }

        private void LoadContent()
        {
            Task.Run(async () =>
            {
                var pref = Preferences.Instance;
                var numLines = pref.UseFullTextDiff ? ENTIRE_FILE_LINES : _unifiedLines;
                var ignoreWhitespace = pref.IgnoreWhitespaceChangesInDiff;

                var latest = await _createCommand(numLines, ignoreWhitespace).ReadAsync().ConfigureAwait(false);
                var info = new Info(numLines, ignoreWhitespace, latest.NewHash);
                if (_info != null && info.IsSame(_info))
                    return;

                _info = info;

                object rs;
                if (latest.TextDiff is { } textDiff)
                    rs = textDiff;
                else if (latest.IsBinary)
                    rs = new Models.SvnBinaryDiff();
                else
                    rs = new Models.NoOrEOLChange();

                Dispatcher.UIThread.Post(() =>
                {
                    if (rs is Models.TextDiff cur)
                    {
                        IsTextDiff = true;

                        if (Preferences.Instance.UseSideBySideDiff)
                            Content = new TwoSideTextDiff(_option, cur, _content as TextDiffContext);
                        else
                            Content = new CombinedTextDiff(_option, cur, _content as TextDiffContext);
                    }
                    else
                    {
                        IsTextDiff = false;
                        Content = rs;
                    }
                });
            });
        }

        private class Info
        {
            public int UnifiedLines { get; }
            public bool IgnoreWhitespace { get; }
            public string Hash { get; }

            public Info(int unifiedLines, bool ignoreWhitespace, string hash)
            {
                UnifiedLines = unifiedLines;
                IgnoreWhitespace = ignoreWhitespace;
                Hash = hash ?? string.Empty;
            }

            public bool IsSame(Info other)
            {
                return UnifiedLines == other.UnifiedLines &&
                    IgnoreWhitespace == other.IgnoreWhitespace &&
                    Hash.Equals(other.Hash, StringComparison.Ordinal);
            }
        }

        private const int ENTIRE_FILE_LINES = 999999999;

        private readonly Models.Change _change;
        private readonly Models.DiffOption _option = null;
        private readonly Func<int, bool, Commands.SvnDiff> _createCommand = null;
        private int _unifiedLines = 4;
        private bool _isTextDiff = false;
        private object _content = null;
        private Info _info = null;
    }
}
