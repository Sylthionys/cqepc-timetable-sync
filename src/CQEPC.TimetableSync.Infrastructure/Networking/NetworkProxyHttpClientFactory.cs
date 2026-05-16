using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using Microsoft.Identity.Client;

namespace CQEPC.TimetableSync.Infrastructure.Networking;

internal sealed class NetworkProxyHttpClientFactory : IMsalHttpClientFactory, IDisposable
{
    private readonly Func<NetworkProxySettings> settingsProvider;
    private readonly Func<string?>? passwordProvider;
    private readonly object sync = new();
    private HttpClient? httpClient;
    private string? signature;

    public NetworkProxyHttpClientFactory(
        Func<NetworkProxySettings> settingsProvider,
        Func<string?>? passwordProvider = null)
    {
        this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        this.passwordProvider = passwordProvider;
    }

    public HttpClient GetHttpClient() => GetCurrentClient();

    public HttpClient GetCurrentClient()
    {
        var settings = settingsProvider();
        var currentSignature = BuildSignature(settings, passwordProvider);
        lock (sync)
        {
            if (httpClient is not null && string.Equals(signature, currentSignature, StringComparison.Ordinal))
            {
                return httpClient;
            }

            httpClient?.Dispose();
            httpClient = CreateHttpClient(settings, passwordProvider);
            signature = currentSignature;
            return httpClient;
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            httpClient?.Dispose();
            httpClient = null;
            signature = null;
        }
    }

    public static HttpClient CreateHttpClient(NetworkProxySettings settings, Func<string?>? passwordProvider = null) =>
        new(CreateHandler(settings, passwordProvider), disposeHandler: true);

    public static HttpMessageHandler CreateHandler(NetworkProxySettings settings, Func<string?>? passwordProvider = null)
    {
        switch (settings.Mode)
        {
            case NetworkProxyMode.Direct:
            {
                var handler = new HttpClientHandler();
                handler.UseProxy = false;
                return handler;
            }

            case NetworkProxyMode.Custom:
            {
                var proxy = CreateCustomProxy(settings, passwordProvider);
                var handler = new HttpClientHandler();
                handler.UseProxy = true;
                handler.Proxy = proxy;
                return handler;
            }

            case NetworkProxyMode.System:
            default:
            {
                var handler = new HttpClientHandler();
                handler.UseProxy = true;
                handler.Proxy = HttpClient.DefaultProxy;
                return handler;
            }
        }
    }

    public static Google.Apis.Http.IHttpClientFactory? CreateGoogleHttpClientFactory(
        NetworkProxySettings settings,
        Func<string?>? passwordProvider = null) =>
        settings.Mode switch
        {
            NetworkProxyMode.System => Google.Apis.Http.HttpClientFactory.ForProxy(HttpClient.DefaultProxy),
            NetworkProxyMode.Direct => Google.Apis.Http.HttpClientFactory.ForProxy(new DirectWebProxy()),
            NetworkProxyMode.Custom => Google.Apis.Http.HttpClientFactory.ForProxy(CreateCustomProxy(settings, passwordProvider)),
            _ => null,
        };

    public static NetworkProxyConnectionTestResult Validate(NetworkProxySettings settings)
    {
        if (settings.Mode is NetworkProxyMode.System or NetworkProxyMode.Direct)
        {
            return new NetworkProxyConnectionTestResult(NetworkProxyConnectionTestStatus.Success, "Proxy settings are valid.");
        }

        if (settings.Mode != NetworkProxyMode.Custom)
        {
            return new NetworkProxyConnectionTestResult(NetworkProxyConnectionTestStatus.ConfigurationError, "Unknown proxy mode.");
        }

        var normalized = NormalizeProxyUri(settings.CustomProxyUri);
        if (normalized is null)
        {
            return new NetworkProxyConnectionTestResult(
                NetworkProxyConnectionTestStatus.ConfigurationError,
                "Enter a custom proxy URI in the form http://host:port.");
        }

        if (!normalized.HasExplicitPort)
        {
            return new NetworkProxyConnectionTestResult(
                NetworkProxyConnectionTestStatus.ConfigurationError,
                "Custom proxy URI must include an explicit port.");
        }

        if (!string.IsNullOrWhiteSpace(normalized.Uri.UserInfo))
        {
            return new NetworkProxyConnectionTestResult(
                NetworkProxyConnectionTestStatus.ConfigurationError,
                "Enter proxy credentials in the username and password fields, not in the proxy URI.");
        }

        return new NetworkProxyConnectionTestResult(NetworkProxyConnectionTestStatus.Success, "Proxy settings are valid.");
    }

    public static string BuildSignature(NetworkProxySettings settings, Func<string?>? passwordProvider = null) =>
        string.Join(
            "|",
            settings.Mode,
            NormalizeProxyUri(settings.CustomProxyUri)?.Uri.AbsoluteUri ?? string.Empty,
            settings.CustomProxyUsername ?? string.Empty,
            settings.CustomProxyHasPassword ? "password" : string.Empty,
            settings.CustomProxyHasPassword ? HashPassword(passwordProvider?.Invoke()) : string.Empty,
            settings.BypassLocal ? "bypass-local" : string.Empty,
            string.Join(",", settings.BypassList.Select(static value => value.ToUpperInvariant())));

