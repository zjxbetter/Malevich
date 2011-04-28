-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 11/29/2008
-- Description: Sets CL status to SUBMITTED
-- =============================================
CREATE PROCEDURE SubmitChangeList
    @ChangeId int
AS
BEGIN
    EXEC dbo.MaybeAudit @ChangeId, "CLOSE"
    UPDATE dbo.ChangeList SET Stage = 2 WHERE Id = @ChangeId
END
