CREATE TABLE [dbo].[FileVersion] (
    [Id]                INT             IDENTITY (1, 1) NOT NULL,
    [FileId]            INT             NOT NULL,
    [Revision]          INT             NOT NULL,
    [Action]            INT             NOT NULL,
    [TimeStamp]         DATETIME        NULL,
    [IsText]            BIT             NOT NULL,
    [IsFullText]        BIT             NOT NULL,
    [IsRevisionBase]    BIT             NOT NULL,
    [Text]              VARCHAR (MAX)   NULL
);

