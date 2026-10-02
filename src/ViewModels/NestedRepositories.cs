using System;
using System.Collections.Generic;
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
    ///     Independent git repositories living directly inside the root repository's folder (typically ignored by
    ///     the root repository). All of them are shown in the same page and share its tab.
    /// </summary>
    public class NestedRepositories : ObservableObject
    {
        public Repository Root
        {
            get => _root.Repo;
        }

        public AvaloniaList<NestedRepository> Items
        {
            get;
        } = [];

        public NestedRepository Selected
        {
            get => _selected;
            set
            {
                if (value == null || !SetProperty(ref _selected, value))
                    return;

                foreach (var item in Items)
                    item.Repo.IsBackground = item != value;

                _root.Repo.UIStates.SelectedNestedRepository = value.IsRoot ? string.Empty : value.Name;
                _page.Data = value.Repo;
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
            return nested;
        }

        public bool Contains(Repository repo)
        {
            foreach (var item in Items)
            {
                if (item.Repo == repo)
                    return true;
            }

            return false;
        }

        public bool TrySelect(string fullpath)
        {
            foreach (var item in Items)
            {
                if (item.Repo.FullPath.Equals(fullpath, StringComparison.Ordinal))
                {
                    Selected = item;
                    return true;
                }
            }

            return false;
        }

        public void Rescan()
        {
            Sync(Scan(_root.Repo.FullPath));
        }

        public void RefreshAll()
        {
            Rescan();

            foreach (var item in Items)
                item.Repo.RefreshAll();
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
            foreach (var item in Items)
            {
                if (!item.IsRoot)
                    item.Repo.Close();
            }

            Items.Clear();
        }

        private NestedRepositories(LauncherPage page, Repository root)
        {
            _page = page;
            _root = new NestedRepository(Path.GetFileName(root.FullPath), true, root);
            Items.Add(_root);
        }

        private static List<string> Scan(string root)
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

        private static bool HasValidUpstream(Models.Branch branch)
        {
            return branch is { IsDetachedHead: false, IsUpstreamGone: false } && !string.IsNullOrEmpty(branch.Upstream);
        }

        private void Sync(List<string> paths)
        {
            var existing = new Dictionary<string, NestedRepository>();
            for (var i = Items.Count - 1; i >= 1; i--)
            {
                var item = Items[i];
                if (paths.Contains(item.Repo.FullPath))
                {
                    existing.Add(item.Repo.FullPath, item);
                }
                else
                {
                    if (_selected == item)
                        Selected = _root;

                    item.Repo.Close();
                }

                Items.RemoveAt(i);
            }

            foreach (var path in paths)
            {
                if (!existing.TryGetValue(path, out var item))
                {
                    var repo = new Repository(false, path, $"{path}/.git") { IsBackground = true };
                    repo.Open();
                    item = new NestedRepository(Path.GetFileName(path), false, repo);
                }

                Items.Add(item);
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
        private NestedRepository _selected = null;
        private bool _isBusy = false;
        private string _busyDescription = string.Empty;
    }
}
