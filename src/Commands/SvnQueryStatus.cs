using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SourceGit.Commands
{
    public class SvnQueryStatus : SvnCommand
    {
        public SvnQueryStatus(string wc)
        {
            WorkingDirectory = wc;
            Context = wc;
            RaiseError = false;
            Args = "status --xml --ignore-externals .";
        }

        public async Task<List<Models.SvnChange>> GetResultAsync()
        {
            var changes = new List<Models.SvnChange>();
            var rs = await ReadToEndAsync(Encoding.UTF8).ConfigureAwait(false);
            if (!rs.IsSuccess || string.IsNullOrEmpty(rs.StdOut))
                return changes;

            try
            {
                var doc = XDocument.Parse(rs.StdOut);
                foreach (var entry in doc.Descendants("entry"))
                {
                    var path = entry.Attribute("path")?.Value;
                    var status = entry.Element("wc-status");
                    if (string.IsNullOrEmpty(path) || path == "." || status == null)
                        continue;

                    var item = status.Attribute("item")?.Value ?? string.Empty;
                    var props = status.Attribute("props")?.Value ?? string.Empty;
                    var treeConflicted = status.Attribute("tree-conflicted")?.Value == "true";

                    var state = ParseState(item, props, treeConflicted);
                    if (state == Models.ChangeState.None)
                        continue;

                    var change = new Models.SvnChange()
                    {
                        Path = path.Replace('\\', '/'),
                        Item = item,
                        WorkTree = state,
                    };

                    changes.Add(change);
                }
            }
            catch
            {
                // Ignore errors
            }

            changes.Sort((l, r) => Models.NumericSort.Compare(l.Path, r.Path));
            return changes;
        }

        private static Models.ChangeState ParseState(string item, string props, bool treeConflicted)
        {
            if (treeConflicted || props.Equals("conflicted", StringComparison.Ordinal))
                return Models.ChangeState.Conflicted;

            switch (item)
            {
                case "modified":
                case "replaced":
                case "incomplete":
                    return Models.ChangeState.Modified;
                case "added":
                    return Models.ChangeState.Added;
                case "deleted":
                case "missing":
                    return Models.ChangeState.Deleted;
                case "unversioned":
                    return Models.ChangeState.Untracked;
                case "conflicted":
                    return Models.ChangeState.Conflicted;
                case "obstructed":
                    return Models.ChangeState.TypeChanged;
                default:
                    // Property-only changes.
                    return props.Equals("modified", StringComparison.Ordinal) ? Models.ChangeState.Modified : Models.ChangeState.None;
            }
        }
    }
}
