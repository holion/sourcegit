using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace SourceGit.Views
{
    public partial class Launcher : ChromelessWindow
    {
        public static readonly DirectProperty<Launcher, GridLength> CaptionHeightProperty =
            AvaloniaProperty.RegisterDirect<Launcher, GridLength>(
                nameof(CaptionHeight),
                static o => o.CaptionHeight);

        public GridLength CaptionHeight
        {
            get => _captionHeight;
            set => SetAndRaise(CaptionHeightProperty, ref _captionHeight, value);
        }

        public bool HasRightCaptionButton
        {
            get
            {
                if (OperatingSystem.IsLinux())
                    return !Native.OS.UseSystemWindowFrame;

                return OperatingSystem.IsWindows();
            }
        }

        public Launcher()
        {
            if (OperatingSystem.IsMacOS())
                CaptionHeight = new GridLength(34);
            else if (UseSystemWindowFrame)
                CaptionHeight = new GridLength(30);
            else
                CaptionHeight = new GridLength(38);

            InitializeComponent();
            PositionChanged += OnPositionChanged;

            var layout = ViewModels.Preferences.Instance.Layout;
            Width = layout.LauncherWidth;
            Height = layout.LauncherHeight;

            var x = layout.LauncherPositionX;
            var y = layout.LauncherPositionY;
            if (x != int.MinValue && y != int.MinValue && Screens is { } screens)
            {
                var position = new PixelPoint(x, y);
                var size = new PixelSize((int)layout.LauncherWidth, (int)layout.LauncherHeight);
                var desiredRect = new PixelRect(position, size);
                for (var i = 0; i < screens.ScreenCount; i++)
                {
                    var screen = screens.All[i];
                    if (screen.WorkingArea.Contains(desiredRect))
                    {
                        Position = position;
                        return;
                    }
                }
            }

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        public void BringToTop()
        {
            if (WindowState == WindowState.Minimized)
                WindowState = _lastWindowState;
            else
                Activate();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            var preferences = ViewModels.Preferences.Instance;
            WindowState = preferences.Layout.LauncherWindowState;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == WindowStateProperty)
            {
                var state = (WindowState)change.NewValue!;
                _lastWindowState = (WindowState)change.OldValue!;

                if (!OperatingSystem.IsMacOS() && !UseSystemWindowFrame)
                    CaptionHeight = new GridLength(state == WindowState.Maximized ? 30 : 38);

                ViewModels.Preferences.Instance.Layout.LauncherWindowState = state;
            }
            else if (change.Property == IsActiveProperty)
            {
                if (!IsActive && DataContext is ViewModels.Launcher { CommandPalette: { } } vm)
                    vm.CommandPalette = null;
            }

            if (OperatingSystem.IsMacOS() && WindowState != WindowState.FullScreen)
            {
                if (change.Property == WindowStateProperty ||
                    change.Property == BoundsProperty ||
                    change.Property == TitleProperty)
                    Native.MacOSUtilities.AdjustTrafficLightsForThickTitleBar(this);
            }
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            if (WindowState == WindowState.Normal)
            {
                var layout = ViewModels.Preferences.Instance.Layout;
                layout.LauncherWidth = Width;
                layout.LauncherHeight = Height;
            }
        }

        protected override async void OnKeyDown(KeyEventArgs e)
        {
            if (DataContext is not ViewModels.Launcher vm)
                return;

            // Check for AltGr (which is detected as Ctrl+Alt)
            bool isAltGr = e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
                           e.KeyModifiers.HasFlag(KeyModifiers.Alt);

            // Skip hotkey processing if AltGr is pressed
            if (isAltGr)
            {
                base.OnKeyDown(e);
                return;
            }

            // Register hotkeys for Windows/Linux (macOS has registered these keys in system menu bar)
            if (!OperatingSystem.IsMacOS())
            {
                if (Models.Shortcuts.OpenPreferences.Matches(e))
                {
                    await this.ShowDialogAsync(new Preferences());
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.OpenHotkeys.Matches(e))
                {
                    await this.ShowDialogAsync(new Hotkeys());
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.Quit.Matches(e))
                {
                    App.Quit(0);
                    return;
                }
            }

            if (Models.Shortcuts.OpenTerminal.Matches(e))
            {
                if (vm.ActivePage.Data is ViewModels.Repository repo)
                    Native.OS.OpenTerminal(repo.FullPath);
                else
                    ViewModels.Welcome.Instance.OpenTerminal();

                e.Handled = true;
                return;
            }

            if (vm.CommandPalette != null)
            {
                if (e.Key == Key.Escape)
                {
                    vm.CommandPalette = null;
                    e.Handled = true;
                }
                else if (vm.ActivePage.Data is ViewModels.Repository repo
                    && vm.CommandPalette is ViewModels.LauncherPagesCommandPalette
                    && Models.Shortcuts.OpenCommandPalette.Matches(e))
                {
                    vm.CommandPalette = new ViewModels.RepositoryCommandPalette(repo);
                    e.Handled = true;
                }

                return;
            }

            if (Models.Shortcuts.CloseTab.Matches(e))
            {
                vm.CloseTab(null);
                e.Handled = true;
                return;
            }

            if (Models.Shortcuts.Clone.Matches(e))
            {
                if (vm.ActivePage.Data is not ViewModels.Welcome)
                    vm.AddNewTab();

                ViewModels.Welcome.Instance.Clone();
                e.Handled = true;
                return;
            }

            if (Models.Shortcuts.OpenLocalRepository.Matches(e))
            {
                if (vm.ActivePage.Data is not ViewModels.Welcome)
                    vm.AddNewTab();

                ViewModels.Welcome.Instance.OpenLocalRepository();
                e.Handled = true;
                return;
            }

            if (Models.Shortcuts.NewTab.Matches(e))
            {
                vm.AddNewTab();
                e.Handled = true;
                return;
            }

            if (Models.Shortcuts.GotoNextTab.Matches(e))
            {
                vm.GotoNextTab();
                e.Handled = true;
                return;
            }

            if (Models.Shortcuts.GotoPrevTab.Matches(e))
            {
                vm.GotoPrevTab();
                e.Handled = true;
                return;
            }

            if (vm.ActivePage.Data is ViewModels.Repository activeRepo)
            {
                if (Models.Shortcuts.ViewHistories.Matches(e))
                {
                    activeRepo.SelectedViewIndex = 0;
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.ViewChanges.Matches(e))
                {
                    activeRepo.SelectedViewIndex = 1;
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.ViewStashes.Matches(e))
                {
                    activeRepo.SelectedViewIndex = 2;
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.OpenCommandPalette.Matches(e))
                {
                    vm.CommandPalette = new ViewModels.RepositoryCommandPalette(activeRepo);
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.ToggleSearchCommits.Matches(e))
                {
                    activeRepo.Histories.IsSearchingCommits = !activeRepo.Histories.IsSearchingCommits;
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.CreateBranchFromCommit.Matches(e))
                {
                    if (activeRepo.CanCreatePopup() && activeRepo.GetSelectedCommitInHistory() is { } bc)
                        activeRepo.ShowPopup(new ViewModels.CreateBranch(activeRepo, bc));
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.CreateTagFromCommit.Matches(e))
                {
                    if (activeRepo.CanCreatePopup() && activeRepo.GetSelectedCommitInHistory() is { } tc)
                        activeRepo.ShowPopup(new ViewModels.CreateTag(activeRepo, tc));
                    e.Handled = true;
                    return;
                }

                if (Models.Shortcuts.OpenInFileManager.Matches(e))
                {
                    Native.OS.OpenInFileManager(activeRepo.FullPath);
                    e.Handled = true;
                    return;
                }

                // Opens the root repository of a page with nested repositories, or the selected one with Control held.
                var openSelectedInVSCode = Models.Shortcuts.OpenInVSCode.Matches(e, KeyModifiers.Control);
                if (openSelectedInVSCode || Models.Shortcuts.OpenInVSCode.Matches(e))
                {
                    var target = openSelectedInVSCode ? activeRepo : vm.ActivePage.RootRepository ?? activeRepo;
                    var vscode = Native.OS.ExternalTools.Find(x => x.Name.Equals("Visual Studio Code", StringComparison.Ordinal));
                    if (vscode != null)
                        vscode.Launch(target.FullPath.Quoted());
                    else
                        activeRepo.SendNotification("Visual Studio Code was not found.", true);

                    e.Handled = true;
                    return;
                }
            }

            if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
            {
                vm.ActivePage.CancelPopup();
                vm.ActivePage.Notifications.Clear();
                e.Handled = true;
                return;
            }

            if (Models.Shortcuts.Refresh.Matches(e))
            {
                if (vm.ActivePage.Data is ViewModels.Repository repo)
                {
                    repo.RefreshAll();
                    e.Handled = true;
                    return;
                }
                else if (vm.ActivePage.Data is ViewModels.Welcome welcome)
                {
                    e.Handled = true;
                    await welcome.UpdateStatusAsync(true, null);
                    return;
                }
            }

            base.OnKeyDown(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (!e.Handled)
            {
                if (e.Properties.PointerUpdateKind == PointerUpdateKind.XButton1Pressed)
                    (DataContext as ViewModels.Launcher)?.GotoPrevTab();
                else if (e.Properties.PointerUpdateKind == PointerUpdateKind.XButton2Pressed)
                    (DataContext as ViewModels.Launcher)?.GotoNextTab();
            }
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            if (!Design.IsDesignMode && DataContext is ViewModels.Launcher launcher)
                launcher.CloseAll();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (!Design.IsDesignMode)
                ViewModels.Preferences.Instance.Save();

            App.Quit(0);
        }

        private void OnPositionChanged(object sender, PixelPointEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                var layout = ViewModels.Preferences.Instance.Layout;
                layout.LauncherPositionX = Position.X;
                layout.LauncherPositionY = Position.Y;
            }
        }

        private void OnOpenWorkspaceMenu(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && DataContext is ViewModels.Launcher launcher)
            {
                if (launcher.CommandPalette != null)
                    launcher.CommandPalette = null;

                var pref = ViewModels.Preferences.Instance;
                var menu = new ContextMenu();
                menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
                menu.VerticalOffset = -6;

                var groupHeader = new TextBlock()
                {
                    Text = App.Text("Launcher.Workspaces"),
                    FontWeight = FontWeight.Bold,
                };

                var workspaces = new MenuItem();
                workspaces.Header = groupHeader;
                workspaces.IsEnabled = false;
                menu.Items.Add(workspaces);

                for (var i = 0; i < pref.Workspaces.Count; i++)
                {
                    var workspace = pref.Workspaces[i];

                    var icon = this.CreateMenuIcon(workspace.IsActive ? "Icons.Check" : "Icons.Workspace");
                    icon.Fill = workspace.Brush;

                    var item = new MenuItem();
                    item.Header = workspace.Name;
                    item.Icon = icon;
                    item.Click += (_, ev) =>
                    {
                        if (!workspace.IsActive)
                            launcher.SwitchWorkspace(workspace);

                        ev.Handled = true;
                    };

                    menu.Items.Add(item);
                }

                menu.Items.Add(new MenuItem() { Header = "-" });

                var configure = new MenuItem();
                configure.Header = App.Text("Workspace.Configure");
                configure.Click += async (_, ev) =>
                {
                    await this.ShowDialogAsync(new ViewModels.ConfigureWorkspace());
                    ev.Handled = true;
                };
                menu.Items.Add(configure);
                menu.Open(btn);
            }

            e.Handled = true;
        }

        private void OnOpenPagesCommandPalette(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Launcher vm)
                vm.CommandPalette = new ViewModels.LauncherPagesCommandPalette(vm);
            e.Handled = true;
        }

        private void OnCloseCommandPalette(object sender, PointerPressedEventArgs e)
        {
            if (e.Source == sender && DataContext is ViewModels.Launcher vm)
                vm.CommandPalette = null;
            e.Handled = true;
        }

        private async void OnShowNewVersion(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.Launcher { NewVersion: { } ver } vm)
            {
                if (!ver.IsReadyToInstall)
                    vm.NewVersion = null;

                var ctx = new ViewModels.SelfUpdate { Data = ver };
                var dialog = new SelfUpdate() { DataContext = ctx };
                await dialog.ShowDialog(this);
            }

            e.Handled = true;
        }

        private GridLength _captionHeight = new(32);
        private WindowState _lastWindowState = WindowState.Normal;
    }
}

