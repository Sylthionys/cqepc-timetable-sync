using System.Net;
using CQEPC.TimetableSync.Application.UseCases.Workspace;
using CQEPC.TimetableSync.Infrastructure.Networking;
using FluentAssertions;
using Xunit;

namespace CQEPC.TimetableSync.Infrastructure.Tests;

public sealed class NetworkProxyHttpClientFactoryTests
{
    [Fact]
    public void CreateHandlerUsesDefaultProxyForSystemMode()
    {
        using var handler = (HttpClientHandler)NetworkProxyHttpClientFactory.CreateHandler(
            new NetworkProxySettings(NetworkProxyMode.System, customProxyUri: null));

        handler.UseProxy.Should().BeTrue();
        handler.Proxy.Should().BeSameAs(HttpClient.DefaultProxy);
    }

    [Fact]
    public void CreateHandlerDisablesProxyForDirectMode()
    {
        using var handler = (HttpClientHandler)NetworkProxyHttpClientFactory.CreateHandler(
            new NetworkProxySettings(NetworkProxyMode.Direct, customProxyUri: null));

        handler.UseProxy.Should().BeFalse();
    }

    [Fact]
    public void CreateHandlerBuildsCustomProxyWithCredentials()
    {
        using var handler = (HttpClientHandler)NetworkProxyHttpClientFactory.CreateHandler(
            new NetworkProxySettings(
                NetworkProxyMode.Custom,
                "http://proxy.example.test:8080",
                customProxyUsername: "student",
                customProxyHasPassword: true),
            () => "secret");

        handler.UseProxy.Should().BeTrue();
        var proxy = handler.Proxy;
        proxy.Should().NotBeNull();
        proxy.GetProxy(new Uri("https://www.googleapis.com/calendar/v3/users/me/calendarList"))
            .Should().Be(new Uri("http://proxy.example.test:8080/"));
        proxy.Credentials.Should().NotBeNull();
        proxy.Credentials!.GetCredential(new Uri("http://proxy.example.test:8080"), "Basic")!.UserName
            .Should().Be("student");
        proxy.Credentials!.GetCredential(new Uri("http://proxy.example.test:8080"), "Basic")!.Password
            .Should().Be("secret");
    }

    [Theory]
    [InlineData("ftp://proxy.example.test:8080")]
    [InlineData("http://:8080")]
    [InlineData("http://user:password@proxy.example.test:8080")]
    public void ValidateRejectsIllegalCustomProxyUri(string value)
    {
        var result = NetworkProxyHttpClientFactory.Validate(new NetworkProxySettings(NetworkProxyMode.Custom, value));

        result.Status.Should().Be(NetworkProxyConnectionTestStatus.ConfigurationError);
    }

    [Theory]
    [InlineData("http://proxy.example.test")]
    [InlineData("http://proxy.example.test:99999")]
    public void ValidateRejectsIllegalOrMissingPort(string value)
    {
        var result = NetworkProxyHttpClientFactory.Validate(new NetworkProxySettings(NetworkProxyMode.Custom, value));

        result.Status.Should().Be(NetworkProxyConnectionTestStatus.ConfigurationError);
    }

    [Fact]
    public void CreateHandlerRejectsInvalidCustomProxyInsteadOfUsingDefaultNetworking()
    {
        var act = () => NetworkProxyHttpClientFactory.CreateHandler(
            new NetworkProxySettings(NetworkProxyMode.Custom, "ftp://proxy.example.test:8080"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*http://host:port*");
    }

    [Theory]
    [InlineData("http://localhost/oauth2callback")]
    [InlineData("http://127.0.0.1:5000/oauth2callback")]
    [InlineData("http://[::1]:5000/oauth2callback")]
    public void CustomProxyBypassesLocalLoopbackDestinations(string destination)
    {
        using var handler = (HttpClientHandler)NetworkProxyHttpClientFactory.CreateHandler(
            new NetworkProxySettings(NetworkProxyMode.Custom, "http://proxy.example.test:8080"));

        handler.Proxy.Should().NotBeNull();
        handler.Proxy!.IsBypassed(new Uri(destination)).Should().BeTrue();
    }

    [Fact]
    public void CustomProxyHonorsBypassList()
    {
        using var handler = (HttpClientHandler)NetworkProxyHttpClientFactory.CreateHandler(
            new NetworkProxySettings(
                NetworkProxyMode.Custom,
                "http://proxy.example.test:8080",
                bypassList: ["*.internal.example", "metadata.google.internal"]));

        handler.Proxy.Should().NotBeNull();
        handler.Proxy!.IsBypassed(new Uri("https://calendar.internal.example")).Should().BeTrue();
        handler.Proxy!.IsBypassed(new Uri("https://metadata.google.internal")).Should().BeTrue();
        handler.Proxy!.IsBypassed(new Uri("https://www.googleapis.com")).Should().BeFalse();
    }

    [Fact]
    public void GetCurrentClientRebuildsWhenProxySignatureChanges()
    {
        var settings = new NetworkProxySettings(NetworkProxyMode.System, customProxyUri: null);
        using var factory = new NetworkProxyHttpClientFactory(() => settings);

        var first = factory.GetCurrentClient();
        settings = new NetworkProxySettings(NetworkProxyMode.Direct, customProxyUri: null);
        var second = factory.GetCurrentClient();

        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void GetCurrentClientRebuildsWhenCustomProxyPasswordChanges()
    {
        var settings = new NetworkProxySettings(
            NetworkProxyMode.Custom,
            "http://proxy.example.test:8080",
            customProxyUsername: "student",
            customProxyHasPassword: true);
        var password = "old-secret";
        using var factory = new NetworkProxyHttpClientFactory(() => settings, () => password);

        var first = factory.GetCurrentClient();
        password = "new-secret";
        var second = factory.GetCurrentClient();

        second.Should().NotBeSameAs(first);
    }
}
