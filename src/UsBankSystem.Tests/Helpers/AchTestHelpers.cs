using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Integrations.Sftp;

namespace UsBankSystem.Tests.Helpers;

/// <summary>
/// No-op SFTP stub: uploads and deletes succeed silently, downloads return empty bytes.
/// </summary>
public class StubSftpService : ISftpService
{
    public Task UploadAsync(string remotePath, byte[] content, CancellationToken ct = default) => Task.CompletedTask;
    public Task<byte[]?> DownloadAsync(string remotePath, CancellationToken ct = default) => Task.FromResult<byte[]?>(Array.Empty<byte>());
    public Task<IEnumerable<string>> ListFilesAsync(string remoteDir, CancellationToken ct = default) => Task.FromResult<IEnumerable<string>>([]);
    public Task DeleteAsync(string remotePath, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Stub sequencer: always returns sequence 1. Does not touch the database.
/// </summary>
public class StubAchTraceSequencer : IAchTraceSequencer
{
    public Task<int> NextAsync(CancellationToken ct = default) => Task.FromResult(1);
}

public static class AchTestHelpers
{
    public static IConfiguration AchConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ach:RoutingNumber"] = "110000000",
                ["Ach:LegalName"] = "US Bank A",
                ["Ach:FrbRoutingNumber"] = "090000515",
                ["Ach:FrbName"] = "FRB Tungsten",
            })
            .Build();

    public static AchGateway CreateGateway(HttpStatusCode statusCode, string responseBody) =>
        new(
            new HttpClient(new MockHttpMessageHandler(statusCode, responseBody))
                { BaseAddress = new Uri("http://localhost:8310") },
            new StubSftpService(),
            new StubAchTraceSequencer(),
            AchConfig(),
            NullLogger<AchGateway>.Instance);
}
