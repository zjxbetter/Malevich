-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 11/29/2008
-- Description:	Adds or updates a comment
-- =============================================
CREATE PROCEDURE [dbo].[AddComment]
    @FileVersion int,
    @Line int,
    @LineStamp bigint,
    @Text nvarchar(2048),
    @result int OUTPUT
AS
BEGIN
    DECLARE @FileId int
    SET @FileId = (SELECT FileId FROM dbo.FileVersion WHERE Id = @FileVersion)

    DECLARE @ChangeListId int
    SET @ChangeListId = (SELECT ChangeListId FROM dbo.ChangeFile WHERE Id = @FileId)

    DECLARE @ReviewId int
    EXEC dbo.AddReview @ChangeId = @ChangeListId, @result = @ReviewId OUTPUT

    DECLARE @CommentId int
    SET @CommentId = (SELECT Id FROM dbo.Comment
        WHERE ReviewId = @ReviewId AND FileVersionId = @FileVersion AND Line = @Line AND LineStamp = @LineStamp)
    IF @CommentId IS NOT NULL
    BEGIN
        UPDATE dbo.Comment SET CommentText = @Text WHERE Id = @CommentId
        SET @result = @CommentId
        RETURN
    END

    INSERT INTO dbo.Comment (ReviewId, FileVersionId, Line, LineStamp, CommentText)
        VALUES(@ReviewId, @FileVersion, @Line, @LineStamp, @Text)
    SET @result = @@IDENTITY
END
