-- Administrator-only: back up the database and verify ownership before changing @Apply.
-- Existing assigned rows are never reassigned. No automatic first-login claim.
SET XACT_ABORT ON;
DECLARE @ApplicationUserId nvarchar(450) = N'REPLACE-WITH-VERIFIED-USER-ID';
DECLARE @Apply bit = 0;
IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE Id = @ApplicationUserId AND EmailConfirmed = 1)
    THROW 50001, 'Specify an existing verified user ID.', 1;
SELECT Id, Email, DisplayName FROM AspNetUsers WHERE Id = @ApplicationUserId;
SELECT Id, WorkDate, Title FROM WorkLogs WHERE ApplicationUserId IS NULL ORDER BY Id;
IF @Apply = 1
BEGIN
    BEGIN TRANSACTION;
    UPDATE WorkLogs SET ApplicationUserId = @ApplicationUserId WHERE ApplicationUserId IS NULL;
    SELECT @@ROWCOUNT AS AssignedRows;
    COMMIT TRANSACTION;
END;
