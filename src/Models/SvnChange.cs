using System;

namespace SourceGit.Models
{
    /// <summary>
    ///     A local change in SVN working copy. It extends `Change` so that the common change views can be reused.
    /// </summary>
    public class SvnChange : Change
    {
        /// <summary>
        ///     Raw `item` status reported by `svn status --xml`, such as `modified`, `missing`, `unversioned`.
        /// </summary>
        public string Item { get; set; } = string.Empty;

        public bool IsMissing => Item.Equals("missing", StringComparison.Ordinal);
        public bool IsUnversioned => Item.Equals("unversioned", StringComparison.Ordinal);
    }

    /// <summary>
    ///     A file changed in a SVN revision. `Path` is used for display, `RepositoryPath` is used to query diff.
    /// </summary>
    public class SvnRevisionChange : Change
    {
        /// <summary>
        ///     Path relative to repository root (without the leading '/').
        /// </summary>
        public string RepositoryPath { get; set; } = string.Empty;

        /// <summary>
        ///     Directories are only listed when none of their children are listed (for example, a deleted directory).
        /// </summary>
        public bool IsDirectory { get; set; } = false;
    }

    /// <summary>
    ///     Placeholder shown in diff view for binary files in SVN working copy/revisions.
    /// </summary>
    public class SvnBinaryDiff;
}
