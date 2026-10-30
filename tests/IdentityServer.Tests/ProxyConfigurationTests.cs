using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using static IdentityServer.Tests.IdentityProviderTests;

namespace IdentityServer.Tests;

public sealed class ProxyConfigurationTests
{
    [Fact]
    public void NoProxyConfigurationLeavesExistingIisAndHostOptionsAlone()
    {
        var settings = ProxyHostingOptions.Read(Configuration());
        Assert.False(settings.UseForwardedHeaders);
        Assert.False(settings.PathBase.HasValue);
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto, ForwardLimit = 2 };
        settings.Configure(options);
        Assert.Equal(ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(2, options.ForwardLimit);
    }

    [Theory]
    [InlineData("IdentityServer:PathBase", "IdentityServer")]
    [InlineData("IdentityServer:PathBase", "//")]
    [InlineData("IdentityServer:PathBase", "/IdentityServer//")]
    [InlineData("IdentityServer:PathBase", "/a/../b")]
    [InlineData("IdentityServer:PathBase", "/a%2fb")]
    [InlineData("IdentityServer:PathBase", "/a?b")]
    [InlineData("IdentityServer:PathBaseMode", "Auto")]
    [InlineData("IdentityServer:PathBaseMode", "1")]
    [InlineData("IdentityServer:PathBaseMode", "Strip")]
    [InlineData("IdentityServer:ReverseProxy:KnownProxies:0", "*")]
    [InlineData("IdentityServer:ReverseProxy:KnownProxies:0", "127.0.0.0/8")]
    [InlineData("IdentityServer:ReverseProxy:KnownProxies:0", "localhost")]
    [InlineData("IdentityServer:ReverseProxy:KnownProxies:0", "0.0.0.0")]
    public void InvalidHostingConfigurationFailsAtStartup(string key, string value) =>
        Assert.Throws<InvalidOperationException>(() => ProxyHostingOptions.Read(Configuration((key, value))));

    [Fact]
    public void ExplicitIssuerMustMatchConfiguredPrefix()
    {
        Assert.Throws<InvalidOperationException>(() => ProxyHostingOptions.Read(Configuration(
            ("IdentityServer:PathBase", "/IdentityServer"), ("IdentityServer:ServerUri", "https://identity.example/other"))));
    }

    [Fact]
    public void ExplicitProxyTrustUsesOneHopAndNeverAcceptsForwardedPrefix()
    {
        var settings = ProxyHostingOptions.Read(Configuration(
            ("IdentityServer:UseForwardedHeaders", "true"), ("IdentityServer:ReverseProxy:KnownProxies:0", "127.0.0.1"),
            ("IdentityServer:ServerUri", "https://identity.example/IdentityServer")));
        var forwarded = new ForwardedHeadersOptions();
        settings.Configure(forwarded);
        Assert.Equal(1, forwarded.ForwardLimit);
        Assert.True(forwarded.RequireHeaderSymmetry);
        Assert.Equal(["identity.example"], forwarded.AllowedHosts);
        Assert.Single(forwarded.KnownProxies);
        Assert.Empty(forwarded.KnownIPNetworks);
        Assert.False(forwarded.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedPrefix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("01234567890123456789012345678901 ")]
    public void ProxyAuthenticationRequiresASeparateSharedSecret(string? secret)
    {
        var configuration = Configuration(("IdentityServer:UseForwardedHeaders", "true"),
            ("IdentityServer:ReverseProxy:KnownProxies:0", "127.0.0.1"), ("IdentityServer:ServerUri", "https://identity.example/"),
            ("IdentityServer:Authentication:ProxySecret", secret));
        Assert.Throws<InvalidOperationException>(() => new ProxyKerberosOptions(configuration, ProxyHostingOptions.Read(configuration)));
    }

    [Fact]
    public void ProxyAuthenticationCannotUseTheWindowsTokenProvider()
    {
        Assert.Throws<InvalidOperationException>(() => IdentityProviderOptions.Read(Configuration(
            ("IdentityServer:Authentication:Mode", "ProxyKerberos")), isWindows: true));
        var configured = IdentityProviderOptions.Read(LdapConfiguration("FreeIpa", ("IdentityServer:Authentication:Mode", "ProxyKerberos")));
        Assert.Equal(ProxyKerberosOptions.Scheme, configured.AuthenticationScheme);
        Assert.False(configured.Allows(Principal(protocol: "Kerberos")));
        Assert.True(configured.Allows(Principal(protocol: ProxyKerberosOptions.Scheme)));
    }
}
