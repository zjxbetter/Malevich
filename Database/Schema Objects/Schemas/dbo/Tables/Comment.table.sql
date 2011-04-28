CREATE TABLE [dbo].[Comment] (
    [Id]            INT             IDENTITY (1, 1) NOT NULL,
    [ReviewId]      INT             NOT NULL,
    [FileVersionId] INT             NOT NULL,
    [Line]          INT             NOT NULL,
    [LineStamp]     BIGINT          NOT NULL,
    [CommentText]   NVARCHAR (2048) NOT NULL
);

