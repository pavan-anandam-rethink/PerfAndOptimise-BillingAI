-- Inbound pipeline manifest (Azure SQL). Run once per environment by the platform team before Pipeline:Mode leaves Off.
-- The service never creates or migrates tables; its workload identity needs data access only:
--   CREATE USER [<workload-identity-name>] FROM EXTERNAL PROVIDER;
--   GRANT SELECT, INSERT, UPDATE ON SCHEMA::[inbound] TO [<workload-identity-name>];
-- Everything below the marker is generated from InboundManifestDbContext; ManifestTests fails when it drifts.
-- <generated>
IF SCHEMA_ID(N'inbound') IS NULL EXEC(N'CREATE SCHEMA [inbound];');
GO


CREATE TABLE [inbound].[InboundFile] (
    [Id] uniqueidentifier NOT NULL,
    [ClearingHouseId] int NOT NULL,
    [RemotePath] nvarchar(512) NOT NULL,
    [FileName] nvarchar(255) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [RemoteModifiedUtc] datetime2 NOT NULL,
    [State] varchar(20) NOT NULL,
    [Attempts] int NOT NULL,
    [LandingBlobName] nvarchar(1024) NULL,
    [Sha256] char(64) NULL,
    [BillingFileId] int NULL,
    [BillingQueuedUtc] datetime2 NULL,
    [LastError] nvarchar(1000) NULL,
    [ClaimExpiresUtc] datetime2 NULL,
    [DiscoveredUtc] datetime2 NOT NULL,
    [UpdatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_InboundFile] PRIMARY KEY ([Id])
);
GO


CREATE INDEX [IX_InboundFile_ClearingHouse_State_Updated] ON [inbound].[InboundFile] ([ClearingHouseId], [State], [UpdatedUtc]);
GO


CREATE INDEX [IX_InboundFile_State_Discovered] ON [inbound].[InboundFile] ([State], [DiscoveredUtc]);
GO


CREATE UNIQUE INDEX [UX_InboundFile_RemoteVersion] ON [inbound].[InboundFile] ([ClearingHouseId], [RemotePath], [SizeBytes], [RemoteModifiedUtc]);
GO
