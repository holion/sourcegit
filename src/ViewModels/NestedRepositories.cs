using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class NestedRepository
    {
        public string Name { get; }
        public bool IsRoot { get; }
        public Repository Repo { get; }

        public NestedRepository(string name, bool isRoot, Repository repo)
        {
            Name = name;
            IsRoot = isRoot;
            Repo = repo;
        }
    }

    /// <summary>
    ///     A set of linked worktrees of the nested repositories that belong together: the worktrees laid out as
    ///     `<group>/<repository name>` share the `<group>` folder, any other worktree is a group of its own. The
    ///     repositories of a group are opened the first time it is selected.
    /// </summary>
    public class NestedWorktree : ObservableObject
    {
        public string Key { get; }
        public string Name { get; }
        public bool IsAll { get; }
        public AvaloniaList<NestedRepository> Items { get; } = [];
        public NestedRepository LastSelected { get; set; }

        public string Branch
        {
            get => _branch;
            private set => SetProperty(ref _branch, value);
        }

        public int Count
        {
            get => IsAll ? Items.Count : _members.Count;
        }

        public NestedWorktree(string key, string name, bool isAll)
        {
            Key = key;
            Name = name;
            IsAll = isAll;

            if (isAll)
                Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));
        }

        public void SetMembers(List<Member> members)
        {
            _members = members;

            var branch = members.Count > 0 ? members[0].Branch : string.Empty;
            foreach (var m in members)
            {
                if (!m.Branch.Equals(branch, StringComparison.Ordinal))
                    branch = string.Empty;
            }

            Branch = branch;
            OnPropertyChanged(nameof(Count));
            if (_isOpened)
                SyncItems();
        }

        public void Open()
        {
            // The items of the group with all repositories are maintained by `NestedRepositories` itself.
            if (IsAll || _isOpened)
                return;

            _isOpened = true;
            SyncItems();
        }

        public void Close()
        {
            foreach (var item in Items)
                item.Repo.Close();

            Items.Clear();
            LastSelected = null;
        }

        private void SyncItems()
        {
            // The worktrees are read again on every branch change, so leave the list alone when nothing moved.
            if (Items.Count == _members.Count)
            {
                var same = true;
                for (var i = 0; i < Items.Count && same; i++)
                    same = Items[i].Repo.FullPath.Equals(_members[i].Path, StringComparison.Ordinal);

                if (same)
                    return;
            }

            var existing = new Dictionary<string, NestedRepository>();
            foreach (var item in Items)
                existing.Add(item.Repo.FullPath, item);

            Items.Clear();
            foreach (var m in _members)
            {
                if (existing.Remove(m.Path, out var item))
                {
                    Items.Add(item);
                    continue;
                }

                var gitDir = NestedRepositories.GetWorktreeGitDir(m.Path);
                if (string.IsNullOrEmpty(gitDir))
                    continue;

                var repo = new Repository(false, m.Path, gitDir) { IsBackground = true };
                repo.Open();
                Items.Add(new NestedRepository(m.Name, false, repo));
            }

            foreach (var kv in existing)
            {
                if (LastSelected == kv.Value)
                    LastSelected = null;

                kv.Value.Repo.Close();
            }
        }

        public record Member(string Name, string Path, string Branch);

        private List<Member> _members = [];
        private bool _isOpened = false;
        private string _branch = string.Empty;
    }

    /// <summary>
    ///     Independent git repositories living directly inside the root repository's folder (typically ignored by
    ///     the root repository). All of them are shown in the same page and share its tab.
    /// </summary>
    public class NestedRepositories : ObservableObject
    {
        public Repository Root
        {
            get => _root.Repo;
        }

        /// <summary>
        ///     The repositories shown in the list: all nested repositories, or those of the selected worktree.
        /// </summary>
        public AvaloniaList<NestedRepository> Items
        {
            get => _worktree.Items;
        }

        public NestedRepository Selected
        {
            get => _selected;
            set
            {
                if (value == null || !SetProperty(ref _selected, value))
                    return;

                foreach (var repo in AllRepositories)
                    repo.IsBackground = repo != value.Repo;

                _worktree.LastSelected = value;
                _pendingWorktree = null;
                _root.Repo.UIStates.SelectedNestedRepository = value.IsRoot ? string.Empty : value.Name;
                _page.Data = value.Repo;
            }
        }

        /// <summary>
        ///     The first item shows all nested repositories, the others are the worktrees found in them.
        /// </summary>
        public AvaloniaList<NestedWorktree> Worktrees
        {
            get;
        } = [];

        public NestedWorktree SelectedWorktree
        {
            get => _worktree;
            set
            {
                if (value == null || !SetProperty(ref _worktree, value))
                    return;

                value.Open();
                _root.Repo.UIStates.SelectedNestedWorktree = value.IsAll ? string.Empty : value.Key;
                OnPropertyChanged(nameof(Items));

                if (value.Items.Count == 0)
                {
                    SelectedWorktree = _all;
                    return;
                }

                // Keep the same repository when it is part of the worktree too.
                var next = value.LastSelected;
                if (next == null && _selected != null)
                {
                    foreach (var item in value.Items)
                    {
                        if (item.Name.Equals(_selected.Name, StringComparison.Ordinal))
                            next = item;
                    }
                }

                Selected = next ?? value.Items[0];
            }
        }

        public bool HasWorktrees
        {
            get => _hasWorktrees;
            private set => SetProperty(ref _hasWorktrees, value);
        }

        /// <summary>
        ///     Every repository opened by this page, including the worktrees' ones.
        /// </summary>
        public IEnumerable<Repository> AllRepositories
        {
            get
            {
                foreach (var wt in Worktrees)
                {
                    foreach (var item in wt.Items)
                        yield return item.Repo;
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public string BusyDescription
        {
            get => _busyDescription;
            private set => SetProperty(ref _busyDescription, value);
        }

        public static NestedRepositories TryCreate(LauncherPage page, Repository root)
        {
            if (root.IsBare)
                return null;

            var paths = Scan(root.FullPath);
            if (paths.Count == 0)
                return null;

            var nested = new NestedRepositories(page, root);
            nested.Sync(paths);

            var last = root.UIStates.SelectedNestedRepository;
            nested.Selected = nested._root;
            if (!string.IsNullOrEmpty(last))
            {
                foreach (var item in nested.Items)
                {
                    if (!item.IsRoot && item.Name.Equals(last, StringComparison.Ordinal))
                        nested.Selected = item;
                }
            }

            // The worktrees are read in the background, so the last selected one is restored once it shows up.
            var lastWorktree = root.UIStates.SelectedNestedWorktree;
            if (!string.IsNullOrEmpty(lastWorktree))
                nested._pendingWorktree = lastWorktree;

            return nested;
        }

        public bool Contains(Repository repo)
        {
            foreach (var r in AllRepositories)
            {
                if (r == repo)
                    return true;
            }

            return false;
        }

        public bool TrySelect(string fullpath)
        {
            foreach (var wt in Worktrees)
            {
                foreach (var item in wt.Items)
                {
                    if (item.Repo.FullPath.Equals(fullpath, StringComparison.Ordinal))
                    {
                        SelectedWorktree = wt;
                        Selected = item;
                        return true;
                    }
                }
            }

            return false;
        }

        public void SelectAdjacent(int delta)
        {
            if (Items.Count < 2)
                return;

            var idx = Items.IndexOf(_selected);
            var next = Math.Clamp(idx + delta, 0, Items.Count - 1);
            if (next != idx)
                Selected = Items[next];
        }

        public void Rescan()
        {
            Sync(Scan(_root.Repo.FullPath));
        }

        public void RefreshAll()
        {
            Rescan();

            // The nested repositories are always refreshed, as their worktrees make up the worktree list.
            foreach (var item in _all.Items)
                item.Repo.RefreshAll();

            if (!_worktree.IsAll)
            {
                foreach (var item in _worktree.Items)
                    item.Repo.RefreshAll();
            }
        }

        public Task FetchAllAsync()
        {
            return RunAllAsync(App.Text("Fetch"), repo => repo.Remotes.Count > 0 ? new Fetch(repo) : null);
        }

        public Task PullAllAsync()
        {
            return RunAllAsync(App.Text("Pull"), repo =>
            {
                if (repo.IsBare || repo.Remotes.Count == 0 || !HasValidUpstream(repo.CurrentBranch))
                    return null;

                return new Pull(repo, null);
            });
        }

        public Task PushAllAsync()
        {
            return RunAllAsync(App.Text("Push"), repo =>
            {
                if (repo.IsBare || repo.Remotes.Count == 0 || !HasValidUpstream(repo.CurrentBranch) || repo.CurrentBranch.Ahead.Count == 0)
                    return null;

                return new Push(repo, null);
            });
        }

        /// <summary>
        ///     Closes all nested repositories except the root one, which is owned by the launcher.
        /// </summary>
        public void Close()
        {
            foreach (var item in _all.Items)
            {
                item.Repo.PropertyChanged -= OnRepositoryPropertyChanged;
                if (!item.IsRoot)
                    item.Repo.Close();
            }

            _all.Items.Clear();

            foreach (var wt in Worktrees)
            {
                if (!wt.IsAll)
                    wt.Close();
            }

            Worktrees.Clear();
        }

        private NestedRepositories(LauncherPage page, Repository root)
        {
            _page = page;
            _root = new NestedRepository(Path.GetFileName(root.FullPath), true, root);
            _all = new NestedWorktree(string.Empty, App.Text("NestedRepos.AllRepositories"), true);
            _all.Items.Add(_root);
            _worktree = _all;
            Worktrees.Add(_all);
            root.PropertyChanged += OnRepositoryPropertyChanged;
        }

        public static List<string> Scan(string root)
        {
            var paths = new List<string>();

            try
            {
                foreach (var dir in new DirectoryInfo(root).EnumerateDirectories())
                {
                    if (dir.Name.StartsWith('.'))
                        continue;

                    // Only folders with their own `.git` directory are independent repositories. A `.git` file
                    // means a submodule or a linked worktree, which are handled elsewhere.
                    var gitDir = Path.Combine(dir.FullName, ".git");
                    if (Directory.Exists(Path.Combine(gitDir, "refs")) &&
                        Directory.Exists(Path.Combine(gitDir, "objects")) &&
                        File.Exists(Path.Combine(gitDir, "HEAD")))
                        paths.Add(dir.FullName.Replace('\\', '/').TrimEnd('/'));
                }
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
            }

            paths.Sort((l, r) => Models.NumericSort.Compare(Path.GetFileName(l), Path.GetFileName(r)));
            return paths;
        }

        /// <summary>
        ///     Resolves the git dir of a linked worktree from the `gitdir: ` line in its `.git` file.
        /// </summary>
        public static string GetWorktreeGitDir(string path)
        {
            try
            {
                var file = Path.Combine(path, ".git");
                if (!File.Exists(file))
                    return null;

                var redirect = File.ReadAllText(file).Trim();
                if (!redirect.StartsWith("gitdir: ", StringComparison.Ordinal))
                    return null;

                redirect = redirect.Substring(8).Trim();
                if (!Path.IsPathRooted(redirect))
                    redirect = Path.GetFullPath(Path.Combine(path, redirect));

                return Directory.Exists(redirect) ? redirect : null;
            }
            catch (Exception e)
            {
                Native.OS.LogException(e);
                return null;
            }
        }

        private static bool HasValidUpstream(Models.Branch branch)
        {
            return branch is { IsDetachedHead: false, IsUpstreamGone: false } && !string.IsNullOrEmpty(branch.Upstream);
        }

        private void Sync(List<string> paths)
        {
            var items = _all.Items;
            var existing = new Dictionary<string, NestedRepository>();
            for (var i = items.Count - 1; i >= 1; i--)
            {
                var item = items[i];
                if (paths.Contains(item.Repo.FullPath))
                {
                    existing.Add(item.Repo.FullPath, item);
                }
                else
                {
                    if (_selected == item)
                        Selected = _root;
                    if (_all.LastSelected == item)
                        _all.LastSelected = null;

                    item.Repo.PropertyChanged -= OnRepositoryPropertyChanged;
                    item.Repo.Close();
                }

                items.RemoveAt(i);
            }

            foreach (var path in paths)
            {
                if (!existing.TryGetValue(path, out var item))
                {
                    var repo = new Repository(false, path, $"{path}/.git") { IsBackground = true };
                    repo.PropertyChanged += OnRepositoryPropertyChanged;
                    repo.Open();
                    item = new NestedRepository(Path.GetFileName(path), false, repo);
                }

                items.Add(item);
            }

            SyncWorktrees();
        }

        private void OnRepositoryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Repository.Worktrees))
                SyncWorktrees();
        }

        /// <summary>
        ///     Rebuilds the worktree groups from the linked worktrees of the nested repositories.
        /// </summary>
        private void SyncWorktrees()
        {
            var groups = new Dictionary<string, List<NestedWorktree.Member>>();
            foreach (var item in _all.Items)
            {
                var repoName = Path.GetFileName(item.Repo.FullPath);
                foreach (var wt in item.Repo.Worktrees)
                {
                    if (wt.IsMain || wt.IsCurrent || wt.Backend.IsBare)
                        continue;

                    var path = wt.FullPath.Replace('\\', '/').TrimEnd('/');
                    if (!Directory.Exists(path))
                        continue;

                    var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    var key = Path.GetFileName(path).Equals(repoName, StringComparison.Ordinal) && !string.IsNullOrEmpty(parent) ? parent : path;
                    if (!groups.TryGetValue(key, out var members))
                    {
                        members = [];
                        groups.Add(key, members);
                    }

                    members.Add(new NestedWorktree.Member(item.Name, path, wt.Branch));
                }
            }

            for (var i = Worktrees.Count - 1; i >= 1; i--)
            {
                var wt = Worktrees[i];
                if (groups.Remove(wt.Key, out var members))
                {
                    wt.SetMembers(members);
                    if (wt == _worktree && wt.Items.Count == 0)
                        SelectedWorktree = _all;
                    else if (wt == _worktree && _selected != null && !wt.Items.Contains(_selected))
                        Selected = wt.Items[0];
                    continue;
                }

                if (wt == _worktree)
                    SelectedWorktree = _all;

                Worktrees.RemoveAt(i);
                wt.Close();
            }

            foreach (var kv in groups)
            {
                var wt = new NestedWorktree(kv.Key, Path.GetFileName(kv.Key), false);
                wt.SetMembers(kv.Value);

                var idx = 1;
                while (idx < Worktrees.Count && Models.NumericSort.Compare(Worktrees[idx].Name, wt.Name) < 0)
                    idx++;
                Worktrees.Insert(idx, wt);
            }

            HasWorktrees = Worktrees.Count > 1;

            if (!string.IsNullOrEmpty(_pendingWorktree))
            {
                foreach (var wt in Worktrees)
                {
                    if (wt.Key.Equals(_pendingWorktree, StringComparison.Ordinal))
                    {
                        _pendingWorktree = null;
                        var name = _root.Repo.UIStates.SelectedNestedRepository;
                        SelectedWorktree = wt;
                        foreach (var item in wt.Items)
                        {
                            if (item.Name.Equals(name, StringComparison.Ordinal))
                                Selected = item;
                        }

                        break;
                    }
                }
            }
        }

        private async Task RunAllAsync(string action, Func<Repository, Popup> factory)
        {
            if (_isBusy || !_page.CanCreatePopup())
                return;

            var jobs = new List<Popup>();
            var skipped = 0;
            foreach (var item in Items)
            {
                item.Repo.CancelAutoFetch();

                var job = factory(item.Repo);
                if (job == null)
                    skipped++;
                else
                    jobs.Add(job);
            }

            var finished = 0;
            var failed = 0;
            IsBusy = true;
            BusyDescription = $"{action} 0/{jobs.Count}";

            // Everything runs on the UI thread, the git processes themselves run concurrently.
            using var limiter = new SemaphoreSlim(MaxConcurrentJobs);
            var tasks = new List<Task>();
            foreach (var job in jobs)
                tasks.Add(RunJobAsync(job));

            async Task RunJobAsync(Popup job)
            {
                await limiter.WaitAsync();

                try
                {
                    if (!job.Check() || !await job.Sure())
                        failed++;
                }
                catch (Exception e)
                {
                    Native.OS.LogException(e);
                    failed++;
                }
                finally
                {
                    limiter.Release();
                    job.Cleanup();
                    finished++;
                    BusyDescription = $"{action} {finished}/{jobs.Count}";
                }
            }

            await Task.WhenAll(tasks);

            IsBusy = false;
            BusyDescription = string.Empty;
            Models.Notification.Send(_root.Repo.FullPath, App.Text("NestedRepos.Summary", action, jobs.Count - failed, failed, skipped), failed > 0);
        }

        private const int MaxConcurrentJobs = 4;

        private readonly LauncherPage _page;
        private readonly NestedRepository _root;
        private readonly NestedWorktree _all;
        private NestedWorktree _worktree;
        private NestedRepository _selected = null;
        private string _pendingWorktree = null;
        private bool _hasWorktrees = false;
        private bool _isBusy = false;
        private string _busyDescription = string.Empty;
    }
}
