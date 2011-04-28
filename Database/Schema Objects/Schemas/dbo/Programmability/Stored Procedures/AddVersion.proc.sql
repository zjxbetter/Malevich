-- =============================================
-- Author:		Sergey Solyanik
-- Create date: 11/29/2008
-- Description:	Adds a file version to a file
-- =============================================
CREATE PROCEDURE AddVersion
    @FileId int,
    @Revision int,
    @Action int,
    @TimeStamp datetime,
    @IsText bit,
    @IsFullText bit,
    @IsRevisionBase bit,
    @Text varchar(MAX),
    @result int OUTPUT
AS
BEGIN
    DECLARE @VersionId int
    SET @VersionId = (SELECT Id FROM dbo.FileVersion
        WHERE FileId = @FileId AND Revision = @Revision AND Action = @Action
            AND IsText = @IsText AND IsFullText = @IsFullText AND IsRevisionBase = @IsRevisionBase
            AND (@TimeStamp IS NULL AND TimeStamp IS NULL OR TimeStamp = @TimeStamp)
            AND (@Text IS NULL AND Text IS NULL OR Text = @Text))
    
    IF @VersionId IS NOT NULL
    BEGIN
       SET @result = @VersionId
       RETURN
    END
    INSERT INTO dbo.FileVersion (FileId, Revision, Action, TimeStamp, IsText, IsFullText, IsRevisionBase, Text)
        VALUES(@FileId, @Revision, @Action, @TimeStamp, @IsText, @IsFullText, @IsRevisionBase, @Text)
    SET @result = @@IDENTITY
END
