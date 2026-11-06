using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Same backup and Billing calls as the legacy per-file flow, fed from landing storage. The SHA-256 recorded at download is
/// checked first, so Billing never receives content that changed or was corrupted after it left SFTP.
/// </summary>
public sealed class LandedFileRegistrar : ILandedFileRegistrar
{
    private readonly ILandingFileStore _landingStore;
    private readonly IBackupFileStore _backupFileStore;
    private readonly IBillingFileRegistrar _billingFileRegistrar;
    private readonly ILogger<LandedFileRegistrar> _logger;

    /// <summary>Creates the registrar.</summary>
    public LandedFileRegistrar(
        ILandingFileStore landingStore,
        IBackupFileStore backupFileStore,
        IBillingFileRegistrar billingFileRegistrar,
        ILogger<LandedFileRegistrar> logger)
    {
        _landingStore = landingStore;
        _backupFileStore = backupFileStore;
        _billingFileRegistrar = billingFileRegistrar;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BillingRegistration> RegisterAsync(ClearingHouseDescriptor clearingHouse, InboundFileEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.LandingBlobName is null || entry.Sha256 is null)
        {
            throw new InvalidOperationException($"{entry.FileName} has no landed copy.");
        }

        using var content = await _landingStore.ReadAsync(entry.LandingBlobName, entry.SizeBytes, cancellationToken).ConfigureAwait(false);
        var actualHash = Convert.ToHexString(SHA256.HashData(content.Memory.Span));
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Landed copy of {entry.FileName} does not match the SHA-256 recorded at download.");
        }

        var backup = await _backupFileStore.WriteOnceAsync(entry.FileName, content.Memory, cancellationToken).ConfigureAwait(false);
        InboundLog.BackupWritten(_logger, backup.AlreadyExisted ? "already existed" : "uploaded", entry.FileName);

        var registration = await _billingFileRegistrar.RegisterAsync(clearingHouse, entry.FileName, content.Memory, cancellationToken).ConfigureAwait(false);
        InboundLog.BillingRegistered(_logger, entry.FileName, registration.Accepted, registration.Queued, registration.FileId);
        return registration;
    }
}
