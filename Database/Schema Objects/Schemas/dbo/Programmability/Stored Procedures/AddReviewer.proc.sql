-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 12/18/2008
-- Description:	Add a code reviewer
-- =============================================
CREATE PROCEDURE [dbo].[AddReviewer] 
    @ReviewerAlias nvarchar(50),
    @ChangeListId int,
    @result int OUTPUT
AS
BEGIN
    DECLARE @ReviewerId int
    SET @ReviewerId = (SELECT Id FROM dbo.Reviewer
        WHERE ChangeListId = @ChangeListId AND ReviewerAlias = @ReviewerAlias)
    IF @ReviewerId IS NOT NULL
    BEGIN
        SET @result = @ReviewerId
        RETURN
    END
    INSERT INTO dbo.Reviewer (ReviewerAlias, ChangeListId)
        VALUES(@ReviewerAlias, @ChangeListId)
    SET @result = @@IDENTITY
    INSERT INTO dbo.MailChangeList (ChangeListId, ReviewerId) VALUES(@ChangeListId, @result)
END
