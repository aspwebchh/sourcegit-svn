using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    /// <summary>
    ///     A path changed in a SVN revision (one `path` element in `svn log --xml -v`).
    /// </summary>
    public class SvnChangedPath
    {
        public char Action { get; set; } = 'M';
        public string Path { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string CopyFromPath { get; set; } = string.Empty;
        public long CopyFromRevision { get; set; } = 0;

        public bool IsDirectory => Kind.Equals("dir", StringComparison.Ordinal);
    }

    /// <summary>
    ///     A SVN revision (one `logentry` element in `svn log --xml -v`).
    /// </summary>
    public class SvnRevision
    {
        public long Revision { get; set; } = 0;
        public string Author { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.MinValue;
        public string Message { get; set; } = string.Empty;
        public List<SvnChangedPath> Paths { get; set; } = [];

        public string RevisionName => $"r{Revision}";
        public string DateStr => Date == DateTime.MinValue ? string.Empty : DateTimeFormat.Format(Date);

        public string Subject
        {
            get
            {
                var msg = Message.TrimStart();
                var idx = msg.IndexOf('\n');
                return (idx > 0 ? msg.Substring(0, idx) : msg).Trim();
            }
        }

        /// <summary>
        ///     Converts the changed paths into `Change` list, so that the common change views can be reused.
        ///     Paths inside `baseDir` (the opened folder, relative to repository root) are shown relative to it, the
        ///     others are shown as absolute repository paths (with the leading '/').
        /// </summary>
        public List<Change> ToChanges(string baseDir)
        {
            var prefix = string.IsNullOrEmpty(baseDir) ? string.Empty : $"{baseDir.Trim('/')}/";
            var changes = new List<Change>();
            foreach (var p in Paths)
            {
                // Directories are only listed when none of their children are listed (for example, a deleted directory).
                if (p.IsDirectory && (p.Action == 'M' || HasChildren(p.Path)))
                    continue;

                var state = p.Action switch
                {
                    'A' => string.IsNullOrEmpty(p.CopyFromPath) ? ChangeState.Added : ChangeState.Copied,
                    'D' => ChangeState.Deleted,
                    'R' => ChangeState.Modified,
                    _ => ChangeState.Modified,
                };

                var repoPath = p.Path.TrimStart('/');
                var change = new SvnRevisionChange() { Path = ToDisplayPath(repoPath, prefix), RepositoryPath = repoPath, IsDirectory = p.IsDirectory };
                if (!string.IsNullOrEmpty(p.CopyFromPath))
                    change.OriginalPath = ToDisplayPath(p.CopyFromPath.TrimStart('/'), prefix);

                change.Index = state;
                change.WorkTree = state;
                changes.Add(change);
            }

            changes.Sort((l, r) => NumericSort.Compare(l.Path, r.Path));
            return changes;
        }

        private static string ToDisplayPath(string repoPath, string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
                return repoPath;

            if (repoPath.Length > prefix.Length && repoPath.StartsWith(prefix, StringComparison.Ordinal))
                return repoPath.Substring(prefix.Length);

            return $"/{repoPath}";
        }

        private bool HasChildren(string dir)
        {
            var prefix = dir.EndsWith('/') ? dir : $"{dir}/";
            foreach (var p in Paths)
            {
                if (p.Path.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
