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

    public async Task<Verification> VerifyAsync(string reportId)
    {
        ReportInput? input;
        try
        {
            input = await _catalogue.FindAsync(reportId);
        }
        catch (CatalogueException)
        {
            return Verification.CatalogueReadFailed;
        }

        if (input is null)
            return Verification.UnknownReport;

        return await CheckAsync(input);
    }

    private async Task<Verification> CheckAsync(ReportInput input)
    {
        byte[] bytes;
        try
        {
            bytes = await _storage.ReadVersionAsync(
                new ExportLocation(input.Bucket, input.Key), input.VersionId);
        }
        catch (ObjectStoreException)
        {
            return Verification.ReadFailed;
        }

        var (sha256, rowCount) = Measure(bytes);

        return sha256 == input.Sha256 && rowCount == input.RowCount
            ? Verification.Matches
            : Verification.Mismatch;
    }

    private static (string Sha256, int RowCount) Measure(byte[] bytes) =>
        (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),   // (f)
        bytes.Count(b => b == (byte)'\n'));
}
