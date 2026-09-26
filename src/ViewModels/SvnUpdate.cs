using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    public class SvnUpdate : Popup
    {
        public string Url
        {
            get => Uri.UnescapeDataString(_repo.Info?.Url ?? string.Empty);
        }

        public string CurrentRevision
        {
            get => _repo.RevisionName;
        }

        /// <summary>
        ///     Target revision. Empty means HEAD.
        /// </summary>
        [RegularExpression(@"^\s*\d*\s*$", ErrorMessage = "Revision must be a number!!!")]
        public string Revision
        {
            get => _revision;
            set => SetProperty(ref _revision, value, true);
        }

        public SvnUpdate(SvnRepository repo)
        {
            _repo = repo;
        }

        public override Task<bool> Sure()
        {
            var revision = long.TryParse(_revision?.Trim(), out var rev) ? rev : 0;

            // Close this popup first, then run the update in the progress dialog.
            Dispatcher.UIThread.Post(async () => await _repo.ShowUpdateProgressAsync(revision));
            return Task.FromResult(true);
        }

        private readonly SvnRepository _repo;
        private string _revision = string.Empty;
    }
}
