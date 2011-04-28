-- =============================================
-- Author: Sergey Solyanik
-- Create date: 5/19/2009
-- Description: Coverts time stamps in the change
--              lists from local to UTC
-- =============================================
DECLARE @UTCDate datetime
DECLARE @LocalDate datetime
DECLARE @TimeDiff int 
  
SET @UTCDate = GETUTCDATE()
SET @LocalDate = GETDATE()
SET @TimeDiff = DATEDIFF(hh, @LocalDate, @UTCDate) 

UPDATE [dbo].[ChangeList] SET TimeStamp = DATEADD(hh, @TimeDiff, TimeStamp)