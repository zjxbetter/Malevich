-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 6/23/2009
-- Description: Changes CL name
-- =============================================
CREATE PROCEDURE RenameChangeList
    @ChangeId int,
    @NewCL nvarchar(128)
AS
BEGIN
    EXEC dbo.MaybeAudit @ChangeId, "RENAME"
    UPDATE dbo.ChangeList SET CL = @NewCL WHERE Id = @ChangeId
END
