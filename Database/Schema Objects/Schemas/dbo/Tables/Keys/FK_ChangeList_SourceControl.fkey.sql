ALTER TABLE [dbo].[ChangeList]
    ADD CONSTRAINT [FK_ChangeList_SourceControl] FOREIGN KEY ([SourceControlId]) REFERENCES [dbo].[SourceControl] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

