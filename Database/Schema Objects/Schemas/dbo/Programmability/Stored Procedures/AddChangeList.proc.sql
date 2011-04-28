-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 11/29/2008
-- Description: Adds a new change list
-- =============================================
CREATE PROCEDURE [dbo].[AddChangeList]
    @SourceControl int,
    @UserClient nvarchar(50),
    @CL nvarchar(128),
    @Description nvarchar(MAX),
    @TimeStamp datetime,
    @result int OUTPUT
AS
BEGIN
    DECLARE @UserName nvarchar(50)
    SET @UserName = dbo.GetCurrentUserAlias()
    DECLARE @ChangeId int
    SET @ChangeId = (SELECT Id FROM dbo.ChangeList
        WHERE SourceControlId = @SourceControl AND UserName = @UserName AND UserClient = @UserClient AND CL = @CL)
    IF @ChangeId IS NOT NULL
    BEGIN
        SET @result = @ChangeId
        UPDATE dbo.ChangeList SET Stage = 0, Description = @Description, TimeStamp = @TimeStamp WHERE Id = @ChangeId
        RETURN
    END
    INSERT INTO dbo.ChangeList (SourceControlId, UserName, UserClient, CL, Description, TimeStamp, Stage)
        VALUES(@SourceControl, @UserName, @UserClient, @CL, @Description, @TimeStamp, 0)
    SET @result = @@IDENTITY
END
