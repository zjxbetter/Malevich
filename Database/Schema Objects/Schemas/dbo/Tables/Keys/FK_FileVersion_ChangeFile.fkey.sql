ALTER TABLE [dbo].[FileVersion]
    ADD CONSTRAINT [FK_FileVersion_ChangeFile] FOREIGN KEY ([FileId]) REFERENCES [dbo].[ChangeFile] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION;

