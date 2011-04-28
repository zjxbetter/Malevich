-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 11/29/2008
-- Description: Sets CL status to DELETED
-- =============================================
CREATE PROCEDURE DeleteChangeList
    @ChangeId int
AS
BEGIN
    EXEC dbo.MaybeAudit @ChangeId, "DELETE"
    UPDATE dbo.ChangeList SET Stage = 3 WHERE Id = @ChangeId
END