    private static ConfiguredWebProxy CreateCustomProxy(NetworkProxySettings settings, Func<string?>? passwordProvider)
    {
        var validation = Validate(settings);
        if (validation.Status != NetworkProxyConnectionTestStatus.Success)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var proxyUri = NormalizeProxyUri(settings.CustomProxyUri)!.Uri;
        ICredentials? credentials = null;
        if (!string.IsNullOrWhiteSpace(settings.CustomProxyUsername))
        {
            credentials = new NetworkCredential(settings.CustomProxyUsername, passwordProvider?.Invoke() ?? string.Empty);
        }

        return new ConfiguredWebProxy(proxyUri, settings.BypassLocal, settings.BypassList, credentials);
    }

    private static string HashPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    private static NormalizedProxyUri? NormalizeProxyUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"http://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.Port is < 1 or > 65535)
        {
            return null;
        }

        var authorityStart = candidate.IndexOf("://", StringComparison.Ordinal) + 3;
        var pathStart = candidate.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = pathStart < 0 ? candidate[authorityStart..] : candidate[authorityStart..pathStart];
        var hostPort = authority.Contains('@')
            ? authority[(authority.LastIndexOf('@') + 1)..]
            : authority;
        var hasExplicitPort = hostPort.StartsWith('[')
            ? hostPort.Contains("]:", StringComparison.Ordinal)
            : hostPort.Contains(':');

        return new NormalizedProxyUri(uri, hasExplicitPort);
    }

    private sealed record NormalizedProxyUri(Uri Uri, bool HasExplicitPort);

    private sealed class ConfiguredWebProxy : IWebProxy
    {
        private readonly Uri proxyUri;
        private readonly bool bypassLocal;
        private readonly IReadOnlyList<string> bypassList;

        public ConfiguredWebProxy(
            Uri proxyUri,
            bool bypassLocal,
            IReadOnlyList<string> bypassList,
            ICredentials? credentials)
        {
            this.proxyUri = proxyUri;
            this.bypassLocal = bypassLocal;
            this.bypassList = bypassList;
            Credentials = credentials;
        }

        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) =>
            IsBypassed(destination) ? destination : proxyUri;

        public bool IsBypassed(Uri host)
        {
            if (bypassLocal && host.IsLoopback)
            {
                return true;
            }

            var destinationHost = host.Host.Trim('[', ']');
            if (bypassLocal && !destinationHost.Contains('.', StringComparison.Ordinal) && !destinationHost.Contains(':', StringComparison.Ordinal))
            {
                return true;
            }

            return bypassList.Any(pattern => IsBypassMatch(destinationHost, pattern));
        }

        private static bool IsBypassMatch(string destinationHost, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            var normalizedPattern = pattern.Trim();
            if (Uri.TryCreate(normalizedPattern, UriKind.Absolute, out var uri))
            {
                normalizedPattern = uri.Host;
            }

            normalizedPattern = normalizedPattern.Trim('[', ']');
            if (normalizedPattern.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = normalizedPattern[1..];
                return destinationHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(destinationHost, normalizedPattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class DirectWebProxy : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => destination;

        public bool IsBypassed(Uri host) => true;
    }
}

public sealed class NetworkProxyConnectionTester : INetworkProxyConnectionTester
{
    private static readonly Uri GoogleCalendarDiscoveryUri = new("https://www.googleapis.com/discovery/v1/apis/calendar/v3/rest");

    public async Task<NetworkProxyConnectionTestResult> TestGoogleApiAsync(
        NetworkProxySettings settings,
        string? customProxyPassword,
        CancellationToken cancellationToken)
    {
        var validation = NetworkProxyHttpClientFactory.Validate(settings);
        if (validation.Status != NetworkProxyConnectionTestStatus.Success)
        {
            return validation;
        }

        using var client = NetworkProxyHttpClientFactory.CreateHttpClient(settings, () => customProxyPassword);
        client.Timeout = TimeSpan.FromSeconds(10);

        try
        {
            using var response = await client.GetAsync(GoogleCalendarDiscoveryUri, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new NetworkProxyConnectionTestResult(
                    NetworkProxyConnectionTestStatus.Success,
                    "Connected to Google APIs with the current proxy settings.");
            }

            if (response.StatusCode is HttpStatusCode.ProxyAuthenticationRequired or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new NetworkProxyConnectionTestResult(
                    NetworkProxyConnectionTestStatus.AuthenticationOrPermissionFailed,
                    $"Authentication or permission failed ({(int)response.StatusCode}).");
            }

            return new NetworkProxyConnectionTestResult(
                NetworkProxyConnectionTestStatus.GoogleApiUnreachable,
                $"Google APIs returned {(int)response.StatusCode}.");
        }
        catch (HttpRequestException exception) when (settings.Mode == NetworkProxyMode.Custom && IsProxyReachabilityFailure(exception))
        {
            return new NetworkProxyConnectionTestResult(
                NetworkProxyConnectionTestStatus.ProxyUnreachable,
                "The custom proxy could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NetworkProxyConnectionTestResult(
                settings.Mode == NetworkProxyMode.Custom
                    ? NetworkProxyConnectionTestStatus.ProxyUnreachable
                    : NetworkProxyConnectionTestStatus.GoogleApiUnreachable,
                "The connection test timed out.");
        }
        catch (HttpRequestException)
        {
            return new NetworkProxyConnectionTestResult(
                NetworkProxyConnectionTestStatus.GoogleApiUnreachable,
                "Google APIs could not be reached.");
        }
    }

    private static bool IsProxyReachabilityFailure(Exception exception) =>
        exception is SocketException
        || exception.InnerException is not null && IsProxyReachabilityFailure(exception.InnerException);
}
