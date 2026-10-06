using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Threading;

namespace SourceGit.ViewModels
{
    public class LauncherPagesCommandPalette : ICommandPalette, IDisposable
    {
        public List<LauncherPage> VisiblePages
        {
            get => _visiblePages;
            private set => SetProperty(ref _visiblePages, value);
        }

        public List<RepositoryNode> VisibleRepos
        {
            get => _visibleRepos;
            private set => SetProperty(ref _visibleRepos, value);
        }

        public List<Models.GitHubRepository> VisibleGitHubRepos
        {
            get => _visibleGitHubRepos;
            private set => SetProperty(ref _visibleGitHubRepos, value);
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set
            {
                if (SetProperty(ref _searchFilter, value))
                    UpdateVisible();
            }
        }

        public LauncherPage SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (SetProperty(ref _selectedPage, value) && value != null)
                {
                    SelectedRepo = null;
                    SelectedGitHubRepo = null;
                }
            }
        }

        public RepositoryNode SelectedRepo
        {
            get => _selectedRepo;
            set
            {
                if (SetProperty(ref _selectedRepo, value) && value != null)
                {
                    SelectedPage = null;
                    SelectedGitHubRepo = null;
                }
            }
        }

        public Models.GitHubRepository SelectedGitHubRepo
        {
            get => _selectedGitHubRepo;
            set
            {
                if (SetProperty(ref _selectedGitHubRepo, value) && value != null)
                {
                    SelectedPage = null;
                    SelectedRepo = null;
                }
            }
        }

        public LauncherPagesCommandPalette(Launcher launcher)
        {
            _launcher = launcher;

            foreach (var page in _launcher.Pages)
            {
                if (page.Node.IsRepository)
                    _opened.Add(page.Node.Id);
            }

            CollectManagedRepository(Preferences.Instance.RepositoryNodes);

            var cloneDir = Preferences.Instance.GetDefaultCloneDir();
            if (!string.IsNullOrEmpty(cloneDir) && Directory.Exists(cloneDir))
            {
                if (cloneDir.Equals(s_scannedDir, StringComparison.Ordinal))
                    SetDiscovered(s_scanned);

                ScanDefaultCloneDir(cloneDir);
            }

            var github = GitHubAccount.Instance;
            if (github.IsSignedIn)
            {
                github.PropertyChanged += OnGitHubAccountPropertyChanged;
                _ = github.RefreshAsync(false);
                UpdateClonedGitHubRepos();
            }

            UpdateVisible();
        }

        public void Dispose()
        {
            GitHubAccount.Instance.PropertyChanged -= OnGitHubAccountPropertyChanged;
        }

        /// <summary>
        ///     Same scan as the palette runs over the default clone dir: every repository found below `dir` with the
        ///     repository owning its tab.
        /// </summary>
        public static List<(string Path, string Root)> ScanForRepositories(string dir)
        {
            var found = new List<(string Path, string Root)>();
            ScanDirectory(new DirectoryInfo(dir), found, 0);
            return found;
        }

        public void ClearFilter()
        {
            SearchFilter = string.Empty;
        }

        public void OpenOrSwitchTo()
        {
            _opened.Clear();
            _visiblePages.Clear();
            _visibleRepos.Clear();
            Close();

            if (_selectedPage != null)
                _launcher.ActivePage = _selectedPage;
            else if (_selectedGitHubRepo != null)
                CloneFromGitHub(_selectedGitHubRepo);
            else if (_selectedRepo != null && _discoveredRoots.TryGetValue(_selectedRepo, out var root))
                OpenDiscovered(_selectedRepo.Id, root);
            else if (_selectedRepo != null)
                _launcher.OpenRepositoryInTab(_selectedRepo, null);

            _discovered.Clear();
            _discoveredRoots.Clear();
        }

        private void UpdateVisible()
        {
            var pages = new List<LauncherPage>();
            CollectVisiblePages(pages);

            var repos = new List<RepositoryNode>();
            CollectVisibleRepository(repos, Preferences.Instance.RepositoryNodes);
            CollectVisibleRepository(repos, _discovered);

            var githubRepos = new List<Models.GitHubRepository>();
            CollectVisibleGitHubRepository(githubRepos);

            var autoSelectPage = _selectedPage;
            var autoSelectRepo = _selectedRepo;
            var autoSelectGitHubRepo = _selectedGitHubRepo != null && githubRepos.Contains(_selectedGitHubRepo) ? _selectedGitHubRepo : null;

            if (_selectedPage != null)
            {
                if (pages.Contains(_selectedPage))
                {
                    // Keep selection
                }
                else if (pages.Count > 0)
                {
                    autoSelectPage = pages[0];
                }
                else if (repos.Count > 0)
                {
                    autoSelectPage = null;
                    autoSelectRepo = repos[0];
                }
                else
                {
                    autoSelectPage = null;
                }
            }
            else if (_selectedRepo != null)
            {
                if (repos.Contains(_selectedRepo))
                {
                    // Keep selection
                }
                else if (repos.Count > 0)
                {
                    autoSelectRepo = repos[0];
                }
                else if (pages.Count > 0)
                {
                    autoSelectPage = pages[0];
                    autoSelectRepo = null;
                }
                else
                {
                    autoSelectRepo = null;
                }
            }
            else if (pages.Count > 0)
            {
                autoSelectPage = pages[0];
                autoSelectRepo = null;
            }
            else if (repos.Count > 0)
            {
                autoSelectPage = null;
                autoSelectRepo = repos[0];
            }
            else
            {
                autoSelectPage = null;
                autoSelectRepo = null;
            }

            // GitHub repositories come last: only pick one when nothing local matches.
            if (autoSelectPage != null || autoSelectRepo != null)
                autoSelectGitHubRepo = null;
            else if (autoSelectGitHubRepo == null && githubRepos.Count > 0)
                autoSelectGitHubRepo = githubRepos[0];

            VisiblePages = pages;
            VisibleRepos = repos;
            VisibleGitHubRepos = githubRepos;
            SelectedPage = autoSelectPage;
            SelectedRepo = autoSelectRepo;
            SelectedGitHubRepo = autoSelectGitHubRepo;
        }

        private void CollectVisiblePages(List<LauncherPage> pages)
        {
            foreach (var page in _launcher.Pages)
            {
                if (page == _launcher.ActivePage)
                    continue;

                if (string.IsNullOrEmpty(_searchFilter) ||
                    page.Node.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    (page.Node.IsRepository && page.Node.Id.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)))
                    pages.Add(page);
            }
        }

        private void CollectVisibleRepository(List<RepositoryNode> outs, List<RepositoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsRepository)
                {
                    CollectVisibleRepository(outs, node.SubNodes);
                    continue;
                }

                if (_opened.Contains(node.Id))
                    continue;

                if (string.IsNullOrEmpty(_searchFilter) ||
                    node.Id.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    node.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    outs.Add(node);
            }
        }

        /// <summary>
        ///     GitHub repositories that are not cloned yet. Only listed while searching, so they do not bury the tabs
        ///     and local repositories.
        /// </summary>
        private void CollectVisibleGitHubRepository(List<Models.GitHubRepository> outs)
        {
            if (string.IsNullOrEmpty(_searchFilter) || !GitHubAccount.Instance.IsSignedIn)
                return;

            foreach (var repo in GitHubAccount.Instance.Repositories)
            {
                if (repo.IsArchived || s_clonedGitHubRepos.Contains(repo.FullName.ToLowerInvariant()))
                    continue;

                if (repo.FullName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    outs.Add(repo);
            }
        }

        private void OnGitHubAccountPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GitHubAccount.Repositories) && _launcher.CommandPalette == this)
                UpdateVisible();
        }

        private void UpdateClonedGitHubRepos()
        {
            var paths = new List<string>(_managed);
            foreach (var (path, _) in s_scanned)
                paths.Add(path);

            Task.Run(() =>
            {
                var cloned = Models.GitHub.CollectClonedFullNames(paths);
                Dispatcher.UIThread.Post(() =>
                {
                    s_clonedGitHubRepos = cloned;
                    if (_launcher.CommandPalette == this)
                        UpdateVisible();
                });
            });
        }

        private void CloneFromGitHub(Models.GitHubRepository repo)
        {
            if (!Preferences.Instance.IsGitConfigured())
            {
                Models.Notification.Send(null, App.Text("NotConfigured"), true);
                return;
            }

            // Cloning replaces the page it runs on, so never start it on top of an open repository.
            if (_launcher.ActivePage is not { Node.IsRepository: false } page || !page.CanCreatePopup())
            {
                _launcher.AddNewTab();
                page = _launcher.ActivePage;
            }

            // Only fill in the clone dialog, so the local location can still be changed before cloning.
            var clone = new Clone(page.Node.Id);
            clone.UseGitHubRepository(repo);
            page.Popup = clone;
        }

        private void CollectManagedRepository(List<RepositoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsRepository)
                    _managed.Add(node.Id);
                else
                    CollectManagedRepository(node.SubNodes);
            }
        }

        private void ScanDefaultCloneDir(string dir)
        {
            if (s_scanning)
                return;

            s_scanning = true;
            Task.Run(() =>
            {
                var found = ScanForRepositories(dir);

                Dispatcher.UIThread.Post(() =>
                {
                    s_scanning = false;
                    s_scannedDir = dir;
                    s_scanned = found;

                    if (_launcher.CommandPalette == this)
                    {
                        SetDiscovered(found);
                        UpdateVisible();

                        if (GitHubAccount.Instance.IsSignedIn)
                            UpdateClonedGitHubRepos();
                    }
                });
            });
        }

        /// <summary>
        ///     Mirrors the rules of `ScanRepositories`, but only checks for a `.git` folder instead of asking git so
        ///     it is cheap enough to run every time the palette opens. Repositories nested directly inside another
        ///     one are reported with the outer repository as their root, since that is the one owning their tab.
        /// </summary>
        private static void ScanDirectory(DirectoryInfo dir, List<(string Path, string Root)> outs, int depth)
        {
            try
            {
                var subdirs = dir.EnumerateDirectories("*", new EnumerationOptions()
                {
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                    IgnoreInaccessible = true,
                });

                foreach (var subdir in subdirs)
                {
                    if (subdir.Name.StartsWith('.') || subdir.Name.Equals("node_modules", StringComparison.Ordinal))
                        continue;

                    var normalized = subdir.FullName.Replace('\\', '/').TrimEnd('/');
                    var gitDir = Path.Combine(subdir.FullName, ".git");
                    if (Directory.Exists(gitDir))
                    {
                        outs.Add((normalized, normalized));
                        foreach (var nested in NestedRepositories.Scan(normalized))
                            outs.Add((nested, normalized));
                    }
                    else if (File.Exists(gitDir) || IsBareRepository(subdir.FullName))
                    {
                        outs.Add((normalized, normalized));
                    }
                    else if (depth < 5)
                    {
                        ScanDirectory(subdir, outs, depth + 1);
                    }
                }
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }
        }

        private static bool IsBareRepository(string dir)
        {
            return File.Exists(Path.Combine(dir, "HEAD")) &&
                Directory.Exists(Path.Combine(dir, "objects")) &&
                Directory.Exists(Path.Combine(dir, "refs"));
        }

        private void SetDiscovered(List<(string Path, string Root)> found)
        {
            var existing = new Dictionary<string, RepositoryNode>();
            foreach (var node in _discovered)
                existing[node.Id] = node;

            _discovered = [];
            _discoveredRoots.Clear();

            foreach (var (path, root) in found)
            {
                if (_managed.Contains(path))
                    continue;

                // Keep the same node instances across rescans, so the current selection survives.
                if (!existing.TryGetValue(path, out var node))
                {
                    var name = Path.GetFileName(path);
                    if (path != root)
                        name = $"{Path.GetFileName(root)}/{name}";

                    node = new RepositoryNode()
                    {
                        Id = path,
                        Name = name,
                        Bookmark = 0,
                        IsRepository = true,
                        IsUnmanaged = true,
                    };
                }

                _discovered.Add(node);
                _discoveredRoots[node] = root;
            }
        }

        private void OpenDiscovered(string path, string root)
        {
            var node = Preferences.Instance.FindNode(root);
            if (node == null)
            {
                var group = Preferences.Instance.FindOrCreateGroupByDefaultCloneDir(root);
                node = Preferences.Instance.FindOrAddNodeByRepositoryPath(root, group, false);
                Welcome.Instance.Refresh();
                _ = node.UpdateStatusAsync(false, null);
            }

            _launcher.OpenRepositoryInTab(node, null);

            if (path != root)
                _launcher.ActivePage?.Nested?.TrySelect(path);
        }

        private static bool s_scanning = false;
        private static string s_scannedDir = null;
        private static List<(string Path, string Root)> s_scanned = [];
        private static HashSet<string> s_clonedGitHubRepos = [];

        private Launcher _launcher = null;
        private HashSet<string> _opened = new HashSet<string>();
        private HashSet<string> _managed = new HashSet<string>();
        private List<RepositoryNode> _discovered = [];
        private Dictionary<RepositoryNode, string> _discoveredRoots = new Dictionary<RepositoryNode, string>();
        private List<LauncherPage> _visiblePages = [];
        private List<RepositoryNode> _visibleRepos = [];
        private List<Models.GitHubRepository> _visibleGitHubRepos = [];
        private Models.GitHubRepository _selectedGitHubRepo = null;
        private string _searchFilter = string.Empty;
        private LauncherPage _selectedPage = null;
        private RepositoryNode _selectedRepo = null;
    }
}
