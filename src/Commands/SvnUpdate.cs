namespace SourceGit.Commands
{
    public class SvnUpdate : SvnCommand
    {
        public SvnUpdate(string wc, long revision)
        {
            WorkingDirectory = wc;
            Context = wc;

            var rev = revision > 0 ? $"-r {revision} " : string.Empty;
            Args = $"update --accept postpone {rev}.";
        }
    }
}
