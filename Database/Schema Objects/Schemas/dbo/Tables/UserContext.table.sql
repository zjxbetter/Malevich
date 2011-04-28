CREATE TABLE [dbo].[UserContext] (
    [Id]            INT           IDENTITY (1, 1) NOT NULL,
    [UserName]      NVARCHAR (50) NOT NULL,
    [KeyName]       NVARCHAR (50) NOT NULL,
    [Value]         NVARCHAR (MAX)
);
