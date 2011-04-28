ALTER TABLE [dbo].[Attachment]
    ADD CONSTRAINT [FK_Attachment_ChangeList] FOREIGN KEY ([ChangeListId]) REFERENCES [dbo].[ChangeList] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;
