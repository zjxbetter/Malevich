ALTER TABLE [dbo].[Comment]
    ADD CONSTRAINT [FK_Comment_FileVersion] FOREIGN KEY ([FileVersionId]) REFERENCES [dbo].[FileVersion] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

