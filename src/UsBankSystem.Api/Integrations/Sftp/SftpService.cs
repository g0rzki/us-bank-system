using Renci.SshNet;

namespace UsBankSystem.Api.Integrations.Sftp;

public class SftpService : ISftpService, IDisposable
{
    private readonly ILogger<SftpService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly PrivateKeyFile _keyFile;   // parsed once at startup, not on every operation
    private readonly string? _expectedFingerprint;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SftpClient? _client;

    public SftpService(IConfiguration configuration, ILogger<SftpService> logger)
    {
        _logger = logger;
        _host = configuration["Ach:Sftp:Host"] ?? "localhost";
        _port = configuration.GetValue<int?>("Ach:Sftp:Port") ?? 2221;
        _username = configuration["Ach:Sftp:Username"] ?? "us-bank-a";
        var keyPath = configuration["Ach:Sftp:PrivateKeyPath"] ?? "sftp_keys/id_rsa";
        _keyFile = new PrivateKeyFile(keyPath);
        _expectedFingerprint = configuration["Ach:Sftp:HostFingerprint"];
    }

    // Reuses the existing connection and reconnects only when needed
    private SftpClient GetConnectedClient()
    {
        if (_client is { IsConnected: true }) return _client;
        _client?.Dispose();
        _client = new SftpClient(_host, _port, _username, _keyFile)
        {
            // Caps how long a single SSH.NET blocking call can take.
            // Prevents shutdown from hanging indefinitely on a stalled SFTP operation.
            OperationTimeout = TimeSpan.FromSeconds(30)
        };
        _client.HostKeyReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(_expectedFingerprint))
            {
                _logger.LogWarning("SFTP host key not verified — set Ach:Sftp:HostFingerprint in production to prevent MITM");
                e.CanTrust = true;
                return;
            }
            e.CanTrust = string.Equals(e.FingerPrintSHA256, _expectedFingerprint, StringComparison.OrdinalIgnoreCase);
            if (!e.CanTrust)
                _logger.LogError("SFTP host key mismatch — expected {Expected}, got {Actual}", _expectedFingerprint, e.FingerPrintSHA256);
        };
        _client.Connect();
        return _client;
    }

    public async Task UploadAsync(string remotePath, byte[] content, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                var client = GetConnectedClient();
                using var stream = new MemoryStream(content);
                client.UploadFile(stream, remotePath);
                _logger.LogInformation("SFTP upload: {Path} ({Bytes} bytes)", remotePath, content.Length);
            });
        }
        finally { _lock.Release(); }
    }

    public async Task<byte[]?> DownloadAsync(string remotePath, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                var client = GetConnectedClient();
                if (!client.Exists(remotePath)) return null;
                using var stream = new MemoryStream();
                client.DownloadFile(remotePath, stream);
                return stream.ToArray();
            });
        }
        finally { _lock.Release(); }
    }

    public async Task<IEnumerable<string>> ListFilesAsync(string remoteDir, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() =>
            {
                var client = GetConnectedClient();
                return client.ListDirectory(remoteDir)
                    .Where(f => f.IsRegularFile)
                    .Select(f => f.Name)
                    .ToList();
            });
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                var client = GetConnectedClient();
                client.DeleteFile(remotePath);
            });
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _lock.Dispose();
    }
}
