-- Run by the platform team immediately before switching Pipeline:Mode to Active (from Shadow, or back from Off after a rollback),
-- with the legacy downloadEdiDataFromSftp caller already stopped and the pipeline workers scaled to zero.
--
-- While the legacy run owned the SFTP folder, it registered files with Billing and deleted them, but the pipeline's manifest
-- still shows its own copies as Landed or Queued. Processing those copies in Active mode would send them to Billing a second
-- time. This removes every row Billing has not been called for, so the first Active listing registers only the files that are
-- still on SFTP. Rows with BillingQueuedUtc set are kept: Billing already has them and only their remote delete may remain.
-- Orphaned landing blobs expire through the container lifecycle rule; leftover queue messages are completed as orphans (1110).
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DELETE FROM [inbound].[InboundFile]
WHERE [BillingQueuedUtc] IS NULL
  AND [State] NOT IN ('Done', 'Superseded');

SELECT [State], COUNT(*) AS [Files]
FROM [inbound].[InboundFile]
GROUP BY [State];

COMMIT TRANSACTION;
