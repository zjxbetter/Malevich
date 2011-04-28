-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 2/14/2009
-- Description:	Set user context key-value pair
-- =============================================
CREATE PROCEDURE [dbo].[SetUserContext] 
    @key nvarchar(50),
    @value nvarchar(MAX)
AS
BEGIN
    DECLARE @UserName nvarchar(50)
    SET @UserName = dbo.GetCurrentUserAlias()

    DECLARE @Id int
    SET @Id = (SELECT Id FROM dbo.UserContext WHERE UserName = @UserName AND KeyName = @key)
    IF @Id IS NOT NULL
    BEGIN
        UPDATE dbo.UserContext SET Value = @value WHERE Id = @Id
        RETURN
    END

    INSERT INTO dbo.UserContext (UserName, KeyName, Value) VALUES(@UserName, @key, @value)
END
