using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Asks username/password, and lets `svn` save them into its auth cache.
    /// </summary>
    public class SvnAuthenticate : Popup
    {
        public string Url
        {
            get => _repo.Info?.RepositoryRoot ?? string.Empty;
        }

        [Required(ErrorMessage = "Username is required!!!")]
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value, true);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public SvnAuthenticate(SvnRepository repo)
        {
            _repo = repo;
        }

        public override async Task<bool> Sure()
        {
            var url = Url;
            if (string.IsNullOrEmpty(url))
            {
                _repo.SendNotification("Can NOT find the repository url of this working copy!", true);
                return false;
            }

            ProgressDescription = $"Authenticate to '{url}' ...";

            var cmd = new Commands.SvnAuthenticate(_repo.FullPath, url, _username, _password);
            var succ = await cmd.ExecAsync();
            if (!succ)
            {
                _repo.SendNotification(cmd.Error, true);
                return false;
            }

            _repo.SendNotification(App.Text("Svn.Authenticate.Success"));
            _repo.RefreshAll();
            return true;
        }

        private readonly SvnRepository _repo;
        private string _username = string.Empty;
        private string _password = string.Empty;
    }
}
