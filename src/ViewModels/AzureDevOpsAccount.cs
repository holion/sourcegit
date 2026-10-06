using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     The signed-in Azure DevOps account. The login name lives in the preferences, the refresh token in the OS
    ///     credential store, where the `--azure-devops-credential` helper also reads it when git talks to Azure DevOps.
    ///     Access tokens only live for about an hour, so they are kept in memory and fetched again when needed.
    /// </summary>
    public class AzureDevOpsAccount : ObservableObject
    {
        public static AzureDevOpsAccount Instance { get; } = new();

        public const string SecretKey = "azure-devops";

        public bool IsAvailable => Models.AzureDevOps.IsConfigured;

        public string Login
        {
            get => Preferences.Instance.AzureDevOpsLogin;
        }

        public bool IsSignedIn
        {
            get => IsAvailable && !string.IsNullOrEmpty(Login);
        }

        public bool PreferSSH
        {
            get => Preferences.Instance.AzureDevOpsPreferSSH;
            set
            {
                if (Preferences.Instance.AzureDevOpsPreferSSH != value)
                {
                    Preferences.Instance.AzureDevOpsPreferSSH = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        ///     Whether git operations against Azure DevOps over HTTPS use this account instead of other credential helpers.
        /// </summary>
        public bool UseForGit
        {
            get => Preferences.Instance.AzureDevOpsUseForGit;
            set
            {
                if (Preferences.Instance.AzureDevOpsUseForGit != value)
                {
                    Preferences.Instance.AzureDevOpsUseForGit = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public string LoadError
        {
            get => _loadError;
            private set => SetProperty(ref _loadError, value);
        }

        /// <summary>
        ///     The last fetched repositories (sorted by organization, project and name), or an empty list.
        /// </summary>
        public List<Models.AzureDevOpsRepository> Repositories
        {
            get => _repositories;
            private set => SetProperty(ref _repositories, value);
        }

        /// <summary>
        ///     Stores the refresh token returned by the device flow and makes the account the signed-in one.
        /// </summary>
        public bool CompleteSignIn(string login, Models.AzureDevOpsTokens tokens)
        {
            if (!Native.SecretStore.Set(SecretKey, tokens.RefreshToken))
                return false;

            _accessToken = tokens.AccessToken;
            _accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            Preferences.Instance.AzureDevOpsLogin = login;
            Preferences.Instance.Save();
            NotifyAccountChanged();

            _ = RefreshAsync(true);
            return true;
        }

        public void SignOut()
        {
            Native.SecretStore.Delete(SecretKey);
            _accessToken = null;
            _accessTokenExpiresAt = DateTime.MinValue;
            _fetchedAt = DateTime.MinValue;
            Repositories = [];
            LoadError = null;

            Preferences.Instance.AzureDevOpsLogin = string.Empty;
            Preferences.Instance.Save();
            NotifyAccountChanged();
        }

        /// <summary>
        ///     Fetches the repository list unless a recent one is cached. Concurrent callers share the same request.
        /// </summary>
        public Task RefreshAsync(bool force)
        {
            if (!IsSignedIn)
                return Task.CompletedTask;

            if (_pending != null)
                return _pending;

            if (!force && DateTime.Now - _fetchedAt < TimeSpan.FromMinutes(5))
                return Task.CompletedTask;

            _pending = DoRefreshAsync();
            return _pending;
        }

        /// <summary>
        ///     Remote URL to clone the given repository with, following the protocol preference.
        /// </summary>
        public string GetCloneUrl(Models.AzureDevOpsRepository repo)
        {
            return PreferSSH && !string.IsNullOrEmpty(repo.SshUrl) ? repo.SshUrl : repo.RemoteUrl;
        }

        /// <summary>
        ///     Folder to clone the given repository into: `<default clone dir>/<project>`, so the clone lands in a group
        ///     named after its project. The clone dialog creates it when missing. Returns null without a default clone dir.
        /// </summary>
        public static string PrepareParentFolder(Models.AzureDevOpsRepository repo)
        {
            var cloneDir = Preferences.Instance.GetDefaultCloneDir();
            if (string.IsNullOrEmpty(cloneDir) || !Directory.Exists(cloneDir))
                return null;

            return Path.Combine(cloneDir, repo.Project.Name);
        }

        /// <summary>
        ///     Marks the repositories already cloned into one of the given local repositories.
        /// </summary>
        public static void MarkCloned(List<Models.AzureDevOpsRepository> repos, HashSet<string> clonedFullNames)
        {
            foreach (var repo in repos)
                repo.IsCloned = clonedFullNames.Contains(repo.FullName.ToLowerInvariant());
        }

        /// <summary>
        ///     Returns an access token for git, refreshing it with the stored refresh token. Runs in the short-lived
        ///     `--azure-devops-credential` process, so it only touches the credential store, never the preferences.
        ///     Returns null when not signed in or the refresh token is no longer valid.
        /// </summary>
        public static async Task<string> GetAccessTokenForGitAsync(CancellationToken token)
        {
            var refreshToken = Native.SecretStore.Get(SecretKey);
            if (string.IsNullOrEmpty(refreshToken))
                return null;

            try
            {
                var tokens = await Models.AzureDevOps.RefreshTokensAsync(refreshToken, token).ConfigureAwait(false);
                StoreRotatedRefreshToken(refreshToken, tokens);
                return tokens.AccessToken;
            }
            catch
            {
                return null;
            }
        }

        private async Task DoRefreshAsync()
        {
            IsLoading = true;
            LoadError = null;

            try
            {
                var token = await GetAccessTokenAsync().ConfigureAwait(false);
                var profile = await Models.AzureDevOps.GetProfileAsync(token, CancellationToken.None).ConfigureAwait(false);
                var organizations = await Models.AzureDevOps.GetOrganizationsAsync(token, profile.Id, CancellationToken.None).ConfigureAwait(false);

                // One organization failing (e.g. one connected to another Entra tenant) should not hide the others.
                var errors = new List<string>();
                var tasks = new List<Task<List<Models.AzureDevOpsRepository>>>();
                foreach (var organization in organizations)
                    tasks.Add(Models.AzureDevOps.GetRepositoriesAsync(token, organization, CancellationToken.None));

                var repos = new List<Models.AzureDevOpsRepository>();
                for (var i = 0; i < tasks.Count; i++)
                {
                    try
                    {
                        repos.AddRange(await tasks[i].ConfigureAwait(false));
                    }
                    catch (Exception e)
                    {
                        errors.Add($"{organizations[i]}: {e.Message}");
                    }
                }

                repos.RemoveAll(x => x.IsDisabled);
                repos.Sort((l, r) => string.Compare(l.FullName, r.FullName, StringComparison.OrdinalIgnoreCase));

                Dispatcher.UIThread.Post(() =>
                {
                    _fetchedAt = DateTime.Now;
                    Repositories = repos;
                    if (repos.Count == 0)
                        LoadError = errors.Count > 0 ? string.Join('\n', errors) : App.Text("AzureDevOps.Picker.NoOrganizations");
                });
            }
            catch (Models.AzureDevOpsException e) when (e.IsUnauthorized)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SignOut();
                    Models.Notification.Send(null, App.Text("AzureDevOps.SignIn.Expired"), true);
                });
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() => LoadError = e.Message);
            }
            finally
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsLoading = false;
                    _pending = null;
                });
            }
        }

        /// <summary>
        ///     The cached access token, or a new one from the stored refresh token once it is about to expire.
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _accessTokenExpiresAt.AddMinutes(-5))
                return _accessToken;

            var refreshToken = await Task.Run(() => Native.SecretStore.Get(SecretKey)).ConfigureAwait(false);
            if (string.IsNullOrEmpty(refreshToken))
                throw new Models.AzureDevOpsException(App.Text("AzureDevOps.SignIn.Missing"), true);

            var tokens = await Models.AzureDevOps.RefreshTokensAsync(refreshToken, CancellationToken.None).ConfigureAwait(false);
            StoreRotatedRefreshToken(refreshToken, tokens);

            _accessToken = tokens.AccessToken;
            _accessTokenExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
            return _accessToken;
        }

        /// <summary>
        ///     Every refresh hands out a new refresh token with a fresh lifetime. Keeping the newest one means the
        ///     sign-in only expires after a long time without using it.
        /// </summary>
        private static void StoreRotatedRefreshToken(string old, Models.AzureDevOpsTokens tokens)
        {
            if (!string.IsNullOrEmpty(tokens.RefreshToken) && tokens.RefreshToken != old)
                Native.SecretStore.Set(SecretKey, tokens.RefreshToken);
        }

        private void NotifyAccountChanged()
        {
            OnPropertyChanged(nameof(Login));
            OnPropertyChanged(nameof(IsSignedIn));
        }

        private string _accessToken = null;
        private DateTime _accessTokenExpiresAt = DateTime.MinValue;
        private bool _isLoading = false;
        private string _loadError = null;
        private DateTime _fetchedAt = DateTime.MinValue;
        private Task _pending = null;
        private List<Models.AzureDevOpsRepository> _repositories = [];
    }
}
