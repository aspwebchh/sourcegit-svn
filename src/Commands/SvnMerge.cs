namespace SourceGit.Commands
{
    public class SvnMerge : SvnCommand
    {
        /// <summary>
        ///     Merges the changes of the working copy's own URL between `from` and `to`. It is a reverse merge (undo
        ///     changes) if `from` is greater than `to`.
        /// </summary>
        public SvnMerge(string wc, long from, long to)
        {
            WorkingDirectory = wc;
            Context = wc;
            Args = $"merge --accept postpone -r {from}:{to} .";
        }
    }
}
