using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGit.Commands
{
    public class SvnQueryInfo : SvnCommand
    {
        public SvnQueryInfo(string wc)
        {
            WorkingDirectory = wc;
            Context = wc;
            RaiseError = false;
            Args = "info --xml .";
        }

        /// <summary>
        ///     Returns true if the given folder is (or is inside) a SVN working copy and is NOT a git repository.
        /// </summary>
        public static bool IsWorkingCopy(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                return false;

            try
            {
                // Bare git repositories have no `.git` folder, but they must still be opened as git repositories.
                if (File.Exists(Path.Combine(dir, "HEAD")) &&
                    Directory.Exists(Path.Combine(dir, "objects")) &&
                    Directory.Exists(Path.Combine(dir, "refs")))
                    return false;

                return !string.IsNullOrEmpty(FindWorkingCopyRoot(dir));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        ///     Returns the folder to open for the given path: the path itself if it is a versioned folder of a SVN
        ///     working copy (so that a sub-folder can be opened on its own), otherwise the root of the working copy.
        ///     Returns null if the path is not inside a SVN working copy.
        /// </summary>
        public static async Task<string> FindWorkingCopyFolderAsync(string path)
        {
            var root = FindWorkingCopyRoot(path);
            if (string.IsNullOrEmpty(root))
                return null;

            string dir;
            try
            {
                dir = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
            }
            catch
            {
                return root;
            }

            if (dir.Equals(root, StringComparison.OrdinalIgnoreCase))
                return root;

            var info = await new SvnQueryInfo(dir).GetResultAsync().ConfigureAwait(false);
            return info != null ? dir : root;
        }

        /// <summary>
        ///     Finds the root of the SVN working copy that contains the given path. Returns null if not found.
        /// </summary>
        public static string FindWorkingCopyRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                var dir = new DirectoryInfo(path);
                while (dir != null)
                {
                    if (Path.Exists(Path.Combine(dir.FullName, ".git")))
                        return null;

                    if (Directory.Exists(Path.Combine(dir.FullName, ".svn")))
                        return dir.FullName.Replace('\\', '/').TrimEnd('/');

                    dir = dir.Parent;
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        public async Task<Models.SvnInfo> GetResultAsync()
        {
            var rs = await ReadToEndAsync(Encoding.UTF8).ConfigureAwait(false);
            if (!rs.IsSuccess || string.IsNullOrEmpty(rs.StdOut))
                return null;

            try
            {
                var doc = XDocument.Parse(rs.StdOut);
                var entry = doc.Root?.Element("entry");
                if (entry == null)
                    return null;

                var info = new Models.SvnInfo();
                info.Url = entry.Element("url")?.Value ?? string.Empty;
                info.RelativeUrl = Uri.UnescapeDataString(entry.Element("relative-url")?.Value ?? string.Empty);
                info.Revision = ParseRevision(entry.Attribute("revision")?.Value);

                var repo = entry.Element("repository");
                if (repo != null)
                {
                    info.RepositoryRoot = repo.Element("root")?.Value ?? string.Empty;
                    info.RepositoryUUID = repo.Element("uuid")?.Value ?? string.Empty;
                }

                var wcInfo = entry.Element("wc-info");
                if (wcInfo != null)
                    info.WorkingCopyRoot = wcInfo.Element("wcroot-abspath")?.Value ?? string.Empty;

                var commit = entry.Element("commit");
                if (commit != null)
                {
                    info.LastChangedRevision = ParseRevision(commit.Attribute("revision")?.Value);
                    info.LastChangedAuthor = commit.Element("author")?.Value ?? string.Empty;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private static long ParseRevision(string value)
        {
            return long.TryParse(value, out var rev) ? rev : 0;
        }
    }
}
