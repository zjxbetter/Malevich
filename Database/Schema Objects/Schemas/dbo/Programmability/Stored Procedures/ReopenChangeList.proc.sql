-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 6/23/2009
-- Description: Sets CL status to ACTIVE
-- =============================================
CREATE PROCEDURE ReopenChangeList
    @ChangeId int
AS
BEGIN
    EXEC dbo.MaybeAudit @ChangeId, "REOPEN"
    UPDATE dbo.ChangeList SET Stage = 0 WHERE Id = @ChangeId
END
