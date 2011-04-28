CREATE TABLE [dbo].[MailReviewRequest]
(
    [Id]            INT           IDENTITY (1, 1) NOT NULL,
    [ReviewerAlias] NVARCHAR(50)  NOT NULL,
    [ChangeListId]  INT           NOT NULL
)
