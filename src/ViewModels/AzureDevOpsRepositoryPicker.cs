using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     Searchable list of the Azure DevOps repositories the signed-in account can access, filtered by project.
    /// </summary>
    public class AzureDevOpsRepositoryPicker : ObservableObject, IDisposable
    {
        public AzureDevOpsAccount Account => AzureDevOpsAccount.Instance;

        /// <summary>
        ///     Project names, prefixed with their organization when the account is a member of more than one.
        /// </summary>
        public List<string> Projects
        {
            get => _projects;
            private set => SetProperty(ref _projects, value);
        }

        public string SelectedProject
        {
            get => _selectedProject;
            set
            {
                if (SetProperty(ref _selectedProject, value))
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

        public List<Models.AzureDevOpsRepository> VisibleRepos
        {
            get => _visibleRepos;
            private set => SetProperty(ref _visibleRepos, value);
        }

        public Models.AzureDevOpsRepository SelectedRepo
        {
            get => _selectedRepo;
            set => SetProperty(ref _selectedRepo, value);
        }

        public AzureDevOpsRepositoryPicker()
        {
            Account.PropertyChanged += OnAccountPropertyChanged;
            OnRepositoriesChanged();
            _ = Account.RefreshAsync(false);

            Task.Run(() =>
            {
                var cloned = Models.AzureDevOps.CollectClonedFullNames(GitHubAccount.CollectLocalRepositoryPaths());
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

        public void OpenOrganizations()
        {
            Native.OS.OpenBrowser(Models.AzureDevOps.OrganizationsUrl);
        }

        private void OnAccountPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AzureDevOpsAccount.Repositories))
                OnRepositoriesChanged();
        }

        private void OnRepositoriesChanged()
        {
            var organizations = new HashSet<string>();
            foreach (var repo in Account.Repositories)
                organizations.Add(repo.Organization);

            _projectOf.Clear();
            var projects = new List<string>();
            foreach (var repo in Account.Repositories)
            {
                var project = organizations.Count > 1 ? $"{repo.Organization}/{repo.Project.Name}" : repo.Project.Name;
                if (!projects.Contains(project))
                    projects.Add(project);

                _projectOf[repo] = project;
            }

            projects.Sort(StringComparer.OrdinalIgnoreCase);
            projects.Insert(0, _allProjects);

            Projects = projects;
            if (!projects.Contains(_selectedProject))
                _selectedProject = _allProjects;
            OnPropertyChanged(nameof(SelectedProject));

            UpdateVisible();
        }

        private void UpdateVisible()
        {
            var repos = Account.Repositories;
            AzureDevOpsAccount.MarkCloned(repos, _cloned);

            var visible = new List<Models.AzureDevOpsRepository>();
            foreach (var repo in repos)
            {
                if (_selectedProject != _allProjects && !_selectedProject.Equals(_projectOf.GetValueOrDefault(repo), StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrEmpty(_searchFilter) && !repo.FullName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                visible.Add(repo);
            }

            var selected = _selectedRepo;
            VisibleRepos = visible;
            SelectedRepo = selected != null && visible.Contains(selected) ? selected : (visible.Count > 0 ? visible[0] : null);
        }

        private readonly string _allProjects = App.Text("AzureDevOps.Picker.AllProjects");

        private List<string> _projects = [];
        private Dictionary<Models.AzureDevOpsRepository, string> _projectOf = [];
        private string _selectedProject = null;
        private string _searchFilter = string.Empty;
        private HashSet<string> _cloned = [];
        private List<Models.AzureDevOpsRepository> _visibleRepos = [];
        private Models.AzureDevOpsRepository _selectedRepo = null;
    }
}
