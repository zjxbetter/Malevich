CREATE TABLE [dbo].[Review]
(
    [Id]            INT             IDENTITY (1, 1) NOT NULL,
    [ChangeListId]  INT             NOT NULL,
    [UserName]      NVARCHAR (50)   NOT NULL,
    [TimeStamp]     DATETIME        NOT NULL,
    [IsSubmitted]   BIT             NOT NULL,
    [OverallStatus] TINYINT         NOT NULL,
    [CommentText]   NVARCHAR (2048) NULL
)
