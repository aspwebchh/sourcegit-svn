namespace SourceGit.Commands
{
    /// <summary>
    ///     Access the repository with the given credentials, so that `svn` saves them into its auth cache.
    /// </summary>
    public class SvnAuthenticate : SvnCommand
    {
        public SvnAuthenticate(string wc, string url, string username, string password)
        {
            WorkingDirectory = wc;
            Context = wc;
            Username = username;
            Password = password;
            RaiseError = false;
            Args = $"info {EscapePath(url).Quoted()}";
        }
    }
}
