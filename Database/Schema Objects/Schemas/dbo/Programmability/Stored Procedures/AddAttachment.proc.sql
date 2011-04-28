-- =============================================
-- Author:      Sergey Solyanik
-- Create date: 6/22/2009
-- Description: Adds an attachment to a change list
-- =============================================
CREATE PROCEDURE [dbo].[AddAttachment]
    @ChangeId       int,
    @Description    NVARCHAR(128),
    @Link           NVARCHAR(MAX),
    @result         int OUTPUT
AS
BEGIN
    DECLARE @AttachmentId int
    SET @AttachmentId = (SELECT Id FROM dbo.Attachment WHERE ChangeListId = @ChangeId AND Link = @Link)
    IF @AttachmentId IS NOT NULL
    BEGIN
        SET @result = @AttachmentId
        UPDATE dbo.Attachment SET TimeStamp = GETUTCDATE(), Description = @Description WHERE Id = @AttachmentId
        RETURN
    END
    INSERT INTO dbo.Attachment (ChangeListId, TimeStamp, Description, Link)
        VALUES(@ChangeId, GETUTCDATE(), @Description, @Link)
    SET @result = @@IDENTITY
END
