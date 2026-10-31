using System.DirectoryServices.Protocols;
using System.Formats.Asn1;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace IdentityServer.Tests;

public sealed class LdapTransportTests
{
    [Fact]
    public async Task LinuxStartTls_ConnectsAnonymously_AndNeverSendsCredentialsWhenUpgradeFails()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Exercises Linux LDAP connection initialization.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IdentityServer:Directory:Provider"] = "ActiveDirectory",
            ["IdentityServer:Directory:Server"] = $"ldap://127.0.0.1:{port}",
            ["IdentityServer:Directory:Realm"] = "EXAMPLE.TEST",
            ["IdentityServer:Directory:BaseDn"] = "dc=example,dc=test",
            ["IdentityServer:Directory:BindDn"] = "reader@example.test",
            ["IdentityServer:Directory:BindPassword"] = "must-never-be-sent-in-cleartext",
            ["IdentityServer:Directory:TimeoutSeconds"] = "3"
        }).Build();

        var connection = Task.Run(() => Record.Exception(() =>
        {
            using var ldap = new LdapSearch(IdentityProviderOptions.Read(configuration));
        }), timeout.Token);

        using TcpClient client = await listener.AcceptTcpClientAsync(timeout.Token);
        using NetworkStream stream = client.GetStream();
        byte[] anonymousBind = await ReadMessage(stream, timeout.Token);
        var bindMessage = new AsnReader(anonymousBind, AsnEncodingRules.BER).ReadSequence();
        int bindId = (int)bindMessage.ReadInteger();
        var bind = bindMessage.ReadSequence(new Asn1Tag(TagClass.Application, 0, true));
        Assert.Equal(3, (int)bind.ReadInteger());
        Assert.Empty(bind.ReadOctetString()); // No bind DN.
        Assert.Empty(bind.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0))); // No password.
        bind.ThrowIfNotEmpty();
        await stream.WriteAsync(Response(bindId, 1, ResultCode.Success), timeout.Token);

        byte[] upgrade = await ReadMessage(stream, timeout.Token);
        var upgradeMessage = new AsnReader(upgrade, AsnEncodingRules.BER).ReadSequence();
        int upgradeId = (int)upgradeMessage.ReadInteger();
        var request = upgradeMessage.ReadSequence(new Asn1Tag(TagClass.Application, 23, true));
        Assert.Equal("1.3.6.1.4.1.1466.20037",
            Encoding.ASCII.GetString(request.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0))));
        await stream.WriteAsync(Response(upgradeId, 24, ResultCode.UnwillingToPerform), timeout.Token);

        Assert.IsAssignableFrom<DirectoryException>(await connection.WaitAsync(timeout.Token));
        // Disposal may send an LDAP Unbind, but must not attempt a credentialed bind.
        using var remaining = new MemoryStream();
        await stream.CopyToAsync(remaining, timeout.Token);
        var trailing = new AsnReader(remaining.ToArray(), AsnEncodingRules.BER);
        while (trailing.HasData)
        {
            var message = trailing.ReadSequence();
            message.ReadInteger();
            message.ReadNull(new Asn1Tag(TagClass.Application, 2));
            message.ThrowIfNotEmpty();
        }
    }

    [Fact]
    public async Task SizeLimitedUserSearch_ReturnsPartialEntries_SoDuplicatesAreAPolicyRejection()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var options = IdentityProviderOptions.Read(IdentityProviderTests.LdapConfiguration());
        // Plain loopback LDAP isolates search handling from the transport security tested above.
        var provider = new LdapUserProfileProvider(options, () =>
        {
            var connection = new LdapConnection(new LdapDirectoryIdentifier("127.0.0.1", port))
            {
                AuthType = AuthType.Anonymous, Timeout = TimeSpan.FromSeconds(5)
            };
            connection.SessionOptions.ProtocolVersion = 3;
            return new LdapSearch(connection);
        });
        var lookup = Task.Run(() => Record.Exception(() => provider.GetProfile(IdentityProviderTests.Principal(), true)), timeout.Token);

        using TcpClient client = await listener.AcceptTcpClientAsync(timeout.Token);
        using NetworkStream stream = client.GetStream();
        int searches = 0;
        while (true)
        {
            var message = new AsnReader(await ReadMessage(stream, timeout.Token), AsnEncodingRules.BER).ReadSequence();
            int id = (int)message.ReadInteger();
            Asn1Tag operation = message.PeekTag();
            if (operation.HasSameClassAndValue(new Asn1Tag(TagClass.Application, 0)))
            {
                await stream.WriteAsync(Response(id, 1, ResultCode.Success), timeout.Token);
                continue;
            }
            Assert.True(operation.HasSameClassAndValue(new Asn1Tag(TagClass.Application, 3)), operation.ToString());
            var search = message.ReadSequence(operation);
            search.ReadOctetString();
            search.ReadEnumeratedBytes();
            search.ReadEnumeratedBytes();
            Assert.Equal(2, (int)search.ReadInteger()); // The user search asks for at most two entries.
            searches++;
            // A server holding three matches returns its limit of two and then SizeLimitExceeded.
            foreach (string uid in new[] { "alice", "alice2" })
                await stream.WriteAsync(Entry(id, $"uid={uid},cn=users,cn=accounts,dc=example,dc=com"), timeout.Token);
            await stream.WriteAsync(Response(id, 5, ResultCode.SizeLimitExceeded), timeout.Token);
            break;
        }

        var error = Assert.IsType<DirectoryIdentityException>(await lookup.WaitAsync(timeout.Token));
        Assert.Contains("exactly one", error.Message);
        Assert.Equal(1, searches);
    }

    private static byte[] Entry(int messageId, string dn)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(messageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, 4, true)))
            {
                writer.WriteOctetString(Encoding.UTF8.GetBytes(dn));
                using (writer.PushSequence())
                using (writer.PushSequence())
                {
                    writer.WriteOctetString("ipaUniqueID"u8);
                    using (writer.PushSetOf()) writer.WriteOctetString(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
                }
            }
        }
        return writer.Encode();
    }

    private static byte[] Response(int messageId, int applicationTag, ResultCode result)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(messageId);
            using (writer.PushSequence(new Asn1Tag(TagClass.Application, applicationTag, true)))
            {
                writer.WriteEnumeratedValue(result);
                writer.WriteOctetString([]);
                writer.WriteOctetString([]);
            }
        }
        return writer.Encode();
    }

    private static async Task<byte[]> ReadMessage(NetworkStream stream, CancellationToken token)
    {
        byte[] header = new byte[2];
        await stream.ReadExactlyAsync(header, token);
        Assert.Equal(0x30, header[0]);
        int size = header[1];
        byte[] length = [];
        if ((size & 0x80) != 0)
        {
            int bytes = size & 0x7f;
            Assert.InRange(bytes, 1, 4);
            length = new byte[bytes];
            await stream.ReadExactlyAsync(length, token);
            size = 0;
            foreach (byte part in length) size = checked(size * 256 + part);
        }
        Assert.InRange(size, 1, 4096);
        byte[] body = new byte[size];
        await stream.ReadExactlyAsync(body, token);
        return [.. header, .. length, .. body];
    }
}
