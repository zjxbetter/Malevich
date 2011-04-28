ALTER TABLE [dbo].[MailChangeList]
    ADD CONSTRAINT [FK_MailChangeList_ChangeList] FOREIGN KEY ([ChangeListId]) REFERENCES [dbo].[ChangeList] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
