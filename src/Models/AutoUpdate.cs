using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    /// <summary>
    ///     Downloads new releases in the background on macOS and swaps the app bundle once SourceGit exits.
    /// </summary>
    [SupportedOSPlatform("macOS")]
    public static partial class AutoUpdate
    {
        [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
        private static extern int access(string path, int mode);

        public static bool IsSupported
        {
            get
            {
                var bundle = AppBundle;
                return bundle != null &&
                    access(bundle, 2 /* W_OK */) == 0 &&
                    access(Path.GetDirectoryName(bundle), 2 /* W_OK */) == 0;
            }
        }

        public static Version Staged
        {
            get;
            private set;
        }

        /// <summary>
        ///     Picks up an update downloaded by a previous session that was never installed, or cleans it up.
        /// </summary>
        public static void Restore()
        {
            try
            {
                var tagFile = Path.Combine(UpdateDir, "tag");
                if (File.Exists(tagFile) && Directory.Exists(StagedBundle))
                {
                    var ver = new Version() { TagName = File.ReadAllText(tagFile).Trim(), IsReadyToInstall = true };
                    if (ver.IsNewVersion)
                    {
                        Staged = ver;
                        return;
                    }
                }

                if (Directory.Exists(UpdateDir))
                    Directory.Delete(UpdateDir, true);
            }
            catch
            {
                // Ignore errors, the update will simply be downloaded again.
            }
        }

        /// <summary>
        ///     Downloads and verifies the given release so it can be installed when the app exits.
        /// </summary>
        public static async Task<bool> StageAsync(Version ver)
        {
            if (!IsSupported)
                return false;

            await _lock.WaitAsync();
            try
            {
                if (Staged?.TagName == ver.TagName)
                {
                    ver.IsReadyToInstall = true;
                    Staged = ver;
                    return true;
                }

                var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
                var assetName = $"sourcegit_{ver.TagName.Substring(1)}.{rid}.zip";
                var asset = ver.Assets.Find(x => x.Name == assetName);
                if (asset == null)
                    return false;

                // Without a digest to check the download against, only accept it if we can compare code signatures.
                var teamId = GetTeamIdentifier(AppBundle);
                var expectedHash = asset.Digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? asset.Digest.Substring(7) : null;
                if (teamId == null && expectedHash == null)
                    return false;

                if (Directory.Exists(UpdateDir))
                    Directory.Delete(UpdateDir, true);
                Directory.CreateDirectory(UpdateDir);

                var zip = Path.Combine(UpdateDir, assetName);
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("SourceGit");

                    await using var input = await client.GetStreamAsync(asset.DownloadUrl);
                    await using var output = File.Create(zip);
                    await input.CopyToAsync(output);
                }

                if (expectedHash != null)
                {
                    await using var stream = File.OpenRead(zip);
                    var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
                    if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                var extractDir = Path.Combine(UpdateDir, "staged");
                if (Run("/usr/bin/ditto", "-x", "-k", zip, extractDir) != 0 || !Directory.Exists(StagedBundle))
                    return false;

                File.Delete(zip);

                // A signed app only accepts updates signed by the same team.
                if (teamId != null)
                {
                    if (Run("/usr/bin/codesign", "--verify", "--deep", "--strict", StagedBundle) != 0 ||
                        GetTeamIdentifier(StagedBundle) != teamId)
                        return false;
                }

                await File.WriteAllTextAsync(Path.Combine(UpdateDir, "tag"), ver.TagName);
                ver.IsReadyToInstall = true;
                Staged = ver;
                return true;
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        ///     Replaces the running app bundle with the staged one after this process has exited.
        /// </summary>
        public static void InstallOnExit(bool relaunch)
        {
            if (Staged == null || !Directory.Exists(StagedBundle) || !IsSupported)
                return;

            var script = Path.Combine(UpdateDir, "install.sh");
            File.WriteAllText(script, INSTALL_SCRIPT);

            var args = new[]
            {
                script,
                Environment.ProcessId.ToString(),
                AppBundle,
                StagedBundle,
                Path.Combine(UpdateDir, "previous.app"),
                UpdateDir,
                relaunch ? "1" : "0",
            };

            var start = new ProcessStartInfo();
            if (Native.OS.SupportSetSid())
            {
                start.FileName = Native.OS.GetSetSidExecutable();
                start.ArgumentList.Add("/bin/sh");
            }
            else
            {
                start.FileName = "/bin/sh";
            }

            foreach (var arg in args)
                start.ArgumentList.Add(arg);

            start.UseShellExecute = false;
            Process.Start(start);
        }

        private static string AppBundle
        {
            get
            {
                // <bundle>.app/Contents/MacOS/SourceGit
                var macOSDir = Path.GetDirectoryName(Environment.ProcessPath);
                var bundle = Path.GetDirectoryName(Path.GetDirectoryName(macOSDir));
                return bundle != null && bundle.EndsWith(".app", StringComparison.Ordinal) ? bundle : null;
            }
        }

        private static string UpdateDir => Path.Combine(Native.OS.BasicDirectories.CacheDir, "update");

        private static string StagedBundle => Path.Combine(UpdateDir, "staged", "SourceGit.app");

        private static string GetTeamIdentifier(string bundle)
        {
            var start = new ProcessStartInfo("/usr/bin/codesign", ["-dv", "--verbose=2", bundle])
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var proc = Process.Start(start);
            var info = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            var match = REG_TEAM_ID().Match(info);
            return proc.ExitCode == 0 && match.Success ? match.Groups[1].Value : null;
        }

        private static int Run(string file, params string[] args)
        {
            var start = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using var proc = Process.Start(start);
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode;
        }

        [GeneratedRegex(@"^TeamIdentifier=([A-Z0-9]{10})$", RegexOptions.Multiline)]
        private static partial Regex REG_TEAM_ID();

        // Arguments: <pid> <app> <staged app> <backup> <update dir> <relaunch>
        private const string INSTALL_SCRIPT = """
            #!/bin/sh
            PID="$1"; APP="$2"; NEW="$3"; OLD="$4"; DIR="$5"; RELAUNCH="$6"

            while kill -0 "$PID" 2>/dev/null; do sleep 0.2; done

            rm -rf "$OLD"
            if mv "$APP" "$OLD"; then
              if mv "$NEW" "$APP"; then
                rm -rf "$DIR"
              else
                mv "$OLD" "$APP"
              fi
            fi

            if [ "$RELAUNCH" = "1" ]; then
              open "$APP"
            fi
            """;

        private static readonly SemaphoreSlim _lock = new(1, 1);
    }
}
