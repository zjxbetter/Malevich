-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 12/18/2008
-- Description:	Gets the alias of the called user
-- =============================================
CREATE FUNCTION [dbo].[GetCurrentUserAlias] 
(
)
RETURNS nvarchar(50)
AS
BEGIN
    DECLARE @name nvarchar(50)
    SET @name = SUSER_SNAME()
    DECLARE @bsposition int
    SET @bsposition = CHARINDEX('\', @name, 1)
    IF @bsposition <= 0
        RETURN @name
    
    RETURN SUBSTRING(@name, @bsposition + 1, LEN(@name) - @bsposition)
END
