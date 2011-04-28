CREATE TABLE [dbo].[Attachment]
(
    [Id]                INT             IDENTITY(1, 1) NOT NULL, 
    [ChangeListId]      INT             NOT NULL,
    [TimeStamp]         DATETIME        NOT NULL,
    [Description]       NVARCHAR (128)  NULL,
    [Link]              NVARCHAR (MAX)  NOT NULL
)
