using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SourceGit.Native
{
    /// <summary>
    ///     Stores small secrets (access tokens) in the OS credential store: the login keychain on macOS, the Windows
    ///     Credential Manager, and libsecret on Linux (falling back to a file only readable by the user).
    /// </summary>
    public static class SecretStore
    {
        public static string Get(string key)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                    return Run("/usr/bin/security", $"find-generic-password -s {Service} -a {key} -w", null);

                if (OperatingSystem.IsWindows())
                    return WindowsRead($"{Service}:{key}");

                var secret = Run("secret-tool", $"lookup service {Service} account {key}", null);
                if (secret != null)
                    return secret;

                var file = GetFallbackFile(key);
                return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            }
            catch (Exception e)
            {
                OS.LogException(e);
                return null;
            }
        }

        public static bool Set(string key, string secret)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // Interactive mode reads the command from stdin, so the secret never shows up in the process list.
                    return Run("/usr/bin/security", "-i", $"add-generic-password -U -s {Service} -a {key} -w \"{secret}\"\n") != null;
                }

                if (OperatingSystem.IsWindows())
                    return WindowsWrite($"{Service}:{key}", secret);

                if (Run("secret-tool", $"store --label=\"SourceGit ({key})\" service {Service} account {key}", secret) != null)
                    return true;

                var file = GetFallbackFile(key);
                File.WriteAllText(file, string.Empty);
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.WriteAllText(file, secret);
                return true;
            }
            catch (Exception e)
            {
                OS.LogException(e);
                return false;
            }
        }

        public static void Delete(string key)
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    Run("/usr/bin/security", $"delete-generic-password -s {Service} -a {key}", null);
                }
                else if (OperatingSystem.IsWindows())
                {
                    CredDeleteW($"{Service}:{key}", CRED_TYPE_GENERIC, 0);
                }
                else
                {
                    Run("secret-tool", $"clear service {Service} account {key}", null);

                    var file = GetFallbackFile(key);
                    if (File.Exists(file))
                        File.Delete(file);
                }
            }
            catch (Exception e)
            {
                OS.LogException(e);
            }
        }

        /// <summary>
        ///     Runs a tool and returns its trimmed stdout, or null when it can not be started or fails.
        /// </summary>
        private static string Run(string file, string args, string stdin)
        {
            var start = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process proc;
            try
            {
                proc = Process.Start(start);
            }
            catch
            {
                return null;
            }

            if (proc == null)
                return null;

            using (proc)
            {
                if (stdin != null)
                    proc.StandardInput.Write(stdin);
                proc.StandardInput.Close();

                var output = proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                return proc.ExitCode == 0 ? output.Trim() : null;
            }
        }

        private static string GetFallbackFile(string key)
        {
            return Path.Combine(OS.BasicDirectories.ConfigDir, $"{key}.secret");
        }

        private static string WindowsRead(string target)
        {
            if (!CredReadW(target, CRED_TYPE_GENERIC, 0, out var ptr))
                return null;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                    return null;

                return Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(ptr);
            }
        }

        private static bool WindowsWrite(string target, string secret)
        {
            var blob = Encoding.Unicode.GetBytes(secret);
            var blobPtr = Marshal.AllocHGlobal(blob.Length);
            var targetPtr = Marshal.StringToHGlobalUni(target);
            var userPtr = Marshal.StringToHGlobalUni(Environment.UserName);

            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);

                var cred = new CREDENTIAL()
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = targetPtr,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = userPtr,
                };

                return CredWriteW(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
                Marshal.FreeHGlobal(targetPtr);
                Marshal.FreeHGlobal(userPtr);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(ref CREDENTIAL credential, uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDeleteW(string target, uint type, uint flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        private const string Service = "SourceGit";
        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 2;
    }
}
