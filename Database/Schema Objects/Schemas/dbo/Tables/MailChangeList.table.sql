CREATE TABLE [dbo].[MailChangeList]
(
    [Id]            INT           IDENTITY (1, 1) NOT NULL,
    [ReviewerId]    INT           NOT NULL,
    [ChangeListId]  INT           NOT NULL
)
