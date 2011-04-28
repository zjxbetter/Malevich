-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 11/29/2008
-- Description:	Removes a comment
-- =============================================
CREATE PROCEDURE [dbo].[DeleteComment]
    @CommentId int
AS
BEGIN
    DELETE FROM dbo.Comment WHERE Id = @CommentId
END
