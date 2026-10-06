using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public class AzureDevOpsProject
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("visibility")]
        public string Visibility { get; set; } = string.Empty;
    }

    public class AzureDevOpsRepository
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("project")]
        public AzureDevOpsProject Project { get; set; } = new();

        [JsonPropertyName("remoteUrl")]
        public string RemoteUrl { get; set; } = string.Empty;

        [JsonPropertyName("sshUrl")]
        public string SshUrl { get; set; } = string.Empty;

        [JsonPropertyName("isDisabled")]
        public bool IsDisabled { get; set; } = false;

        [JsonIgnore]
        public string Organization { get; set; } = string.Empty;

        /// <summary>
        ///     `organization/project/name`, the same key `AzureDevOps.ParseFullName` returns for a remote URL.
        /// </summary>
        [JsonIgnore]
        public string FullName => $"{Organization}/{Project.Name}/{Name}";

        [JsonIgnore]
        public bool IsPrivate => !Project.Visibility.Equals("public", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool IsCloned { get; set; } = false;
    }

    public class AzureDevOpsRepositoryList
    {
        [JsonPropertyName("value")]
        public List<AzureDevOpsRepository> Value { get; set; } = [];
    }

    public class AzureDevOpsProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("emailAddress")]
        public string EmailAddress { get; set; } = string.Empty;
    }

    public class AzureDevOpsOrganization
    {
        [JsonPropertyName("accountName")]
        public string AccountName { get; set; } = string.Empty;
    }

    public class AzureDevOpsOrganizationList
    {
        [JsonPropertyName("value")]
        public List<AzureDevOpsOrganization> Value { get; set; } = [];
    }

    public class AzureDevOpsDeviceCode
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 900;

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 5;
    }

    public class AzureDevOpsTokens
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 0;

        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string ErrorDescription { get; set; } = string.Empty;
    }

    public class AzureDevOpsException : Exception
    {
        public bool IsUnauthorized { get; }

        public AzureDevOpsException(string message, bool isUnauthorized = false) : base(message)
        {
            IsUnauthorized = isUnauthorized;
        }
    }

    /// <summary>
    ///     Minimal Azure DevOps client: Microsoft Entra ID device code sign-in, and listing the repositories of every
    ///     organization the signed-in user is a member of.
    /// </summary>
    public static partial class AzureDevOps
    {
        /// <summary>
        ///     Application (client) id of the Holion app registration in Microsoft Entra ID: a multitenant public client
        ///     with the delegated Azure DevOps `user_impersonation` permission. It is public by design; the device code
        ///     flow needs no client secret.
        /// </summary>
        public const string ClientId = "a87d6f69-c057-4782-8ce7-d6b5d64b021b";

        public const string TokenUserName = "AzureDevOps";
        public const string OrganizationsUrl = "https://aex.dev.azure.com/me";

        public static bool IsConfigured => !string.IsNullOrEmpty(ClientId);

        /// <summary>
        ///     Whether git talks to Azure DevOps when using this host: `dev.azure.com` or `<organization>.visualstudio.com`.
        /// </summary>
        public static bool IsHost(string host)
        {
            return host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<AzureDevOpsDeviceCode> RequestDeviceCodeAsync(CancellationToken token)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["client_id"] = ClientId,
                ["scope"] = Scopes,
            });

            using var rsp = await s_client.PostAsync($"{Authority}/devicecode", form, token).ConfigureAwait(false);
            await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

            await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.AzureDevOpsDeviceCode, token).ConfigureAwait(false);
        }

        /// <summary>
        ///     Polls until the user has entered the code at microsoft.com/devicelogin. Returns the tokens, or throws when
        ///     the code expires or the user declines.
        /// </summary>
        public static async Task<AzureDevOpsTokens> WaitForTokensAsync(AzureDevOpsDeviceCode code, CancellationToken token)
        {
            var interval = Math.Max(code.Interval, 1);
            var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), token).ConfigureAwait(false);

                var result = await RequestTokensAsync(new Dictionary<string, string>()
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = code.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }, token).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(result.AccessToken))
                    return result;

                switch (result.Error)
                {
                    case "authorization_pending":
                        break;
                    case "slow_down":
                        interval += 5;
                        break;
                    case "expired_token":
                        throw new AzureDevOpsException(App.Text("AzureDevOps.SignIn.Expired"));
                    case "authorization_declined":
                    case "access_denied":
                        throw new AzureDevOpsException(App.Text("AzureDevOps.SignIn.Denied"));
                    default:
                        throw new AzureDevOpsException(string.IsNullOrEmpty(result.ErrorDescription) ? result.Error : result.ErrorDescription);
                }
            }

            throw new AzureDevOpsException(App.Text("AzureDevOps.SignIn.Expired"));
        }

        /// <summary>
        ///     Trades a refresh token for a new access token (and usually a new refresh token). Throws an unauthorized
        ///     exception when the refresh token is no longer valid, so the user has to sign in again.
        /// </summary>
        public static async Task<AzureDevOpsTokens> RefreshTokensAsync(string refreshToken, CancellationToken token)
        {
            var result = await RequestTokensAsync(new Dictionary<string, string>()
            {
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = Scopes,
            }, token).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(result.AccessToken))
                return result;

            var message = string.IsNullOrEmpty(result.ErrorDescription) ? result.Error : result.ErrorDescription;
            throw new AzureDevOpsException($"Azure DevOps: {message}", result.Error is "invalid_grant" or "interaction_required");
        }

        public static async Task<AzureDevOpsProfile> GetProfileAsync(string accessToken, CancellationToken token)
        {
            using var req = CreateApiRequest("https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1-preview.3", accessToken);
            using var rsp = await s_client.SendAsync(req, token).ConfigureAwait(false);
            await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

            await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.AzureDevOpsProfile, token).ConfigureAwait(false);
        }

        /// <summary>
        ///     Names of the organizations the user is a member of.
        /// </summary>
        public static async Task<List<string>> GetOrganizationsAsync(string accessToken, string memberId, CancellationToken token)
        {
            var url = $"https://app.vssps.visualstudio.com/_apis/accounts?memberId={Uri.EscapeDataString(memberId)}&api-version=7.1-preview.1";
            using var req = CreateApiRequest(url, accessToken);
            using var rsp = await s_client.SendAsync(req, token).ConfigureAwait(false);
            await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

            await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var list = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.AzureDevOpsOrganizationList, token).ConfigureAwait(false);

            var outs = new List<string>();
            foreach (var account in list?.Value ?? [])
            {
                if (!string.IsNullOrEmpty(account.AccountName))
                    outs.Add(account.AccountName);
            }

            return outs;
        }

        /// <summary>
        ///     Lists the repositories of every project in the organization that the user can access.
        /// </summary>
        public static async Task<List<AzureDevOpsRepository>> GetRepositoriesAsync(string accessToken, string organization, CancellationToken token)
        {
            var url = $"https://dev.azure.com/{Uri.EscapeDataString(organization)}/_apis/git/repositories?api-version=7.1";
            using var req = CreateApiRequest(url, accessToken);
            using var rsp = await s_client.SendAsync(req, token).ConfigureAwait(false);
            await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

            await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var list = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.AzureDevOpsRepositoryList, token).ConfigureAwait(false);

            var repos = list?.Value ?? [];
            foreach (var repo in repos)
                repo.Organization = organization;

            return repos;
        }

        /// <summary>
        ///     Returns `organization/project/name` (lower case) when the given remote URL points at a repository on
        ///     Azure DevOps, over HTTPS (`dev.azure.com` or `<organization>.visualstudio.com`) or SSH.
        /// </summary>
        public static string ParseFullName(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            url = url.Trim();
            foreach (var regex in new[] { REG_DEV_AZURE_URL(), REG_VISUALSTUDIO_URL(), REG_SSH_URL() })
            {
                var match = regex.Match(url);
                if (!match.Success)
                    continue;

                // A URL without a project points at the repository named after its project.
                var repo = match.Groups["repo"].Value;
                var project = match.Groups["project"].Success ? match.Groups["project"].Value : repo;
                return Uri.UnescapeDataString($"{match.Groups["org"].Value}/{project}/{repo}").ToLowerInvariant();
            }

            return null;
        }

        /// <summary>
        ///     Collects the Azure DevOps repositories (`organization/project/name`, lower case) that the given local
        ///     repositories use as remotes.
        /// </summary>
        public static HashSet<string> CollectClonedFullNames(IEnumerable<string> repoPaths)
        {
            var outs = new HashSet<string>();
            foreach (var path in repoPaths)
            {
                var config = Path.Combine(path, ".git", "config");
                if (!File.Exists(config))
                    config = Path.Combine(path, "config");

                try
                {
                    if (!File.Exists(config))
                        continue;

                    foreach (var line in File.ReadLines(config))
                    {
                        var trimmed = line.Trim();
                        if (!trimmed.StartsWith("url", StringComparison.Ordinal))
                            continue;

                        var idx = trimmed.IndexOf('=');
                        if (idx < 0)
                            continue;

                        var fullName = ParseFullName(trimmed.Substring(idx + 1));
                        if (fullName != null)
                            outs.Add(fullName);
                    }
                }
                catch
                {
                    // Ignore unreadable repositories.
                }
            }

            return outs;
        }

        /// <summary>
        ///     The token endpoint answers pending and failed requests with 400 and an error in the body, so the body
        ///     is read whatever the status.
        /// </summary>
        private static async Task<AzureDevOpsTokens> RequestTokensAsync(Dictionary<string, string> fields, CancellationToken token)
        {
            using var form = new FormUrlEncodedContent(fields);
            using var rsp = await s_client.PostAsync($"{Authority}/token", form, token).ConfigureAwait(false);
            var body = await rsp.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize(body, JsonCodeGen.Default.AzureDevOpsTokens);
            }
            catch (System.Text.Json.JsonException)
            {
                throw new AzureDevOpsException($"Azure DevOps: {(int)rsp.StatusCode} {rsp.ReasonPhrase} {body}".Trim());
            }
        }

        private static HttpRequestMessage CreateApiRequest(string url, string accessToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // Without it, failed authentication redirects to a sign-in page instead of answering 401.
            req.Headers.Add("X-TFS-FedAuthRedirect", "Suppress");
            return req;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage rsp, CancellationToken token)
        {
            // Azure DevOps answers 203 with an HTML sign-in page when it does not accept the token.
            if (rsp.IsSuccessStatusCode && rsp.StatusCode != HttpStatusCode.NonAuthoritativeInformation)
                return;

            var body = await rsp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (body.Length > 500)
                body = body.Substring(0, 500);

            var unauthorized = rsp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation;
            throw new AzureDevOpsException($"Azure DevOps: {(int)rsp.StatusCode} {rsp.ReasonPhrase} {body}".Trim(), unauthorized);
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient(new HttpClientHandler() { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SourceGit");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        [GeneratedRegex(@"^https?://(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/(?:(?<project>[^/]+)/)?_git/(?<repo>[^/?#]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
        private static partial Regex REG_DEV_AZURE_URL();

        [GeneratedRegex(@"^https?://(?:[^@/]+@)?(?<org>[\w-]+)\.visualstudio\.com/(?:DefaultCollection/)?(?:(?<project>[^/]+)/)?_git/(?<repo>[^/?#]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
        private static partial Regex REG_VISUALSTUDIO_URL();

        [GeneratedRegex(@"^(?:ssh://)?(?:[^@/]+@)?(?:ssh\.dev\.azure\.com|vs-ssh\.visualstudio\.com)(?::22)?[:/]v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
        private static partial Regex REG_SSH_URL();

        private const string Authority = "https://login.microsoftonline.com/organizations/oauth2/v2.0";

        // The resource id of Azure DevOps, plus a refresh token so git keeps working without signing in again.
        private const string Scopes = "499b84ac-1321-427f-aa17-267ca6975798/.default offline_access";

        private static readonly HttpClient s_client = CreateClient();
    }
}
