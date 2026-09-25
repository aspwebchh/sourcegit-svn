using System;
using System.IO;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Saves the content of a file in repository (`svn cat URL@REV`) to local file.
    /// </summary>
    public class SvnSaveRevisionFile : SvnCommand
    {
        public SvnSaveRevisionFile(string wc, string url)
        {
            WorkingDirectory = wc;
            Context = wc;
            Args = $"cat {url.Quoted()}";
        }

        public async Task<bool> SaveAsync(string saveTo)
        {
            var (succ, data, err) = await ReadBytesAsync().ConfigureAwait(false);
            if (!succ)
            {
                Models.Notification.Send(Context, string.IsNullOrWhiteSpace(err) ? "Failed to read file from SVN repository" : err.Trim(), true);
                return false;
            }

            try
            {
                await File.WriteAllBytesAsync(saveTo, data).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                Models.Notification.Send(Context, $"Failed to save file: {e.Message}", true);
                return false;
            }
        }
    }
}
