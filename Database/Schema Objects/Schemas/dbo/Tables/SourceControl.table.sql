CREATE TABLE [dbo].[SourceControl] (
    [Id]          INT            IDENTITY (1, 1) NOT NULL,
    [Type]        INT            NOT NULL,
    [Server]      NVARCHAR (50)  NULL,
    [Client]      NVARCHAR (50)  NULL,
    [Description] NVARCHAR (256) NULL,
    [WebsiteName] NVARCHAR (50)  NULL
);

