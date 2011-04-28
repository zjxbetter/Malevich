ALTER TABLE [dbo].[MailReviewRequest]
    ADD CONSTRAINT [FK_MailReviewRequest_ChangeList] FOREIGN KEY ([ChangeListId]) REFERENCES [dbo].[ChangeList] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
