using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IdentityServer.Directory.Tests;

/// <summary>Binding the <c>Directory</c> section and registering the lookup the way the README shows.</summary>
public class DirectoryOptionsTests
{
    [Fact]
    public void MissingSection_GivesTheDefaults()
    {
        using ServiceProvider services = Build(new ConfigurationBuilder().Build());

        DirectoryOptions directory = services.GetRequiredService<IOptions<DirectoryOptions>>().Value;
        Assert.Null(directory.UserRoot);
        Assert.Null(directory.UserRootGlobal);
        Assert.Null(directory.GroupRoot);
        Assert.Equal(2, directory.GlobalSearchMinLength);
        Assert.Equal(20, directory.GlobalSearchSizeLimitBuffer);
        Assert.Equal(20, directory.DefaultTake);
        Assert.Equal(50, directory.MaxTake);
        Assert.Equal(500, directory.MaxGroupMembers);
        Assert.IsType<ActiveDirectoryLookup>(services.GetRequiredService<IDirectoryLookup>());
    }

    [Fact]
    public void DirectorySection_BindsTheLookupSettings()
    {
        using ServiceProvider services = Build(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Directory:UserRoot"] = "LDAP://OU=Local,DC=example,DC=com",
            ["Directory:UserRootGlobal"] = "LDAP://DC=example,DC=com",
            ["Directory:GroupRoot"] = "LDAP://OU=Groups,DC=example,DC=com",
            ["Directory:GlobalSearchMinLength"] = "3",
            ["Directory:MaxTake"] = "10",
            ["Directory:MaxGroupMembers"] = "100",
        }).Build());

        DirectoryOptions directory = services.GetRequiredService<IOptions<DirectoryOptions>>().Value;
        Assert.Equal("LDAP://OU=Local,DC=example,DC=com", directory.UserRoot);
        Assert.Equal("LDAP://DC=example,DC=com", directory.UserRootGlobal);
        Assert.Equal("LDAP://OU=Groups,DC=example,DC=com", directory.GroupRoot);
        Assert.Equal(3, directory.GlobalSearchMinLength);
        Assert.Equal(10, directory.MaxTake);
        Assert.Equal(100, directory.MaxGroupMembers);
    }

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddOptions<DirectoryOptions>().BindConfiguration(DirectoryOptions.SectionName);
        services.AddSingleton<IDirectoryLookup, ActiveDirectoryLookup>();
        return services.BuildServiceProvider();
    }
}
