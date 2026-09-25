using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

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

        public override async Task<bool> Sure()
        {
            var revision = long.TryParse(_revision?.Trim(), out var rev) ? rev : 0;
            ProgressDescription = revision > 0 ? $"Update working copy to r{revision} ..." : "Update working copy to HEAD ...";

            var log = _repo.CreateLog("Update");
            Use(log);

            return await _repo.ExecUpdateAsync(revision, log);
        }

        private readonly SvnRepository _repo;
        private string _revision = string.Empty;
    }
}
