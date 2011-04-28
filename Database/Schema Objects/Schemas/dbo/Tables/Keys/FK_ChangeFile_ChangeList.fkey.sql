ALTER TABLE [dbo].[ChangeFile]
    ADD CONSTRAINT [FK_ChangeFile_ChangeList] FOREIGN KEY ([ChangeListId]) REFERENCES [dbo].[ChangeList] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

