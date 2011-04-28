ALTER TABLE [dbo].[Reviewer]
    ADD CONSTRAINT [FK_Reviewer_ChangeList] FOREIGN KEY ([ChangeListId]) REFERENCES [dbo].[ChangeList] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

