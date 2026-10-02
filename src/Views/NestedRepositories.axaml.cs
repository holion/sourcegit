using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class NestedRepositories : UserControl
    {
        public NestedRepositories()
        {
            InitializeComponent();
        }

        private async void OnFetchAll(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is ViewModels.NestedRepositories vm)
                await vm.FetchAllAsync();
        }

        private async void OnPullAll(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is ViewModels.NestedRepositories vm)
                await vm.PullAllAsync();
        }

        private async void OnPushAll(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is ViewModels.NestedRepositories vm)
                await vm.PushAllAsync();
        }

        private void OnRefreshAll(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.NestedRepositories vm)
                vm.RefreshAll();

            e.Handled = true;
        }

        private void OnResizePointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            _resizeStartX = e.GetPosition(this).X;
            _resizeStartWidth = Bounds.Width;
            _isResizing = true;
            e.Pointer.Capture(sender as IInputElement);
            e.Handled = true;
        }

        private void OnResizePointerMoved(object sender, PointerEventArgs e)
        {
            if (!_isResizing)
                return;

            // The left edge of this control never moves, so positions relative to it are stable while resizing.
            var width = Math.Clamp(_resizeStartWidth + e.GetPosition(this).X - _resizeStartX, 160, 600);
            ViewModels.Preferences.Instance.Layout.NestedRepositoriesWidth = width;
            e.Handled = true;
        }

        private void OnResizePointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!_isResizing)
                return;

            _isResizing = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private bool _isResizing = false;
        private double _resizeStartX = 0;
        private double _resizeStartWidth = 0;
    }
}
