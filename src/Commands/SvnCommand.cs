using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Base class of all commands that runs `svn`. It is independent of `Command` (which is only for git).
    /// </summary>
    public class SvnCommand
    {
        public string Context { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = null;
        public string Args { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RaiseError { get; set; } = true;
        public Models.ICommandLog Log { get; set; } = null;
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

        /// <summary>
        ///     Error message of the last execution (only available after `ExecAsync` returns false).
        /// </summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>
        ///     Whether the last execution failed because of missing/invalid credentials.
        /// </summary>
        public bool IsAuthenticationFailed => IsAuthError(Error);

        /// <summary>
        ///     The encoding used by `svn` for its non-XML outputs and `--targets` files. On Windows `svn` uses the
        ///     ANSI code page when it is started without console, on other platforms we force UTF-8 locale.
        /// </summary>
        public static Encoding NativeEncoding
        {
            get
            {
                if (_nativeEncoding == null)
                {
                    var encoding = Encoding.UTF8;
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                            encoding = Encoding.GetEncoding(0);
                        }
                        catch
                        {
                            encoding = Encoding.UTF8;
                        }
                    }

                    _nativeEncoding = encoding;
                }

                return _nativeEncoding;
            }
        }

        public static bool IsAuthError(string error)
        {
            if (string.IsNullOrEmpty(error))
                return false;

            return error.Contains("E170001", StringComparison.Ordinal) || // Authorization failed
                error.Contains("E215004", StringComparison.Ordinal); // No more credentials or we tried too many times
        }

        /// <summary>
        ///     Escapes a working copy path or URL to avoid `svn` treating the last `@` as peg revision.
        /// </summary>
        public static string EscapePath(string path)
        {
            return path.Contains('@') ? $"{path}@" : path;
        }

        /// <summary>
        ///     Builds the URL (with peg revision) of a path in repository. `repoPath` is relative to repository root.
        /// </summary>
        public static string BuildRepositoryUrl(string repositoryRoot, string repoPath, long peg)
        {
            return $"{repositoryRoot.TrimEnd('/')}/{repoPath.Replace("%", "%25", StringComparison.Ordinal)}@{peg}";
        }

        public async Task<bool> ExecAsync()
        {
            Log?.AppendLine($"$ svn {Args}\n");
            Error = string.Empty;

            var errs = new List<string>();
            var errsLock = new object();

            using var proc = new Process();
            proc.StartInfo = CreateStartInfo(NativeEncoding);
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Log?.AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                Log?.AppendLine(e.Data);
                lock (errsLock)
                    errs.Add(e.Data);
            };

            try
            {
                proc.Start();
                WritePasswordIfNeeded(proc);
            }
            catch (Exception e)
            {
                Error = e.Message;
                if (RaiseError)
                    Models.Notification.Send(Context, e.Message, true);

                Log?.AppendLine(string.Empty);
                return false;
            }

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcess(proc);
            }
            catch (Exception e)
            {
                lock (errsLock)
                    errs.Add(e.Message);
            }

            Log?.AppendLine(string.Empty);

            if (CancellationToken.IsCancellationRequested)
                return false;

            if (proc.ExitCode != 0)
            {
                lock (errsLock)
                    Error = string.Join("\n", errs).Trim();

                if (string.IsNullOrEmpty(Error))
                    Error = $"svn exited with code {proc.ExitCode}";

                if (RaiseError && !IsAuthenticationFailed)
                    Models.Notification.Send(Context, Error, true);

                return false;
            }

            return true;
        }

        protected async Task<Command.Result> ReadToEndAsync(Encoding outputEncoding)
        {
            using var proc = new Process();
            proc.StartInfo = CreateStartInfo(outputEncoding);

            try
            {
                proc.Start();
                WritePasswordIfNeeded(proc);
            }
            catch (Exception e)
            {
                return Command.Result.Failed(e.Message);
            }

            var rs = new Command.Result() { IsSuccess = true };

            try
            {
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(CancellationToken);
                var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken);
                rs.StdOut = await stdoutTask.ConfigureAwait(false);
                rs.StdErr = await stderrTask.ConfigureAwait(false);
                await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                KillProcess(proc);
                return Command.Result.Failed("Canceled");
            }

            rs.IsSuccess = proc.ExitCode == 0;
            return rs;
        }

        protected async Task<(bool, byte[], string)> ReadBytesAsync()
        {
            using var proc = new Process();
            proc.StartInfo = CreateStartInfo(NativeEncoding);

            try
            {
                proc.Start();
                WritePasswordIfNeeded(proc);
            }
            catch (Exception e)
            {
                return (false, [], e.Message);
            }

            try
            {
                using var ms = new MemoryStream();
                var stdoutTask = proc.StandardOutput.BaseStream.CopyToAsync(ms, CancellationToken);
                var stderrTask = proc.StandardError.ReadToEndAsync(CancellationToken);
                await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                await proc.WaitForExitAsync(CancellationToken).ConfigureAwait(false);
                return (proc.ExitCode == 0, ms.ToArray(), stderr);
            }
            catch (OperationCanceledException)
            {
                KillProcess(proc);
                return (false, [], "Canceled");
            }
        }

        /// <summary>
        ///     Writes given paths into a temporary file that can be used with `--targets` option.
        /// </summary>
        protected static string WriteTargetsFile(IEnumerable<string> paths)
        {
            var builder = new StringBuilder();
            foreach (var p in paths)
                builder.Append(EscapePath(p)).Append('\n');

            var tmp = Path.GetTempFileName();
            File.WriteAllBytes(tmp, NativeEncoding.GetBytes(builder.ToString()));
            return tmp;
        }

        protected static void DeleteTempFile(string file)
        {
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Ignore errors
            }
        }

        private ProcessStartInfo CreateStartInfo(Encoding outputEncoding)
        {
            var builder = new StringBuilder(512);
            builder.Append("--non-interactive ");

            if (!string.IsNullOrEmpty(Username))
            {
                builder.Append("--username ").Append(Username.Quoted()).Append(' ');
                if (!string.IsNullOrEmpty(Password))
                    builder.Append("--password-from-stdin ");
            }

            builder.Append(Args);

            var start = new ProcessStartInfo();
            start.FileName = Native.OS.SvnExecutable;
            start.Arguments = builder.ToString();
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.RedirectStandardInput = !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
            start.StandardOutputEncoding = outputEncoding;
            start.StandardErrorEncoding = NativeEncoding;
            if (start.RedirectStandardInput)
                start.StandardInputEncoding = NativeEncoding;

            // Always show messages in English, and make sure `svn` can handle non-ASCII paths on Linux/macOS.
            start.Environment["LC_MESSAGES"] = "C";
            if (!OperatingSystem.IsWindows() && !IsUTF8Locale())
                start.Environment["LC_CTYPE"] = OperatingSystem.IsMacOS() ? "en_US.UTF-8" : "C.UTF-8";

            if (!string.IsNullOrEmpty(WorkingDirectory))
                start.WorkingDirectory = WorkingDirectory;

            return start;
        }

        private void WritePasswordIfNeeded(Process proc)
        {
            if (!proc.StartInfo.RedirectStandardInput)
                return;

            proc.StandardInput.WriteLine(Password);
            proc.StandardInput.Close();
        }

        private static bool IsUTF8Locale()
        {
            foreach (var name in new[] { "LC_ALL", "LC_CTYPE", "LANG" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(value))
                    return value.Contains("UTF-8", StringComparison.OrdinalIgnoreCase) || value.Contains("utf8", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static void KillProcess(Process proc)
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(true);
            }
            catch
            {
                // Ignore errors
            }
        }

        private static Encoding _nativeEncoding = null;
    }
}
