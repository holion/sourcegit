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
    ///     The signed-in GitHub account. The login name lives in the preferences, the access token in the OS
    ///     credential store, where the `--github-credential` helper also reads it when git talks to github.com.
    /// </summary>
    public class GitHubAccount : ObservableObject
    {
        public static GitHubAccount Instance { get; } = new();

        public const string SecretKey = "github";

        public bool IsAvailable => Models.GitHub.IsConfigured;

        public string Login
        {
            get => Preferences.Instance.GitHubLogin;
        }

        public bool IsSignedIn
        {
            get => IsAvailable && !string.IsNullOrEmpty(Login);
        }

        public bool PreferSSH
        {
            get => Preferences.Instance.GitHubPreferSSH;
            set
            {
                if (Preferences.Instance.GitHubPreferSSH != value)
                {
                    Preferences.Instance.GitHubPreferSSH = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        ///     Whether git operations against https://github.com use this account instead of other credential helpers.
        /// </summary>
        public bool UseForGit
        {
            get => Preferences.Instance.GitHubUseForGit;
            set
            {
                if (Preferences.Instance.GitHubUseForGit != value)
                {
                    Preferences.Instance.GitHubUseForGit = value;
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
        ///     The last fetched repositories (sorted by most recently pushed), or an empty list.
        /// </summary>
        public List<Models.GitHubRepository> Repositories
        {
            get => _repositories;
            private set => SetProperty(ref _repositories, value);
        }

        /// <summary>
        ///     Stores the token returned by the device flow and makes the account the signed-in one.
        /// </summary>
        public bool CompleteSignIn(string login, string token)
        {
            if (!Native.SecretStore.Set(SecretKey, token))
                return false;

            _token = token;
            Preferences.Instance.GitHubLogin = login;
            Preferences.Instance.Save();
            NotifyAccountChanged();

            _ = RefreshAsync(true);
            return true;
        }

        public void SignOut()
        {
            Native.SecretStore.Delete(SecretKey);
            _token = null;
            _fetchedAt = DateTime.MinValue;
            Repositories = [];
            LoadError = null;

            Preferences.Instance.GitHubLogin = string.Empty;
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
        public string GetCloneUrl(Models.GitHubRepository repo)
        {
            return PreferSSH ? repo.SshUrl : repo.CloneUrl;
        }

        /// <summary>
        ///     Folder to clone the given repository into: `<default clone dir>/<owner>`, so the clone lands in a group
        ///     named after the organization. The clone dialog creates it when missing. Returns null without a default
        ///     clone dir.
        /// </summary>
        public static string PrepareParentFolder(Models.GitHubRepository repo)
        {
            var cloneDir = Preferences.Instance.GetDefaultCloneDir();
            if (string.IsNullOrEmpty(cloneDir) || !Directory.Exists(cloneDir))
                return null;

            return Path.Combine(cloneDir, repo.Owner.Login);
        }

        /// <summary>
        ///     Marks the repositories already cloned into one of the given local repositories.
        /// </summary>
        public static void MarkCloned(List<Models.GitHubRepository> repos, HashSet<string> clonedFullNames)
        {
            foreach (var repo in repos)
                repo.IsCloned = clonedFullNames.Contains(repo.FullName.ToLowerInvariant());
        }

        /// <summary>
        ///     Paths of every repository known to SourceGit plus the ones found under the default clone dir.
        /// </summary>
        public static List<string> CollectLocalRepositoryPaths()
        {
            var paths = new List<string>();
            CollectManaged(paths, Preferences.Instance.RepositoryNodes);

            var cloneDir = Preferences.Instance.GetDefaultCloneDir();
            if (!string.IsNullOrEmpty(cloneDir) && Directory.Exists(cloneDir))
            {
                foreach (var (path, _) in LauncherPagesCommandPalette.ScanForRepositories(cloneDir))
                    paths.Add(path);
            }

            return paths;
        }

        private async Task DoRefreshAsync()
        {
            IsLoading = true;
            LoadError = null;

            try
            {
                var token = await GetTokenAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(token))
                    throw new Models.GitHubException(App.Text("GitHub.SignIn.Missing"), true);

                var repos = await Models.GitHub.GetRepositoriesAsync(token, CancellationToken.None).ConfigureAwait(false);
                repos.Sort((l, r) => string.CompareOrdinal(r.PushedAt, l.PushedAt));

                Dispatcher.UIThread.Post(() =>
                {
                    _fetchedAt = DateTime.Now;
                    Repositories = repos;
                });
            }
            catch (Models.GitHubException e) when (e.IsUnauthorized)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SignOut();
                    Models.Notification.Send(null, App.Text("GitHub.SignIn.Expired"), true);
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

        private Task<string> GetTokenAsync()
        {
            if (!string.IsNullOrEmpty(_token))
                return Task.FromResult(_token);

            return Task.Run(() =>
            {
                _token = Native.SecretStore.Get(SecretKey);
                return _token;
            });
        }

        private void NotifyAccountChanged()
        {
            OnPropertyChanged(nameof(Login));
            OnPropertyChanged(nameof(IsSignedIn));
        }

        private static void CollectManaged(List<string> outs, List<RepositoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsRepository)
                    outs.Add(node.Id);
                else
                    CollectManaged(outs, node.SubNodes);
            }
        }

        private string _token = null;
        private bool _isLoading = false;
        private string _loadError = null;
        private DateTime _fetchedAt = DateTime.MinValue;
        private Task _pending = null;
        private List<Models.GitHubRepository> _repositories = [];
    }
}
