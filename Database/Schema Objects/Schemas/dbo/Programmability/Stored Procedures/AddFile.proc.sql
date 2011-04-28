-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 11/29/2008
-- Description:	Adds a file to a change list
-- =============================================
CREATE PROCEDURE [dbo].[AddFile]
    @ChangeId int,
    @LocalFile nvarchar(512),
    @ServerFile nvarchar(512),
    @result int OUTPUT
AS
BEGIN
    DECLARE @FileId int
    SET @FileId = (SELECT Id FROM dbo.ChangeFile
        WHERE ChangeListId = @ChangeId AND ServerFileName = @ServerFile AND IsActive = 1)
    IF @FileId IS NOT NULL
    BEGIN
        SET @result = @FileId
        RETURN
    END
    INSERT INTO dbo.ChangeFile (ChangeListId, LocalFileName, ServerFileName, IsActive)
        VALUES(@ChangeId, @LocalFile, @ServerFile, 1)
    SET @result = @@IDENTITY
END
