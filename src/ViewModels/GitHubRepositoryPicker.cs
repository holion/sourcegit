using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Searchable list of the GitHub repositories the signed-in account can access, filtered by owner.
    /// </summary>
    public class GitHubRepositoryPicker : ObservableObject, IDisposable
    {
        public GitHubAccount Account => GitHubAccount.Instance;

        public List<string> Owners
        {
            get => _owners;
            private set => SetProperty(ref _owners, value);
        }

        public string SelectedOwner
        {
            get => _selectedOwner;
            set
            {
                if (SetProperty(ref _selectedOwner, value))
                    UpdateVisible();
            }
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

        public List<Models.GitHubRepository> VisibleRepos
        {
            get => _visibleRepos;
            private set => SetProperty(ref _visibleRepos, value);
        }

        public Models.GitHubRepository SelectedRepo
        {
            get => _selectedRepo;
            set => SetProperty(ref _selectedRepo, value);
        }

        public GitHubRepositoryPicker()
        {
            Account.PropertyChanged += OnAccountPropertyChanged;
            OnRepositoriesChanged();
            _ = Account.RefreshAsync(false);

            Task.Run(() =>
            {
                var cloned = Models.GitHub.CollectClonedFullNames(GitHubAccount.CollectLocalRepositoryPaths());
                Dispatcher.UIThread.Post(() =>
                {
                    _cloned = cloned;
                    UpdateVisible();
                });
            });
        }

        public void Dispose()
        {
            Account.PropertyChanged -= OnAccountPropertyChanged;
        }

        public void ClearFilter()
        {
            SearchFilter = string.Empty;
        }

        public void Refresh()
        {
            _ = Account.RefreshAsync(true);
        }

        public void GrantOrganizationAccess()
        {
            Native.OS.OpenBrowser(Models.GitHub.ApplicationSettingsUrl);
        }

        private void OnAccountPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GitHubAccount.Repositories))
                OnRepositoriesChanged();
        }

        private void OnRepositoriesChanged()
        {
            var owners = new List<string>();
            foreach (var repo in Account.Repositories)
            {
                if (!owners.Contains(repo.Owner.Login))
                    owners.Add(repo.Owner.Login);
            }

            // The signed-in user first, then organizations alphabetically.
            var login = Account.Login;
            owners.Sort((l, r) =>
            {
                if (l.Equals(login, StringComparison.OrdinalIgnoreCase))
                    return -1;
                if (r.Equals(login, StringComparison.OrdinalIgnoreCase))
                    return 1;
                return string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
            });
            owners.Insert(0, _allOwners);

            Owners = owners;
            if (!owners.Contains(_selectedOwner))
                _selectedOwner = _allOwners;
            OnPropertyChanged(nameof(SelectedOwner));

            UpdateVisible();
        }

        private void UpdateVisible()
        {
            var repos = Account.Repositories;
            GitHubAccount.MarkCloned(repos, _cloned);

            var visible = new List<Models.GitHubRepository>();
            foreach (var repo in repos)
            {
                if (_selectedOwner != _allOwners && !repo.Owner.Login.Equals(_selectedOwner, StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !repo.FullName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) &&
                    !(repo.Description?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                    continue;

                visible.Add(repo);
            }

            var selected = _selectedRepo;
            VisibleRepos = visible;
            SelectedRepo = selected != null && visible.Contains(selected) ? selected : (visible.Count > 0 ? visible[0] : null);
        }

        private readonly string _allOwners = App.Text("GitHub.Picker.AllOwners");

        private List<string> _owners = [];
        private string _selectedOwner = null;
        private string _searchFilter = string.Empty;
        private HashSet<string> _cloned = [];
        private List<Models.GitHubRepository> _visibleRepos = [];
        private Models.GitHubRepository _selectedRepo = null;
    }
}
