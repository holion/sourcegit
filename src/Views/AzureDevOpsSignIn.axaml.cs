using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class AzureDevOpsSignIn : ChromelessWindow
    {
        public AzureDevOpsSignIn()
        {
            InitializeComponent();
        }

        /// <summary>
        ///     Shows the sign-in dialog on top of the window hosting `anchor`. Returns true once signed in.
        /// </summary>
        public static async Task<bool> ShowAsync(Control anchor)
        {
            var account = ViewModels.AzureDevOpsAccount.Instance;
            if (account.IsSignedIn)
                return true;

            if (!account.IsAvailable || TopLevel.GetTopLevel(anchor) is not Window owner)
                return false;

            var dialog = new AzureDevOpsSignIn() { DataContext = new ViewModels.AzureDevOpsSignIn() };
            return await dialog.ShowDialog<bool>(owner);
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            Start();
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            (DataContext as ViewModels.AzureDevOpsSignIn)?.Cancel();
            base.OnClosing(e);
        }

        private async void Start()
        {
            if (DataContext is not ViewModels.AzureDevOpsSignIn vm)
                return;

            // Copy the code and open the browser right away, so all that is left is pasting it on microsoft.com.
            var succ = await vm.RunAsync(async () =>
            {
                await this.CopyTextAsync(vm.UserCode);
                vm.OpenVerificationPage();
            });

            if (succ)
            {
                Activate();
                Close(true);
            }
        }

        private async void OnCopyCode(object _, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.AzureDevOpsSignIn vm)
                await this.CopyTextAsync(vm.UserCode);

            e.Handled = true;
        }

        private void OnOpenVerificationPage(object _, RoutedEventArgs e)
        {
            (DataContext as ViewModels.AzureDevOpsSignIn)?.OpenVerificationPage();
            e.Handled = true;
        }

        private void OnRetry(object _, RoutedEventArgs e)
        {
            Start();
            e.Handled = true;
        }

        private void OnCancel(object _, RoutedEventArgs e)
        {
            Close(false);
            e.Handled = true;
        }
    }
}
