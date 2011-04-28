ALTER TABLE [dbo].[MailReview]
    ADD CONSTRAINT [FK_MailReview_Review] FOREIGN KEY ([ReviewId]) REFERENCES [dbo].[Review] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
