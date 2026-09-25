using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGit.Commands
{
    public class SvnQueryLog : SvnCommand
    {
        /// <summary>
        ///     Query at most `limit` revisions of the working copy, starting from `startRevision` (`HEAD` if it is less than 1) to 1.
        /// </summary>
        public SvnQueryLog(string wc, long startRevision, int limit)
        {
            WorkingDirectory = wc;
            Context = wc;
            RaiseError = false;

            var start = startRevision > 0 ? startRevision.ToString() : "HEAD";
            Args = $"log --xml -v -r {start}:1 --limit {limit} .";
        }

        /// <summary>
        ///     Error message of the last query (empty if succeeded).
        /// </summary>
        public string QueryError { get; private set; } = string.Empty;

        public async Task<List<Models.SvnRevision>> GetResultAsync()
        {
            var revisions = new List<Models.SvnRevision>();
            var rs = await ReadToEndAsync(Encoding.UTF8).ConfigureAwait(false);
            if (!rs.IsSuccess)
            {
                QueryError = string.IsNullOrEmpty(rs.StdErr) ? "Failed to query SVN log" : rs.StdErr.Trim();
                return revisions;
            }

            try
            {
                var doc = XDocument.Parse(rs.StdOut);
                foreach (var entry in doc.Descendants("logentry"))
                {
                    var revision = new Models.SvnRevision();
                    revision.Revision = long.TryParse(entry.Attribute("revision")?.Value, out var rev) ? rev : 0;
                    revision.Author = entry.Element("author")?.Value ?? string.Empty;
                    revision.Message = (entry.Element("msg")?.Value ?? string.Empty).Replace("\r\n", "\n");

                    var date = entry.Element("date")?.Value;
                    if (!string.IsNullOrEmpty(date) &&
                        DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                        revision.Date = utc.ToLocalTime();

                    var paths = entry.Element("paths");
                    if (paths != null)
                    {
                        foreach (var p in paths.Elements("path"))
                        {
                            var action = p.Attribute("action")?.Value;
                            var changed = new Models.SvnChangedPath()
                            {
                                Action = string.IsNullOrEmpty(action) ? 'M' : action[0],
                                Path = p.Value,
                                Kind = p.Attribute("kind")?.Value ?? string.Empty,
                                CopyFromPath = p.Attribute("copyfrom-path")?.Value ?? string.Empty,
                                CopyFromRevision = long.TryParse(p.Attribute("copyfrom-rev")?.Value, out var copyFromRev) ? copyFromRev : 0,
                            };

                            revision.Paths.Add(changed);
                        }
                    }

                    revisions.Add(revision);
                }
            }
            catch (Exception e)
            {
                QueryError = e.Message;
            }

            return revisions;
        }
    }
}
