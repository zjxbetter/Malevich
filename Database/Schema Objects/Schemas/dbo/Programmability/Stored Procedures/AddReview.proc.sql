-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 12/23/2008
-- Description:	Adds a code review
-- =============================================
CREATE PROCEDURE [dbo].[AddReview]
	@ChangeId int,
	@Text nvarchar(2048) = NULL,
	@Status tinyint = NULL,
	@Result int OUTPUT
AS
BEGIN
    DECLARE @UserName nvarchar(50)
    SET @UserName = dbo.GetCurrentUserAlias()

    DECLARE @ReviewId int
    SET @ReviewId = (SELECT Id FROM dbo.Review
                     WHERE ChangeListId = @ChangeId AND UserName = @UserName AND IsSubmitted = 0)
    IF @ReviewId IS NOT NULL
    BEGIN
        IF @Text IS NOT NULL
            UPDATE dbo.Review SET CommentText = @Text WHERE Id = @ReviewId

        IF @Status IS NOT NULL
            UPDATE dbo.Review SET OverallStatus = @Status WHERE Id = @ReviewId

        SET @Result = @ReviewId
        RETURN
    END

    DECLARE @Status2 int
    SET @Status2 = 0
    IF @Status IS NOT NULL
        SET @Status2 = @Status

    INSERT INTO dbo.Review (ChangeListId, UserName, TimeStamp, IsSubmitted, OverallStatus, CommentText)
        VALUES(@ChangeId, @UserName, GETUTCDATE(), 0, @Status2, @Text)
    SET @Result = @@IDENTITY
END
