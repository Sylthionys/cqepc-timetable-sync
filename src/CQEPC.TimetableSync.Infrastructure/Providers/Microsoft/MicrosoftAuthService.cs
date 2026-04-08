using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.Versioning;
using CQEPC.TimetableSync.Application.Abstractions.Sync;
using CQEPC.TimetableSync.Infrastructure.Persistence.Local;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace CQEPC.TimetableSync.Infrastructure.Providers.Microsoft;

[SupportedOSPlatform("windows")]
internal sealed class MicrosoftAuthService : IDisposable
{
    private static readonly string[] Scopes =
    [
        "User.Read",
        "Calendars.ReadWrite",
        "Tasks.ReadWrite",
        "offline_access",
    ];

    private readonly MicrosoftTokenCacheStore tokenCacheStore;
    private readonly string summaryFilePath;

    public MicrosoftAuthService(LocalStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);

        var rootDirectory = Path.Combine(storagePaths.ProviderTokensDirectory, "microsoft");
        tokenCacheStore = new MicrosoftTokenCacheStore(Path.Combine(rootDirectory, "msal-token-cache.bin"));
        summaryFilePath = Path.Combine(rootDirectory, "connection-summary.bin");
    }

    public async Task<ProviderConnectionState> GetConnectionStateAsync(CancellationToken cancellationToken)
    {
        var summary = await LoadSummaryAsync(cancellationToken).ConfigureAwait(false);
        return summary is null
            ? new ProviderConnectionState(false)
            : new ProviderConnectionState(true, summary.ConnectedAccountSummary);
    }

    public async Task<ProviderConnectionState> ConnectAsync(
        ProviderConnectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await AcquireTokenInteractiveAsync(request, cancellationToken).ConfigureAwait(false);
        var summary = result.Account?.Username ?? result.Account?.HomeAccountId?.Identifier ?? "Connected Microsoft account";
        await SaveSummaryAsync(new StoredConnectionSummary(summary), cancellationToken).ConfigureAwait(false);
        return new ProviderConnectionState(true, summary);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await tokenCacheStore.ClearAsync().ConfigureAwait(false);

        if (File.Exists(summaryFilePath))
        {
            File.Delete(summaryFilePath);
        }
    }

    public async Task<string> GetAccessTokenAsync(
        ProviderConnectionContext connectionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connectionContext);
        ValidateClientId(connectionContext.ClientId);

        var application = CreateApplication(connectionContext);
        var accounts = await application.GetAccountsAsync().ConfigureAwait(false);
        var account = accounts.FirstOrDefault();
        if (account is null)
        {
            throw new InvalidOperationException("Microsoft is not connected. Connect the account in Settings first.");
        }

        try
        {
            var result = await application.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException exception)
        {
            throw new InvalidOperationException($"Microsoft sign-in requires attention. Reconnect in Settings. {exception.Message}");
        }
    }

    private async Task<AuthenticationResult> AcquireTokenInteractiveAsync(
        ProviderConnectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AcquireTokenInteractiveCoreAsync(request, request.ConnectionContext.UseBroker, cancellationToken).ConfigureAwait(false);
        }
        catch (MsalException) when (request.ConnectionContext.UseBroker)
        {
            return await AcquireTokenInteractiveCoreAsync(request, useBroker: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<AuthenticationResult> AcquireTokenInteractiveCoreAsync(
        ProviderConnectionRequest request,
        bool useBroker,
        CancellationToken cancellationToken)
    {
        var connectionContext = request.ConnectionContext with { UseBroker = useBroker };
        var application = CreateApplication(connectionContext);
        var builder = application
            .AcquireTokenInteractive(Scopes)
            .WithPrompt(Prompt.SelectAccount)
            .WithUseEmbeddedWebView(false);

        if (request.ParentWindowHandle.HasValue && request.ParentWindowHandle.Value != 0)
        {
            builder = builder.WithParentActivityOrWindow(request.ParentWindowHandle.Value);
        }

        return await builder.ExecuteAsync(cancellationToken).ConfigureAwait(false);
    }

    private IPublicClientApplication CreateApplication(ProviderConnectionContext connectionContext)
    {
        ValidateClientId(connectionContext.ClientId);

        var builder = PublicClientApplicationBuilder.Create(connectionContext.ClientId!)
            .WithDefaultRedirectUri()
            .WithAuthority(AzureCloudInstance.AzurePublic, string.IsNullOrWhiteSpace(connectionContext.TenantId) ? "common" : connectionContext.TenantId.Trim());

        if (connectionContext.UseBroker)
        {
            builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
        }

        var application = builder.Build();
        tokenCacheStore.Register(application.UserTokenCache);
        return application;
    }

    private static void ValidateClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Enter a Microsoft public client application ID before connecting.");
        }
    }

    private async Task SaveSummaryAsync(StoredConnectionSummary summary, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(summaryFilePath)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(summary);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(summaryFilePath, protectedBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoredConnectionSummary?> LoadSummaryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(summaryFilePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(summaryFilePath, cancellationToken).ConfigureAwait(false);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredConnectionSummary>(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record StoredConnectionSummary(string ConnectedAccountSummary);

    public void Dispose()
    {
        tokenCacheStore.Dispose();
    }
}
