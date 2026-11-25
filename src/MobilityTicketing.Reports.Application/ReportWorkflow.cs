using System.Security.Cryptography;

namespace MobilityTicketing.Reports.Application;

public sealed class ReportWorkflow(
    IReportObjectStore storage, IReportCatalogue catalogue, IUploadReceipts receipts, ExportLocation destination)
{
    private readonly IReportObjectStore _storage = storage;
    private readonly IReportCatalogue _catalogue = catalogue;
    private readonly IUploadReceipts _receipts = receipts;
    private readonly ExportLocation _destination = destination;

    public async Task<string> StageAsync(string reportId, byte[] bytes)
    {
        var (sha256, rowCount) = Measure(bytes);

        _receipts.Reserve(reportId);                                   // (a) reservér FØR PUT

        var versionId = await _storage.UploadAsync(_destination, bytes);   // (b) PUT til S3
        if (string.IsNullOrWhiteSpace(versionId) || versionId is "None" or "null")   // (c)(d)
            throw new InvalidOperationException("MissingVersionId");

        await _receipts.WriteAsync(new ReportInput(
            reportId, _destination.Bucket, _destination.Key, versionId, sha256, rowCount));  // (e)
        return versionId;
    }

    public Task<string> PublishAsync(string reportId, byte[] bytes, string? supersedes) =>
        throw new NotImplementedException("Complete PublishAsync.");

    public Task<string> RecoverAsync(string reportId, string? supersedes) =>
        throw new NotImplementedException("Complete RecoverAsync.");

    public Task<Verification> VerifyAsync(string reportId) =>
        throw new NotImplementedException("Complete VerifyAsync.");

    private Task<Verification> CheckAsync(ReportInput input) =>
        throw new NotImplementedException("Complete CheckAsync.");

    private static (string Sha256, int RowCount) Measure(byte[] bytes) =>
        (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),   // (f)
        bytes.Count(b => b == (byte)'\n'));
}
