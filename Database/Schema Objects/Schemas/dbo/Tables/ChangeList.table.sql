CREATE TABLE [dbo].[ChangeList] (
    [Id]              INT            IDENTITY (1, 1) NOT NULL,
    [SourceControlId] INT            NOT NULL,
    [UserName]        NVARCHAR (50)  NOT NULL,
    [UserClient]      NVARCHAR (50)  NOT NULL,
    [CL]              NVARCHAR (128) NOT NULL,
    [Description]     NVARCHAR (MAX) NOT NULL,
    [TimeStamp]       DATETIME       NOT NULL,
    [Stage]           INT            NOT NULL
);

