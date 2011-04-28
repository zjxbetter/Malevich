-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 11/29/2008
-- Description:	Marks review as submitted
-- =============================================
CREATE PROCEDURE SubmitReview
    @ReviewId int
AS
BEGIN
    UPDATE dbo.Review SET [IsSubmitted] = 1, [TimeStamp] = GETUTCDATE() WHERE Id = @ReviewId
    INSERT INTO dbo.MailReview (ReviewId) VALUES(@ReviewId)
END
