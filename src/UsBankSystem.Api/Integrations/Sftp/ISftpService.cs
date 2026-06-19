namespace UsBankSystem.Api.Integrations.Sftp;

public interface ISftpService
{
    Task UploadAsync(string remotePath, byte[] content, CancellationToken ct = default);
    Task<byte[]?> DownloadAsync(string remotePath, CancellationToken ct = default);
    Task<IEnumerable<string>> ListFilesAsync(string remoteDir, CancellationToken ct = default);
    Task DeleteAsync(string remotePath, CancellationToken ct = default);
}
