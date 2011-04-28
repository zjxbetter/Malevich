ALTER TABLE [dbo].[Review]
    ADD CONSTRAINT [FK_Review_ChangeList] FOREIGN KEY ([ChangeListId]) REFERENCES [dbo].[ChangeList] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
