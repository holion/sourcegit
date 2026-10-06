using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class AzureDevOpsRepositoryPicker : ChromelessWindow
    {
        public AzureDevOpsRepositoryPicker()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Signs in first when needed, then lets the user pick one of their Azure DevOps repositories. Returns null when
        ///     cancelled.
        /// </summary>
        public static async Task<Models.AzureDevOpsRepository> ShowAsync(Control anchor)
        {
            if (!await AzureDevOpsSignIn.ShowAsync(anchor))
                return null;

            if (TopLevel.GetTopLevel(anchor) is not Window owner)
                return null;

            using var vm = new ViewModels.AzureDevOpsRepositoryPicker();
            var dialog = new AzureDevOpsRepositoryPicker() { DataContext = vm };
            return await dialog.ShowDialog<Models.AzureDevOpsRepository>(owner);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            FilterTextBox.Focus(NavigationMethod.Directional);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || DataContext is not ViewModels.AzureDevOpsRepositoryPicker vm)
                return;

            if (e.Key == Key.Enter)
            {
                Pick(vm);
                e.Handled = true;
            }
            else if (e.Key == Key.Down && FilterTextBox.IsKeyboardFocusWithin && vm.VisibleRepos.Count > 0)
            {
                RepoListBox.Focus(NavigationMethod.Directional);
                vm.SelectedRepo ??= vm.VisibleRepos[0];
                RepoListBox.ContainerFromItem(vm.SelectedRepo)?.Focus(NavigationMethod.Directional);
                e.Handled = true;
            }
        }

        private void Pick(ViewModels.AzureDevOpsRepositoryPicker vm)
        {
            if (vm.SelectedRepo != null)
                Close(vm.SelectedRepo);
        }

        private void OnItemDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.AzureDevOpsRepositoryPicker vm)
                Pick(vm);

            e.Handled = true;
        }

        private void OnOpenOrganizations(object sender, PointerPressedEventArgs e)
        {
            (DataContext as ViewModels.AzureDevOpsRepositoryPicker)?.OpenOrganizations();
            e.Handled = true;
        }

        private void OnClone(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.AzureDevOpsRepositoryPicker vm)
                Pick(vm);

            e.Handled = true;
        }

        private void OnCancel(object _, RoutedEventArgs e)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
