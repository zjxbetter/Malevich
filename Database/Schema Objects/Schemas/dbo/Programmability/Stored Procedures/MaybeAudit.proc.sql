-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 6/23/2009
-- Description: Potentially audits an action
-- =============================================
CREATE PROCEDURE [dbo].[MaybeAudit]
	@ChangeId int, 
	@Action nvarchar(50),
	@Description nvarchar(MAX) = NULL
AS
BEGIN
    DECLARE @UserName nvarchar(50) = dbo.GetCurrentUserAlias()
    DECLARE @ClUserName nvarchar(50) = (SELECT UserName FROM dbo.ChangeList WHERE Id = @ChangeId)

    IF @UserName != @ClUserName
    BEGIN
        INSERT INTO dbo.AuditRecord (TimeStamp, UserName, ChangeListId, Action, Description)
            VALUES(GETUTCDATE(), @UserName, @ChangeId, @Action, @Description)
    END
END