USE [master]
IF NOT EXISTS(SELECT * FROM [master].[dbo].[syslogins] WHERE loginname = 'NT AUTHORITY\Authenticated Users')
    CREATE LOGIN [NT AUTHORITY\Authenticated Users] FROM WINDOWS WITH DEFAULT_DATABASE=[CodeReview]

USE [CodeReview]
IF NOT EXISTS(SELECT * FROM sys.database_principals WHERE name = 'CodeReviewUser')
BEGIN
    CREATE USER [CodeReviewUser] FOR LOGIN [NT AUTHORITY\Authenticated Users]

	GRANT EXECUTE ON [dbo].[AddAttachment] To [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddChangeList] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddComment] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddFile] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddReview] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddReviewer] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddReviewRequest] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[AddVersion] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[DeleteChangeList] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[DeleteComment] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[RemoveFile] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[RenameChangeList] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[ReopenChangeList] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[SetUserContext] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[SubmitChangeList] TO [CodeReviewUser]
	GRANT EXECUTE ON [dbo].[SubmitReview] TO [CodeReviewUser]

	GRANT SELECT ON [dbo].[Attachment] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[AuditRecord] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[ChangeFile] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[ChangeList] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[Comment] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[FileVersion] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[MailChangeList] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[MailReview] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[Review] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[Reviewer] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[SourceControl] TO [CodeReviewUser]
	GRANT SELECT ON [dbo].[UserContext] TO [CodeReviewUser]
END

IF NOT EXISTS (SELECT * FROM [dbo].[SourceControl])
BEGIN
   INSERT INTO [dbo].[SourceControl] (TYPE, SERVER, CLIENT, DESCRIPTION)
       VALUES(1, NULL, NULL, 'Placeholder source control system')
END

ALTER DATABASE CodeReview
SET ALLOW_SNAPSHOT_ISOLATION ON
