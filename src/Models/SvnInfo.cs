namespace SourceGit.Models
{
    /// <summary>
    ///     Information about a SVN working copy (output of `svn info --xml`).
    /// </summary>
    public class SvnInfo
    {
        public string WorkingCopyRoot { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        /// <summary>
        ///     URL relative to the repository root (`^/...`), URL-decoded for display.
        /// </summary>
        public string RelativeUrl { get; set; } = string.Empty;
        public string RepositoryRoot { get; set; } = string.Empty;
        public string RepositoryUUID { get; set; } = string.Empty;
        public long Revision { get; set; } = 0;
        public long LastChangedRevision { get; set; } = 0;
        public string LastChangedAuthor { get; set; } = string.Empty;
    }
}
