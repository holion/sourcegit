using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class LauncherPagesCommandPalette : UserControl
    {
        public LauncherPagesCommandPalette()
        {
            InitializeComponent();
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            FilterTextBox.Focus(NavigationMethod.Directional);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (DataContext is not ViewModels.LauncherPagesCommandPalette vm)
                return;

            if (e.Key == Key.Enter)
            {
                vm.OpenOrSwitchTo();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                var idx = GetFocusedSection();
                if (idx < 0)
                    return;

                // Move to the last item of the previous non-empty section, or back to the filter box.
                if (!FocusSection(vm, idx - 1, -1, false))
                    FilterTextBox.Focus(NavigationMethod.Directional);

                e.Handled = true;
            }
            else if (e.Key == Key.Down || e.Key == Key.Tab)
            {
                var idx = FilterTextBox.IsKeyboardFocusWithin ? -1 : GetFocusedSection();
                if (idx < 0 && !FilterTextBox.IsKeyboardFocusWithin)
                    return;

                // Move to the first item of the next non-empty section. Tab wraps around to the filter box.
                if (!FocusSection(vm, idx + 1, 1, true) && e.Key == Key.Tab)
                    FilterTextBox.Focus(NavigationMethod.Directional);

                e.Handled = true;
            }
        }

        private int GetFocusedSection()
        {
            if (PageListBox.IsKeyboardFocusWithin)
                return 0;
            if (RepoListBox.IsKeyboardFocusWithin)
                return 1;
            if (GitHubRepoListBox.IsKeyboardFocusWithin)
                return 2;
            return -1;
        }

        private bool FocusSection(ViewModels.LauncherPagesCommandPalette vm, int idx, int step, bool first)
        {
            for (; idx >= 0 && idx <= 2; idx += step)
            {
                switch (idx)
                {
                    case 0 when vm.VisiblePages.Count > 0:
                        PageListBox.Focus(NavigationMethod.Directional);
                        vm.SelectedPage = first ? vm.VisiblePages[0] : vm.VisiblePages[^1];
                        return true;
                    case 1 when vm.VisibleRepos.Count > 0:
                        RepoListBox.Focus(NavigationMethod.Directional);
                        vm.SelectedRepo = first ? vm.VisibleRepos[0] : vm.VisibleRepos[^1];
                        return true;
                    case 2 when vm.VisibleGitHubRepos.Count > 0:
                        GitHubRepoListBox.Focus(NavigationMethod.Directional);
                        vm.SelectedGitHubRepo = first ? vm.VisibleGitHubRepos[0] : vm.VisibleGitHubRepos[^1];
                        return true;
                }
            }

            return false;
        }

        private void OnItemTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is ViewModels.LauncherPagesCommandPalette vm)
            {
                vm.OpenOrSwitchTo();
                e.Handled = true;
            }
        }
    }
}
