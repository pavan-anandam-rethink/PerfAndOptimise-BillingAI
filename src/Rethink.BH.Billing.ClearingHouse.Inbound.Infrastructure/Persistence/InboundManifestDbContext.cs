using Microsoft.EntityFrameworkCore;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>
/// The pipeline manifest. Schema changes ship as scripts in <c>deployment/sql</c>; a unit test keeps the script identical to
/// this model, so the service never creates or migrates tables at runtime (least privilege: data read/write only).
/// </summary>
public sealed class InboundManifestDbContext : DbContext
{
    /// <summary>SQL Server schema.</summary>
    public const string Schema = "inbound";

    /// <summary>Table name.</summary>
    public const string TableName = "InboundFile";

    /// <summary>Longest stored failure reason.</summary>
    public const int LastErrorMaxLength = 1000;

    /// <summary>Creates the context.</summary>
    public InboundManifestDbContext(DbContextOptions<InboundManifestDbContext> options)
        : base(options)
    {
    }

    /// <summary>Tracked files.</summary>
    public DbSet<InboundFileRecord> Files => Set<InboundFileRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        if (Database.IsSqlServer())
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        var file = modelBuilder.Entity<InboundFileRecord>();
        file.ToTable(TableName);
        file.HasKey(record => record.Id);
        file.Property(record => record.Id).ValueGeneratedNever();
        file.Property(record => record.RemotePath).HasMaxLength(512).IsRequired();
        file.Property(record => record.FileName).HasMaxLength(255).IsRequired();
        file.Property(record => record.State).HasMaxLength(20).IsUnicode(false).IsRequired();
        file.Property(record => record.LandingBlobName).HasMaxLength(1024);
        file.Property(record => record.Sha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        file.Property(record => record.LastError).HasMaxLength(LastErrorMaxLength);

        file.HasIndex(record => new { record.ClearingHouseId, record.RemotePath, record.SizeBytes, record.RemoteModifiedUtc })
            .IsUnique()
            .HasDatabaseName("UX_InboundFile_RemoteVersion");
        file.HasIndex(record => new { record.ClearingHouseId, record.State, record.UpdatedUtc })
            .HasDatabaseName("IX_InboundFile_ClearingHouse_State_Updated");
        file.HasIndex(record => new { record.State, record.DiscoveredUtc })
            .HasDatabaseName("IX_InboundFile_State_Discovered");
    }
}
