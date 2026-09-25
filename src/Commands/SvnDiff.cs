using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Runs `svn diff` and parses its unified-diff output with the same parser used by git diff.
    /// </summary>
    public class SvnDiff : SvnCommand
    {
        private const string SPECIAL_BINARY = "Cannot display: file marked as a binary type.";
        private const int BINARY_TEST_LENGTH = 8000;

        /// <summary>
        ///     Diff of a local change (working copy against BASE).
        /// </summary>
        public SvnDiff(string wc, Models.SvnChange change, int unifiedLines, bool ignoreWhitespace)
        {
            WorkingDirectory = wc;
            Context = wc;
            RaiseError = false;

            _fullPath = Path.Combine(wc, change.Path);
            _isUntracked = change.IsUnversioned;
            _isMissing = change.IsMissing;

            // `svn diff` shows nothing for missing files, so we read its BASE content instead.
            // NOTE: `svn diff` does not treat the last `@` in working copy paths as peg revision, so do not escape it.
            if (_isMissing)
                Args = $"cat -r BASE {EscapePath(change.Path).Quoted()}";
            else
                Args = $"diff --internal-diff {BuildExtensions(unifiedLines, ignoreWhitespace)} {change.Path.Quoted()}";
        }

        /// <summary>
        ///     Diff of a file changed in the given revision.
        /// </summary>
        public SvnDiff(string wc, string repositoryRoot, long revision, Models.SvnRevisionChange change, int unifiedLines, bool ignoreWhitespace)
        {
            WorkingDirectory = wc;
            Context = wc;
            RaiseError = false;

            var peg = change.WorkTree == Models.ChangeState.Deleted ? revision - 1 : revision;
            var url = BuildRepositoryUrl(repositoryRoot, change.RepositoryPath, peg);
            Args = $"diff --internal-diff {BuildExtensions(unifiedLines, ignoreWhitespace)} -c {revision} {url.Quoted()}";
        }

        public async Task<Models.DiffResult> ReadAsync()
        {
            if (_isUntracked)
                return ReadUntrackedFile();

            var (succ, data, _) = await ReadBytesAsync().ConfigureAwait(false);
            if (_isMissing)
                return succ ? BuildWholeFileDiff(data, false) : new Models.DiffResult();

            if (data.Length == 0)
                return new Models.DiffResult();

            if (IsMarkedAsBinary(data))
                return new Models.DiffResult() { IsBinary = true, NewHash = ComputeHash(data) };

            var rs = Diff.ParseUnified(data);
            rs.NewHash = ComputeHash(data);
            return rs;
        }

        private Models.DiffResult ReadUntrackedFile()
        {
            try
            {
                if (!File.Exists(_fullPath))
                    return new Models.DiffResult();

                return BuildWholeFileDiff(File.ReadAllBytes(_fullPath), true);
            }
            catch
            {
                return new Models.DiffResult();
            }
        }

        /// <summary>
        ///     Builds an unified diff that adds (or deletes) all lines of the given file content, so that the git diff
        ///     parser can be reused.
        /// </summary>
        private static Models.DiffResult BuildWholeFileDiff(byte[] data, bool isAdded)
        {
            var testLength = Math.Min(data.Length, BINARY_TEST_LENGTH);
            if (Array.IndexOf(data, (byte)0, 0, testLength) >= 0)
                return new Models.DiffResult() { IsBinary = true, NewHash = ComputeHash(data) };

            var lineCount = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == (byte)'\n')
                    lineCount++;
            }

            var endsWithNewLine = data.Length > 0 && data[^1] == (byte)'\n';
            if (!endsWithNewLine && data.Length > 0)
                lineCount++;

            using var ms = new MemoryStream(data.Length + lineCount + 64);
            var header = isAdded ? $"@@ -0,0 +1,{lineCount} @@\n" : $"@@ -1,{lineCount} +0,0 @@\n";
            ms.Write(Encoding.ASCII.GetBytes(header));

            var prefix = isAdded ? (byte)'+' : (byte)'-';
            var start = 0;
            while (start < data.Length)
            {
                var end = Array.IndexOf(data, (byte)'\n', start);
                ms.WriteByte(prefix);
                if (end < 0)
                {
                    ms.Write(data, start, data.Length - start);
                    ms.Write(Encoding.ASCII.GetBytes("\n\\ No newline at end of file\n"));
                    break;
                }

                ms.Write(data, start, end - start + 1);
                start = end + 1;
            }

            var rs = Diff.ParseUnified(ms.ToArray());
            rs.NewHash = ComputeHash(data);
            return rs;
        }

        private static string BuildExtensions(int unifiedLines, bool ignoreWhitespace)
        {
            var extensions = ignoreWhitespace ? $"-U{unifiedLines} -w --ignore-eol-style" : $"-U{unifiedLines}";
            return $"-x \"{extensions}\"";
        }

        private static bool IsMarkedAsBinary(byte[] data)
        {
            var testLength = Math.Min(data.Length, 4096);
            var head = Encoding.ASCII.GetString(data, 0, testLength);
            return head.Contains(SPECIAL_BINARY, StringComparison.Ordinal) && !head.Contains("\n@@ ", StringComparison.Ordinal);
        }

        private static string ComputeHash(byte[] data)
        {
            return Convert.ToHexString(SHA1.HashData(data));
        }

        private readonly string _fullPath = string.Empty;
        private readonly bool _isUntracked = false;
        private readonly bool _isMissing = false;
    }
}
