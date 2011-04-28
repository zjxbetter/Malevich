-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 1/18/2008
-- Description:	Add a request for a code review
-- =============================================
CREATE PROCEDURE [dbo].[AddReviewRequest]
    @ChangeListId int,
    @ReviewerAlias nvarchar(50)
AS
BEGIN
    INSERT INTO dbo.MailReviewRequest (ChangeListId, ReviewerAlias) VALUES(@ChangeListId, @ReviewerAlias)
END
