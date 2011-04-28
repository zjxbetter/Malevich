ALTER TABLE [dbo].[MailChangeList]
    ADD CONSTRAINT [FK_MailChangeList_Reviewer] FOREIGN KEY ([ReviewerId]) REFERENCES [dbo].[Reviewer] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
