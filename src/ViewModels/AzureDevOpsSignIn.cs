using System;
using System.Threading;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    /// <summary>
    ///     OAuth device flow: shows a one-time code, which the user enters on microsoft.com/devicelogin.
    /// </summary>
    public class AzureDevOpsSignIn : ObservableObject
    {
        public string UserCode
        {
            get => _userCode;
            private set => SetProperty(ref _userCode, value);
        }

        public string VerificationUri
        {
            get => _verificationUri;
            private set => SetProperty(ref _verificationUri, value);
        }

        public string Error
        {
            get => _error;
            private set => SetProperty(ref _error, value);
        }

        public bool IsWaiting
        {
            get => _isWaiting;
            private set => SetProperty(ref _isWaiting, value);
        }

        /// <summary>
        ///     Runs the whole flow. Returns true once the account is signed in; `onCodeReady` runs as soon as the
        ///     code to enter on microsoft.com is known.
        /// </summary>
        public async Task<bool> RunAsync(Action onCodeReady)
        {
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;

            Error = null;
            IsWaiting = true;

            try
            {
                var code = await Models.AzureDevOps.RequestDeviceCodeAsync(token);
                UserCode = code.UserCode;
                VerificationUri = code.VerificationUri;
                onCodeReady?.Invoke();

                var tokens = await Models.AzureDevOps.WaitForTokensAsync(code, token);
                var profile = await Models.AzureDevOps.GetProfileAsync(tokens.AccessToken, token);
                var login = string.IsNullOrEmpty(profile.EmailAddress) ? profile.DisplayName : profile.EmailAddress;
                if (!AzureDevOpsAccount.Instance.CompleteSignIn(login, tokens))
                    throw new Models.AzureDevOpsException(App.Text("AzureDevOps.SignIn.StoreFailed"));

                return true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return false;
            }
            finally
            {
                IsWaiting = false;
            }
        }

        public void OpenVerificationPage()
        {
            if (!string.IsNullOrEmpty(_verificationUri))
                Native.OS.OpenBrowser(_verificationUri);
        }

        public void Cancel()
        {
            _cancellation?.Cancel();
        }

        private CancellationTokenSource _cancellation = null;
        private string _userCode = string.Empty;
        private string _verificationUri = string.Empty;
        private string _error = null;
        private bool _isWaiting = false;
    }
}
