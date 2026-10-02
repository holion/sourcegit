using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SourceGit.Views
{
    public partial class DiffView : UserControl
    {
        public DiffView()
        {
            InitializeComponent();
        }

        public void ToggleHotkeyBindings(bool enabled)
        {
            _hotkeysEnabled = enabled;

            if (enabled)
            {
                BtnGotoFirstChange.HotKey = Models.Shortcuts.GotoFirstChange.Gesture;
                BtnGotoPrevChange.HotKey = Models.Shortcuts.GotoPrevChange.Gesture;
                BtnGotoNextChange.HotKey = Models.Shortcuts.GotoNextChange.Gesture;
                BtnGotoLastChange.HotKey = Models.Shortcuts.GotoLastChange.Gesture;
                BtnOpenExternalMergeTool.HotKey = Models.Shortcuts.OpenExternalMergeTool.Gesture;
            }
            else
            {
                BtnGotoFirstChange.HotKey = null;
                BtnGotoPrevChange.HotKey = null;
                BtnGotoNextChange.HotKey = null;
                BtnGotoLastChange.HotKey = null;
                BtnOpenExternalMergeTool.HotKey = null;
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (DataContext is ViewModels.DiffContext vm)
                vm.CheckSettings();

            ToggleHotkeyBindings(IsEffectivelyVisible);
            Models.Shortcuts.Changed += OnShortcutsChanged;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            Models.Shortcuts.Changed -= OnShortcutsChanged;
            ToggleHotkeyBindings(false);
        }

        private void OnShortcutsChanged()
        {
            if (_hotkeysEnabled)
                ToggleHotkeyBindings(true);
        }

        private void OnGotoFirstChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.First);
            e.Handled = true;
        }

        private void OnGotoPrevChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Prev);
            e.Handled = true;
        }

        private void OnGotoNextChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Next);
            e.Handled = true;
        }

        private void OnGotoLastChange(object _, RoutedEventArgs e)
        {
            this.FindDescendantOfType<ThemedTextDiffPresenter>()?.GotoChange(ViewModels.BlockNavigationDirection.Last);
            e.Handled = true;
        }

        private void OnToggleButtonPropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ToggleButton.IsCheckedProperty && DataContext is ViewModels.DiffContext vm)
                vm.CheckSettings();
        }

        private void OnOpenSubmoduleRevisionCompare(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.SubmoduleDiff diff } && diff.CanOpenDetails)
            {
                var vm = new ViewModels.SubmoduleRevisionCompare(diff);
                this.ShowWindow(vm);
            }
        }

        private async void OnOpenBinaryFileViewer(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: Models.BinaryDiff diff } && diff.NewSize > 0)
            {
                await this.ShowDialogAsync(new ViewModels.BinaryFileViewer(diff.Repository, diff.FilePath, diff.NewRevision));
                e.Handled = true;
            }
        }

        private bool _hotkeysEnabled = false;
    }
}
