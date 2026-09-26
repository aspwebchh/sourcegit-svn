using System;
using System.Text.RegularExpressions;

namespace SourceGit.Models
{
    public enum SvnUpdateAction
    {
        Added,
        Deleted,
        Updated,
        Merged,
        Conflicted,
        Existed,
        Replaced,
        Restored,
        Skipped,
        Message,
        Error,
    }

    /// <summary>
    ///     A line printed by `svn update`. For `Message` and `Error`, `Path` is the whole line.
    /// </summary>
    public partial class SvnUpdateEntry
    {
        public SvnUpdateAction Action { get; set; } = SvnUpdateAction.Message;
        public string Path { get; set; } = string.Empty;

        public bool IsFile => Action < SvnUpdateAction.Message;
        public bool IsError => Action == SvnUpdateAction.Error;

        [GeneratedRegex(@"^([ADUCGER ])([UCG ])([B ])([C ]) (.+)$")]
        private static partial Regex REG_FILE();

        [GeneratedRegex(@"^(Restored|Skipped)[^']*'(.+)'")]
        private static partial Regex REG_RESTORED_OR_SKIPPED();

        [GeneratedRegex(@"^(?:Updated to|At) revision (\d+)\.$")]
        private static partial Regex REG_FINAL_REVISION();

        /// <summary>
        ///     Parses one line of `svn update` output. Returns null for lines that should be ignored.
        /// </summary>
        public static SvnUpdateEntry Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("$ svn ", StringComparison.Ordinal))
                return null;

            var match = REG_FILE().Match(line);
            if (match.Success)
            {
                var content = match.Groups[1].Value[0];
                var prop = match.Groups[2].Value[0];
                var lockBroken = match.Groups[3].Value[0];
                var treeConflict = match.Groups[4].Value[0];

                if (content != ' ' || prop != ' ' || lockBroken != ' ' || treeConflict != ' ')
                {
                    SvnUpdateAction action;
                    if (content == 'C' || prop == 'C' || treeConflict == 'C')
                        action = SvnUpdateAction.Conflicted;
                    else
                        action = content switch
                        {
                            'A' => SvnUpdateAction.Added,
                            'D' => SvnUpdateAction.Deleted,
                            'G' => SvnUpdateAction.Merged,
                            'E' => SvnUpdateAction.Existed,
                            'R' => SvnUpdateAction.Replaced,
                            _ => prop == 'G' ? SvnUpdateAction.Merged : SvnUpdateAction.Updated,
                        };

                    return new SvnUpdateEntry() { Action = action, Path = match.Groups[5].Value };
                }
            }

            match = REG_RESTORED_OR_SKIPPED().Match(line);
            if (match.Success)
            {
                var action = match.Groups[1].Value.Equals("Restored", StringComparison.Ordinal) ? SvnUpdateAction.Restored : SvnUpdateAction.Skipped;
                return new SvnUpdateEntry() { Action = action, Path = match.Groups[2].Value };
            }

            if (line.StartsWith("svn: ", StringComparison.Ordinal))
                return new SvnUpdateEntry() { Action = SvnUpdateAction.Error, Path = line.Trim() };

            return new SvnUpdateEntry() { Action = SvnUpdateAction.Message, Path = line.Trim() };
        }

        /// <summary>
        ///     Gets the revision from the final line (`Updated to revision N.` or `At revision N.`), or -1.
        /// </summary>
        public static long ParseFinalRevision(string line)
        {
            var match = REG_FINAL_REVISION().Match(line);
            return match.Success && long.TryParse(match.Groups[1].Value, out var rev) ? rev : -1;
        }
    }
}
