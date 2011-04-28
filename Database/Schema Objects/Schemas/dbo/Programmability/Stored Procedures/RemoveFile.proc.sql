-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 11/29/2008
-- Description:	Removes a file from a change list
-- =============================================
CREATE PROCEDURE RemoveFile
    @FileId int
AS
BEGIN
    UPDATE dbo.ChangeFile SET IsActive = 0 WHERE Id = @FileId
END
