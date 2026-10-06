using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public class GitHubOwner
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;
    }

    public class GitHubRepository
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("owner")]
        public GitHubOwner Owner { get; set; } = new();

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("private")]
        public bool IsPrivate { get; set; } = false;

        [JsonPropertyName("archived")]
        public bool IsArchived { get; set; } = false;

        [JsonPropertyName("fork")]
        public bool IsFork { get; set; } = false;

        [JsonPropertyName("clone_url")]
        public string CloneUrl { get; set; } = string.Empty;

        [JsonPropertyName("ssh_url")]
        public string SshUrl { get; set; } = string.Empty;

        [JsonPropertyName("pushed_at")]
        public string PushedAt { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsCloned { get; set; } = false;
    }

    public class GitHubUser
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;
    }

    public class GitHubDeviceCode
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

    public class GitHubAccessToken
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string ErrorDescription { get; set; } = string.Empty;

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 0;
    }

    public class GitHubException : Exception
    {
        public bool IsUnauthorized { get; }

        public GitHubException(string message, bool isUnauthorized = false) : base(message)
        {
            IsUnauthorized = isUnauthorized;
        }
    }

    /// <summary>
    ///     Minimal GitHub client: OAuth device flow sign-in and listing the repositories the signed-in user can access.
    /// </summary>
    public static partial class GitHub
    {
        /// <summary>
        ///     Client id of the Holion OAuth app (github.com/organizations/holion/settings/applications) with device flow
        ///     enabled. It is public by design; the device flow needs no client secret.
        /// </summary>
        public const string ClientId = "Ov23liyFBsWP7an8gjv0";

        public const string Host = "github.com";
        public const string Scopes = "repo read:org workflow";
        public const string TokenUserName = "x-access-token";

        public static bool IsConfigured => !string.IsNullOrEmpty(ClientId);

        public static string ApplicationSettingsUrl => $"https://github.com/settings/connections/applications/{ClientId}";

        public static async Task<GitHubDeviceCode> RequestDeviceCodeAsync(CancellationToken token)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>()
            {
                ["client_id"] = ClientId,
                ["scope"] = Scopes,
            });

            using var rsp = await s_client.PostAsync("https://github.com/login/device/code", form, token).ConfigureAwait(false);
            await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

            await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.GitHubDeviceCode, token).ConfigureAwait(false);
        }

        /// <summary>
        ///     Polls until the user has entered the code on github.com. Returns the access token, or throws when the
        ///     code expires or the user denies access.
        /// </summary>
        public static async Task<string> WaitForAccessTokenAsync(GitHubDeviceCode code, CancellationToken token)
        {
            var interval = Math.Max(code.Interval, 1);
            var deadline = DateTime.UtcNow.AddSeconds(code.ExpiresIn);

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), token).ConfigureAwait(false);

                var form = new FormUrlEncodedContent(new Dictionary<string, string>()
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = code.DeviceCode,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                });

                using var rsp = await s_client.PostAsync("https://github.com/login/oauth/access_token", form, token).ConfigureAwait(false);
                await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

                await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var result = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.GitHubAccessToken, token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(result.AccessToken))
                    return result.AccessToken;

                switch (result.Error)
                {
                    case "authorization_pending":
                        break;
                    case "slow_down":
                        interval = result.Interval > 0 ? result.Interval : interval + 5;
                        break;
                    case "expired_token":
                        throw new GitHubException(App.Text("GitHub.SignIn.Expired"));
                    case "access_denied":
                        throw new GitHubException(App.Text("GitHub.SignIn.Denied"));
                    default:
                        throw new GitHubException(string.IsNullOrEmpty(result.ErrorDescription) ? result.Error : result.ErrorDescription);
                }
            }

            throw new GitHubException(App.Text("GitHub.SignIn.Expired"));
        }

        public static async Task<GitHubUser> GetUserAsync(string accessToken, CancellationToken token)
        {
            using var req = CreateApiRequest("https://api.github.com/user", accessToken);
            using var rsp = await s_client.SendAsync(req, token).ConfigureAwait(false);
            await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

            await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.GitHubUser, token).ConfigureAwait(false);
        }

        /// <summary>
        ///     Lists every repository the user owns, collaborates on or can access through an organization.
        /// </summary>
        public static async Task<List<GitHubRepository>> GetRepositoriesAsync(string accessToken, CancellationToken token)
        {
            var repos = new List<GitHubRepository>();
            var url = "https://api.github.com/user/repos?per_page=100&affiliation=owner,collaborator,organization_member";

            while (!string.IsNullOrEmpty(url))
            {
                using var req = CreateApiRequest(url, accessToken);
                using var rsp = await s_client.SendAsync(req, token).ConfigureAwait(false);
                await EnsureSuccessAsync(rsp, token).ConfigureAwait(false);

                await using var stream = await rsp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var page = await System.Text.Json.JsonSerializer.DeserializeAsync(stream, JsonCodeGen.Default.ListGitHubRepository, token).ConfigureAwait(false);
                if (page != null)
                    repos.AddRange(page);

                url = GetNextPageUrl(rsp);
            }

            return repos;
        }

        /// <summary>
        ///     Returns `owner/name` (lower case) when the given remote URL points at a repository on github.com.
        /// </summary>
        public static string ParseFullName(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            var match = REG_GITHUB_URL().Match(url.Trim());
            if (!match.Success)
                return null;

            return $"{match.Groups[1].Value}/{match.Groups[2].Value}".ToLowerInvariant();
        }

        /// <summary>
        ///     Collects the github.com repositories (`owner/name`, lower case) that the given local repositories
        ///     use as remotes. Reads the config files directly so it is cheap enough for hundreds of repositories.
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

        private static HttpRequestMessage CreateApiRequest(string url, string accessToken)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            return req;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage rsp, CancellationToken token)
        {
            if (rsp.IsSuccessStatusCode)
                return;

            var body = await rsp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var unauthorized = rsp.StatusCode == System.Net.HttpStatusCode.Unauthorized;
            throw new GitHubException($"GitHub: {(int)rsp.StatusCode} {rsp.ReasonPhrase} {body}".Trim(), unauthorized);
        }

        private static string GetNextPageUrl(HttpResponseMessage rsp)
        {
            if (!rsp.Headers.TryGetValues("Link", out var values))
                return null;

            foreach (var value in values)
            {
                var match = REG_NEXT_LINK().Match(value);
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SourceGit");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        [GeneratedRegex(@"^(?:https?://(?:[^@/]+@)?|ssh://(?:[^@/]+@)?|git@)github\.com[:/]([\w.-]+)/([\w.-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
        private static partial Regex REG_GITHUB_URL();

        [GeneratedRegex(@"<([^>]+)>;\s*rel=""next""")]
        private static partial Regex REG_NEXT_LINK();

        private static readonly HttpClient s_client = CreateClient();
    }
}
