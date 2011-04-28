//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

using DataModel;
using SourceControl;
using Malevich.Extensions;

namespace review
{
    /// <summary>
    /// An encapsulation for the main functionality of the review submission tool.
    /// </summary>
    sealed class Program
    {
        /// <summary>
        /// This is purely utility class for one and only one goal - to store temp results of the
        /// version quert. This is here onl;y because anonymous datetime gets non-nullable DateTime in query, but
        /// cannot declare it in non-nullable way.
        /// </summary>
        private sealed class ShortVersion
        {
            // The action, as in the database.
            public int Action;

            // The time stamp. Conditional, since it can be null.
            public DateTime? TimeStamp;

            // Whether this is the base revision.
            public bool IsRevisionBase;

            /// <summary>
            /// Trivial constructor.
            /// </summary>
            /// <param name="action"> The action. </param>
            /// <param name="timeStamp"> The time stamp. </param>
            /// <param name="isRevisionBase"> Whether this is the base revision. </param>
            public ShortVersion(int action, DateTime? timeStamp, bool isRevisionBase)
            {
                Action = action;
                TimeStamp = timeStamp;
                IsRevisionBase = isRevisionBase;
            }
        }

        /// <summary>
        /// Maximum number of integrated files before we ask for confirmation.
        /// </summary>
        private const int MaximumIntegratedFiles = 50;

        /// <summary>
        /// The default id of our source control record.
        /// </summary>
        static int DefaultSourceControlInstanceId = 1;

        /// <summary>
        /// Compares two text strings that are version/file bodies. They should either be both null or 
        /// both not null and equal strings for the function to return true.
        /// </summary>
        /// <param name="s1"> First string or null. </param>
        /// <param name="s2"> Second string or null. </param>
        /// <returns> Either both nulls, or both equal strings. </returns>
        private static bool BodiesEqual(string s1, string s2)
        {
            return s1 == null ? s2 == null : s1.Equals(s2);
        }

        /// <summary>
        /// Ensures that the diffs in files can in fact be parsed by Malevich. If non-graphical characters or
        /// incorrect (mixed: Unix + Windows, or Windows + Mac) line endings are present, this throws "sd diff"
        /// off and it produces the results that we will not be able to read. This checks that this does not occur.
        /// </summary>
        /// <param name="change"> Change List. </param>
        /// <returns> True if the differ is intact. </returns>
        private static bool VerifyDiffIntegrity(Change change)
        {
            Regex diffDecoder = new Regex(@"^([0-9]+)(,[0-9]+)?([a,d,c]).*$");
            bool result = true;
            foreach (SourceControl.ChangeFile file in change.Files)
            {
                if (file.Data == null ||
                    (file.Action != SourceControl.ChangeFile.SourceControlAction.EDIT &&
                    file.Action != SourceControl.ChangeFile.SourceControlAction.INTEGRATE))
                    continue;

                StringReader reader = new StringReader(file.Data);
                for (; ; )
                {
                    string line = reader.ReadLine();
                    if (line == null)
                        break;

                    if (line.StartsWith("> ") || line.StartsWith("< ") || line.Equals("---") ||
                        line.Equals("\\ No newline at end of file") || diffDecoder.IsMatch(line))
                        continue;

                    Console.WriteLine("Cannot parse the difference report for {0}.",
                        file.LocalOrServerFileName);
                    Console.WriteLine("{0}", file.Data);
                    Console.WriteLine();
                    result = false;
                    break;
                }
            }

            if (!result)
            {
                Console.WriteLine();
                Console.WriteLine("Found problems processing the file differences in the change list.");
                Console.WriteLine();
                Console.WriteLine("This is typically caused by incorrect or mixed end of line markers, or other");
                Console.WriteLine("non-graphical characters that your source control system could not process.");
                Console.WriteLine();
                Console.WriteLine("Please fix the files in question and resubmit the change.");
            }

            return result;
        }

        private static Tuple<string, string> GetWindowsDomainAndUserNameOrDefault(Tuple<string, string> defaultValue = null)
        {
            var iden = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (iden == null)
            {
                if (defaultValue != null)
                    return defaultValue;
                else
                    throw new Exception("No windows credentials available.");
            }
            var res = iden.Name.Split(new char[] { '\\' });
            return new Tuple<string, string>(res[0], res[1]);
        }

        /// <summary>
        /// Indicates if the file is in the change, the database, or both.
        /// </summary>
        [Flags]
        private enum FileExistsIn
        {
            Neither         = 0,
            Change          = 1,
            Database        = 2,
            Both            = 3,
        }

        private static string NormalizeLineEndings(string str)
        {
            if (str == null)
                return str;

            str = Regex.Replace(str, "\\r\\n", "\n", RegexOptions.Multiline);
            //str = Regex.Replace(str, "\\n", "\r\n", RegexOptions.Multiline);
            return str;
        }

        /// <summary>
        /// Main driver for the code review submission tool.
        /// </summary>
        /// <param name="context"> The database context. </param>
        /// <param name="sd"> Source control client. </param>
        /// <param name="sourceControlInstanceId"> The ID of source control instance to use.
        ///     This is an ID of a record in the database that is unique for a given CL namespace.</param>
        /// <param name="changeList"> CL. </param>
        /// <param name="reviewers"> The list of people to who to send the code review request. </param>
        /// <param name="invitees"> The list of people who are invited to participate in the code review
        /// (but need to positively acknowledge the invitation by choosing to review the code). </param>
        /// <param name="link"> Optionally, a link to a file or a web page to be displayed in CL page. </param>
        /// <param name="linkDescr"> An optional description of the said link. </param>
        /// <param name="description"> An optional description of the changelist, overrides any description
        ///                             from the source control tool. </param>
        /// <param name="bugTracker">The server for accessing bugs.</param>
        /// <param name="bugIds">List of bugs to associate with review page.</param>
        /// <param name="force"> If branched files are included, confirms the submission even if there
        /// are too many files. </param>
        /// <param name="includeBranchedFiles"> </param>
        /// <param name="preview">If true, do not commit changes.</param>
        private static void ProcessCodeReview(
            string databaseServer,
            ISourceControl sd,
            int sourceControlInstanceId,
            string changeList,
            List<string> reviewers,
            List<string> invitees,
            string link,
            string linkDescr,
            string description,
            IBugServer bugServer,
            List<string> bugIds,
            bool force,
            bool includeBranchedFiles,
            bool preview,
            string impersonateUserName)
        {
            Change change = sd.GetChange(changeList, includeBranchedFiles);
            if (change == null)
                return;

            changeList = change.ChangeListFriendlyName ?? changeList;

            if (change == null)
                return;

            if (includeBranchedFiles && !force)
            {
                int branchedFiles = 0;
                foreach (SourceControl.ChangeFile file in change.Files)
                {
                    if (file.IsText && (file.Action == SourceControl.ChangeFile.SourceControlAction.BRANCH ||
                        file.Action == SourceControl.ChangeFile.SourceControlAction.INTEGRATE))
                        ++branchedFiles;
                }

                if (branchedFiles > MaximumIntegratedFiles)
                {
                    Console.WriteLine("There are {0} branched/integrated files in this change.", branchedFiles);
                    Console.WriteLine("Including the full text of so many files in review may increase the size");
                    Console.WriteLine("of the review database considerably.");
                    Console.Write("Are you sure you want to proceed (Yes/No)? ");
                    string response = Console.ReadLine();
                    Console.WriteLine("NOTE: In the future you can override this check by specifying --force");
                    Console.WriteLine("on the command line.");
                    if (response[0] != 'y' && response[0] != 'Y')
                        return;
                }
            }

            if (!VerifyDiffIntegrity(change))
                return;

            CodeReviewDataContext context = new CodeReviewDataContext("Data Source=" + databaseServer +
                ";Initial Catalog=CodeReview;Integrated Security=True");

            var existingReviewQuery = from rv in context.ChangeLists
                                      where rv.CL == changeList && rv.SourceControlId == sourceControlInstanceId
                                      select rv;

            // is this a new review, or a refresh of an existing one?
            bool isNewReview = (existingReviewQuery.Count() == 0);

            int? changeId = null;
            if (description == null)
                description = change.Description;

            context.Connection.Open();
            using (context.Connection)
            using (context.Transaction = context.Connection.BeginTransaction(System.Data.IsolationLevel.Snapshot))
            {
                // This more like "GetOrAddChangeList", as it returns the id of any pre-existing changelist
                // matching 'changeList'.
                if (impersonateUserName == null)
                {
                    context.AddChangeList(
                        sourceControlInstanceId, change.SdClientName, changeList, description,
                        change.TimeStamp.ToUniversalTime(), ref changeId);
                }
                else
                {
                    var changeListDb = (from c in context.ChangeLists
                                        where c.SourceControlId == sourceControlInstanceId &&
                                              c.UserName == impersonateUserName &&
                                              c.UserClient == change.SdClientName &&
                                              c.CL == changeList
                                        select c).FirstOrDefault();

                    if (changeListDb == null)
                    {
                        changeListDb = new ChangeList()
                        {
                            SourceControlId = sourceControlInstanceId,
                            UserName = impersonateUserName,
                            UserClient = change.SdClientName,
                            CL = changeList,
                            Description = description,
                            TimeStamp = change.TimeStamp.ToUniversalTime(),
                            Stage = 0
                        };

                        context.ChangeLists.InsertOnSubmit(changeListDb);
                        context.SubmitChanges(); // Not actually submitted until transaction completes.
                    }

                    changeId = changeListDb.Id;
                }

                // Get the list of files corresponding to this changelist already on the server.
                var dbChangeFiles = (from fl in context.ChangeFiles
                                     where fl.ChangeListId == changeId && fl.IsActive
                                     select fl)
                                     .OrderBy(file => file.ServerFileName)
                                     .GetEnumerator();

                var inChangeFiles = (from fl in change.Files
                                     select fl)
                                     .OrderBy(file => file.ServerFileName)
                                     .GetEnumerator();

                bool dbChangeFilesValid = dbChangeFiles.MoveNext();
                bool inChangeFilesValid = inChangeFiles.MoveNext();

                // Uses bitwise OR to ensure that both MoveNext methods are invoked.
                FileExistsIn existsIn = FileExistsIn.Neither;
                while (dbChangeFilesValid || inChangeFilesValid)
                {
                    int comp;
                    if (!dbChangeFilesValid) // No more files in database
                        comp = 1;
                    else if (!inChangeFilesValid) // No more files in change list.
                        comp = -1;
                    else
                        comp = string.Compare(dbChangeFiles.Current.ServerFileName,
                                              inChangeFiles.Current.ServerFileName);

                    if (comp < 0) // We have a file in DB, but not in source control. Delete it from DB.
                    {
                        Console.WriteLine("File {0} has been dropped from the change list.",
                                          dbChangeFiles.Current.ServerFileName);
                        context.RemoveFile(dbChangeFiles.Current.Id);

                        dbChangeFilesValid = dbChangeFiles.MoveNext();
                        existsIn = FileExistsIn.Database;
                        continue;
                    }

                    SourceControl.ChangeFile file = inChangeFiles.Current;

                    int? fid = null;
                    if (comp > 0) // File in source control, but not in DB
                    {
                        Console.WriteLine("Adding file {0}", file.ServerFileName);
                        context.AddFile(changeId, file.LocalFileName, file.ServerFileName, ref fid);
                        existsIn = FileExistsIn.Change;
                    }
                    else // Both files are here. Need to check the versions.
                    {
                        fid = dbChangeFiles.Current.Id;
                        existsIn = FileExistsIn.Both;
                    }

                    bool haveBase = (from bv in context.FileVersions
                                     where bv.FileId == fid && bv.Revision == file.Revision && bv.IsRevisionBase
                                     select bv).Count() > 0;

                    var versionQuery = from fv in context.FileVersions
                                       where fv.FileId == fid && fv.Revision == file.Revision
                                       orderby fv.Id descending
                                       select fv;

                    var version = versionQuery.FirstOrDefault();
                    bool haveVersion = false;
                    if (version != null && version.Action == (int)file.Action &&
                        BodiesEqual(NormalizeLineEndings(file.Data), NormalizeLineEndings(version.Text)))
                        haveVersion = true;

                    int? vid = null;
                    if (!haveBase && file.IsText &&
                        (file.Action == SourceControl.ChangeFile.SourceControlAction.EDIT ||
                         (file.Action == SourceControl.ChangeFile.SourceControlAction.INTEGRATE &&
                          includeBranchedFiles)))
                    {
                        string fileBody;
                        DateTime? dateTime;
                        fileBody = sd.GetFile(
                            file.OriginalServerFileName == null ? file.ServerFileName : file.OriginalServerFileName,
                            file.Revision, out dateTime);
                        if (fileBody == null)
                        {
                            Console.WriteLine("ERROR: Could not retrieve {0}#{1}", file.ServerFileName, file.Revision);
                            return;
                        }

                        Console.WriteLine("Adding base revision for {0}#{1}", file.ServerFileName, file.Revision);
                        context.AddVersion(fid, file.Revision, (int)file.Action, dateTime, true, true, true, fileBody,
                            ref vid);
                    }
                    else
                    {
                        // Do this so we print the right thing.
                        haveBase = true;
                    }

                    if (!haveVersion)
                    {
                        if (file.Action == SourceControl.ChangeFile.SourceControlAction.DELETE)
                        {
                            context.AddVersion(
                                fid, file.Revision, (int)file.Action, null, file.IsText, false, false, null, ref vid);
                        }
                        else if ((file.Action == SourceControl.ChangeFile.SourceControlAction.RENAME) || !file.IsText)
                        {
                            context.AddVersion(fid, file.Revision, (int)file.Action, file.LastModifiedTime, file.IsText,
                                false, false, null, ref vid);
                        }
                        else if (file.Action == SourceControl.ChangeFile.SourceControlAction.ADD ||
                            file.Action == SourceControl.ChangeFile.SourceControlAction.BRANCH)
                        {
                            context.AddVersion(fid, file.Revision, (int)file.Action, file.LastModifiedTime, file.IsText,
                                true, false, file.Data, ref vid);
                        }
                        else if (file.Action == SourceControl.ChangeFile.SourceControlAction.EDIT ||
                            file.Action == SourceControl.ChangeFile.SourceControlAction.INTEGRATE)
                        {
                            context.AddVersion(fid, file.Revision, (int)file.Action, file.LastModifiedTime, file.IsText,
                                false, false, file.Data, ref vid);
                        }

                        string textFlag = file.IsText ? "text" : "binary";
                        string action;
                        switch (file.Action)
                        {
                            case SourceControl.ChangeFile.SourceControlAction.ADD:
                                action = "add";
                                break;

                            case SourceControl.ChangeFile.SourceControlAction.EDIT:
                                action = "edit";
                                break;

                            case SourceControl.ChangeFile.SourceControlAction.DELETE:
                                action = "delete";
                                break;

                            case SourceControl.ChangeFile.SourceControlAction.BRANCH:
                                action = "branch";
                                break;

                            case SourceControl.ChangeFile.SourceControlAction.INTEGRATE:
                                action = "integrate";
                                break;

                            case SourceControl.ChangeFile.SourceControlAction.RENAME:
                                action = "rename";
                                break;

                            default:
                                action = "unknown";
                                break;
                        }

                        if (version != null && vid == version.Id)
                        {
                            // The file was already there. This happens sometimes because SQL rountrip (to database
                            // and back) is not an identity: somtimes the non-graphical characters change depending
                            // on the database code page. But if the database has returned a number, and this number
                            // is the same as the previous version id, we know that the file has not really been added.
                            haveVersion = true;
                        }
                        else
                        {
                            Console.WriteLine("Added version for {0}#{1}({2}, {3})", file.ServerFileName, file.Revision,
                                textFlag, action);
                        }
                    }

                    if (haveBase && haveVersion)
                        Console.WriteLine("{0} already exists in the database.", file.ServerFileName);

                    if ((existsIn & FileExistsIn.Database) == FileExistsIn.Database)
                        dbChangeFilesValid = dbChangeFiles.MoveNext();
                    if ((existsIn & FileExistsIn.Change) == FileExistsIn.Change)
                        inChangeFilesValid = inChangeFiles.MoveNext();

                    existsIn = FileExistsIn.Neither;
                }

                foreach (string reviewer in reviewers)
                {
                    int? reviewId = null;
                    context.AddReviewer(reviewer, changeId.Value, ref reviewId);
                }

                foreach (string invitee in invitees)
                {
                    context.AddReviewRequest(changeId.Value, invitee);
                }

                if (link != null)
                {
                    int? attachmentId = null;
                    context.AddAttachment(changeId.Value, linkDescr, link, ref attachmentId);
                }

                if (preview)
                    context.Transaction.Rollback();
                else
                    context.Transaction.Commit();
            }

            var reviewSiteUrl = Environment.GetEnvironmentVariable("REVIEW_SITE_URL");
            if (reviewSiteUrl != null)
            {
                var reviewPage = reviewSiteUrl;
                if (!reviewPage.EndsWith("/"))
                    reviewPage += "/";
                reviewPage += @"default.aspx?cid=" + changeId.ToString();
                Console.WriteLine("Change {0} is ready for review, and may be viewed at", changeList);
                Console.WriteLine("   {0}", reviewPage);

                var allBugIds = Enumerable.Union(change.BugIds, bugIds);
                if (bugServer != null && allBugIds.Count() > 0)
                {
                    Console.WriteLine("Connecting to TFS Work Item Server");
                    if (bugServer.Connect())
                    {
                        foreach (var bugId in allBugIds)
                        {
                            var bug = bugServer.GetBug(bugId);
                            if (bug.AddLink(new Uri(reviewPage), null))
                                Console.WriteLine("Bug {0} has been linked to review page.", bugId);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("Change {0} is ready for review.", changeList);
            if (isNewReview)
            {
                if (reviewers.Count == 0 && invitees.Count == 0)
                {
                    Console.WriteLine("Note: no reviewers specified. You can add them later using this utility.");
                }
                else
                {
                    Console.WriteLine("If the mail notifier is enabled, the reviewers will shortly receive mail");
                    Console.WriteLine("asking them to review your changes.");
                }
            }
            else
            {
                Console.WriteLine("Note: existing reviewers will not be immediately informed of this update.");
                Console.WriteLine("To ask them to re-review your updated changes, you can visit the review website");
                Console.WriteLine("and submit a response.");
            }
            }

            if (preview)
                Console.WriteLine("In preview mode -- no actual changes committed.");
        }

        /// <summary>
        /// Verifies that there are no pending reviews for this change list.
        /// </summary>
        /// <param name="context"> Database context. </param>
        /// <param name="cid"> Change list ID. </param>
        /// <returns></returns>
        private static bool NoPendingReviews(CodeReviewDataContext context, int cid)
        {
            var unsubmittedReviewsQuery = from rr in context.Reviews
                                          where rr.ChangeListId == cid && !rr.IsSubmitted
                                          select rr;

            bool printedTitle = false;
            int unsubmittedComments = 0;

            foreach (Review review in unsubmittedReviewsQuery)
            {
                int comments = (from cc in context.Comments where cc.ReviewId == review.Id select cc).Count();
                if (comments == 0)
                    continue;

                unsubmittedComments += comments;

                if (!printedTitle)
                {
                    printedTitle = true;
                    Console.WriteLine("Reviews are still pending for this change list:");
                }

                Console.WriteLine("{0} : {1} comments.", review.UserName, comments);
            }

            return unsubmittedComments == 0;
        }

        /// <summary>
        /// Marks review as closed.
        /// </summary>
        /// <param name="context"> Database context. </param>
        /// <param name="userName"> User alias. </param>
        /// <param name="sourceControlId"> Source control ID. </param>
        /// <param name="cl"> Review number (source control side). </param>
        /// <param name="force"> Whether to force closing the review even if there are pending changes. </param>
        /// <param name="admin"> Close the review in admin mode, regardless of the user. </param>
        private static void MarkChangeListAsClosed(CodeReviewDataContext context, string userName,
            int sourceControlId, string cl, bool force, bool admin)
        {
            var cid = (from ch in context.ChangeLists
                       where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl) &&
                             ch.Stage == 0 && (admin || ch.UserName.Equals(userName))
                       select ch.Id).FirstOrDefault();

            if (cid == null)
            {
                Console.WriteLine("No active change in database.");
                return;
            }

            if (force || NoPendingReviews(context, cid))
            {
                context.SubmitChangeList(cid);
                Console.WriteLine("{0} closed. Use 'review reopen {0}' to reopen.", cl);
            }
            else
            {
                Console.WriteLine("Pending review detected. If you want to close this change list");
                Console.WriteLine("anyway, use the --force.");
            }
        }

        /// <summary>
        /// Marks review as deleted.
        /// </summary>
        /// <param name="context"> Database context. </param>
        /// <param name="userName"> User alias. </param>
        /// <param name="sourceControlId"> Source control ID. </param>
        /// <param name="cl"> Review number (source control side). </param>
        /// <param name="force"> Whether to force closing the review even if there are pending changes. </param>
        /// <param name="admin"> Close the review in admin mode, regardless of the user. </param>
        private static void DeleteChangeList(CodeReviewDataContext context, string userName,
            int sourceControlId, string cl, bool force, bool admin)
        {
            int[] cids = admin ?
                (from ch in context.ChangeLists
                 where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl) && ch.Stage == 0
                 select ch.Id).ToArray() :
                (from ch in context.ChangeLists
                 where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl) &&
                       ch.UserName.Equals(userName) && ch.Stage == 0
                 select ch.Id).ToArray();

            if (cids.Length != 1)
            {
                Console.WriteLine("No active change in database.");
                return;
            }

            if (force || NoPendingReviews(context, cids[0]))
            {
                context.DeleteChangeList(cids[0]);
                Console.WriteLine("{0} deleted. Use 'review reopen {0}' to undelete.", cl);
            }
            else
            {
                Console.WriteLine("Pending review detected. If you want to delete this change list");
                Console.WriteLine("anyway, use the --force.");
            }
        }

        /// <summary>
        /// Renames a change list.
        /// </summary>
        /// <param name="context"> Database context. </param>
        /// <param name="userName"> User alias. </param>
        /// <param name="sourceControlId"> Source control ID. </param>
        /// <param name="cl"> Review number (source control side). </param>
        /// <param name="newCl"> New name for the change list. </param>
        /// <param name="admin"> Close the review in admin mode, regardless of the user. </param>
        private static void RenameChangeList(CodeReviewDataContext context, string userName,
            int sourceControlId, string cl, string newCl, bool admin)
        {
            int[] cids = admin ?
                (from ch in context.ChangeLists
                 where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl)
                 select ch.Id).ToArray() :
                (from ch in context.ChangeLists
                 where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl) && ch.UserName.Equals(userName)
                 select ch.Id).ToArray();

            if (cids.Length != 1)
            {
                Console.WriteLine("No active change in database.");
                return;
            }

            context.RenameChangeList(cids[0], newCl);
            Console.WriteLine("{0} renamed to {1}.", cl, newCl);
        }

        /// <summary>
        /// Reopens a change list.
        /// </summary>
        /// <param name="context"> Database context. </param>
        /// <param name="userName"> User alias. </param>
        /// <param name="sourceControlId"> Source control ID. </param>
        /// <param name="cl"> Review number (source control side). </param>
        /// <param name="admin"> Close the review in admin mode, regardless of the user. </param>
        private static void ReopenChangeList(CodeReviewDataContext context, string userName,
            int sourceControlId, string cl, bool admin)
        {
            int[] cids = admin ?
                (from ch in context.ChangeLists
                 where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl) && ch.Stage != 0
                 select ch.Id).ToArray() :
                (from ch in context.ChangeLists
                 where ch.SourceControlId == sourceControlId && ch.CL.Equals(cl) &&
                       ch.UserName.Equals(userName) && ch.Stage != 0
                 select ch.Id).ToArray();

            if (cids.Length != 1)
            {
                Console.WriteLine("No inactive change in database.");
                return;
            }

            context.ReopenChangeList(cids[0]);
            Console.WriteLine("{0} reopened.", cl);
        }

        /// <summary>
        /// Adds an attachment to code review.
        /// </summary>
        /// <param name="context"> Database context. </param>
        /// <param name="userName"> User alias. </param>
        /// <param name="cl"> Change list to modify. </param>
        /// <param name="link"> The file or web page URL. </param>
        /// <param name="linkDescr"> The text (optional). </param>
        private static void AddAttachment(CodeReviewDataContext context, string userName, string cl,
            string link, string linkDescr)
        {
            var changeListQuery = from ch in context.ChangeLists
                                  where ch.CL.Equals(cl) && ch.UserName.Equals(userName) && ch.Stage == 0
                                  select ch.Id;
            if (changeListQuery.Count() != 1)
            {
                Console.WriteLine("No active change in database.");
                return;
            }

            int cid = changeListQuery.Single();

            int? result = null;
            context.AddAttachment(cid, linkDescr, link, ref result);

            Console.WriteLine("Attachment submitted.");
        }

        /// <summary>
        /// Processes a string that is a user name or a comma or a semicolon separated array of users.
        /// Adds them to the list. This is used to add reviewers and invitees.
        /// </summary>
        /// <param name="users"> List of users. </param>
        /// <param name="user"> A user name or a comma or semicolon separated list of user names, without domain.
        /// </param>
        /// <returns> false if there was a syntax error (user name contains invalid characters). </returns>
        private static bool AddReviewers(List<string> users, string user)
        {
            string[] usernames = user.Split(',', ';');
            foreach (string u in usernames)
            {
                try
                {
                    new MailAddress(u + "@testdomain.com");
                }
                catch (FormatException)
                {
                    Console.WriteLine("{0} is not a valid alias!", u);
                    return false;
                }

                users.Add(u);
            }

            return true;
        }

        /// <summary>
        /// Displays the help.
        /// </summary>
        private static void DisplayHelp()
        {
            Console.WriteLine(
@"This tool starts or updates an existing code review request.
Usage:
    review [--sourcecontrol TFS|P4|SD|SVN]
           [--port|--server source_control_server]
           [--client|--workspace source_control_client|workspace_name]
           [--user source_control_user_name]
           [--password source_control_password]
           [--clientexe path_to_source_control_client.exe]
           [--tfsdiffsource local|shelf]
           [--database server/instance]
           [--instance sourceControlInstanceId]
           [--link URL]
           [--linkdescr ""description string""]
           [--description ""description string""]
           [--bugid <bug id>]
           [[--force] --includebranchedfiles] (*)
           [--preview]
           [--impersonate user_name]
           change_list (reviewer_alias)*

    review [--database server/instance]
           [--instance sourceControlInstanceId]
           [--force]
           [--admin]
           close change_list

    review [--database server/instance]
           [--instance sourceControlInstanceId]
           [--force]
           [--admin]
           delete change_list

    review [--database server/instance]
           [--instance sourceControlInstanceId]
           addlink change_list URL ""description string""

    review [--database server/instance]
           [--instance sourceControlInstanceId]
           [--admin]
           rename original_change_list new_change_list

    review [--database server/instance]
           [--instance sourceControlInstanceId]
           [--admin]
           reopen change_list

Source control parameters (sourcecontrol, port, client, user, password etc)
are optional if they can be discovered from the environment. We look for
perforce and source depot configuration files and environment variables as
well as their client executables in the path.
The following environment variables are recognized:
    For Perforce: P4PORT, P4CLIENT, P4USER, P4PASSWD, P4CONFIG.
    For Source Depot: SDPORT, SDCLIENT (both in environment and in sd.ini).
    For TFS: TFSSERVER, TFSWORKSPACE, TFSWORKSPACEOWNER, TFSUSER,
             TFSPASSWORD, TFSDIFFSOURCE.
    For Subversion: SVNURL, SVNUSER, SVNPASSWORD.

Database can also be specified via REVIEW_DATABASE environment variable.
Instance is optional and is only necessary if the same server hosts more than
one source control. It can be specified via REVIEW_INSTANCE variable.
'review' command is optional, and is assumed if no other command is given.
The change must be unsubmitted. Only text files are uploaded for the review.

Reviewer aliases can be added later, but cannot be subtracted. For example:
    review 5151 alice
    review 5151 bob
executed sequentially have the same effect as
    review 5151 alice bob

If a reviewer alias is prefixed by an --invite flag, the reviewer is sent
a message that encourages him or her to join the review, but does not
add the alias to the set of reviewers yet. This is useful for distribution
lists. For example:
    review 5151 alice bob --invite reviewmonsters dave
makes alice, bob, and dave the reviewers, and also invites everyone from the
reviewmonsters distribution list to participate.
The command is idempotent - running it twice does not result in new data
being added to the system, unless the files had changed in between, in which
case the new versions are uploaded.

(TFS only) One can specify whether the files are read from the local file
or the shelf set using --tfsdiffsource command line flag or TFSDIFFSOURCE
environment variable. If the argument is local, the shelf set is used
only as a list of file names, whereas the file content comes from the
local hard drive. If the argument is shelf, the files are read from the
shelf set.

(TFS only) If the environment variable REVIEW_SITE_URL is set to the root
of the review website (e.g. http://MyServer/Malevich), then a link to the
review request will be added to any associated bugs. An associated bug is
identified through the --bugid switch and/or any woritems associated with
a shelveset. In addition, a link to the review page will be printed at the
end of a successful review create or update operation.

--link and --linkdescr allow users to attach pointers to external resources
which will be linked from the change description page. The argument to the
--link should be prefixed by the protocol, e.g. file://\\\\server\\share\\myfile,
or http://webserver/page.html. If the file is specified, it should be a UNC,
not a local name.
--linkdescr allows to supply the text for the hyperlink constructed from
the --link target. The argument for --linkdescr should be surrounded by quotation
marks: --linkdescr ""Bug # 25613""

A closed review can be reopened by running 'review' command again.

--force flag allows closing/deleting reviews with outstanding comments.

--preview allows a simulated review submission without committing any changes.

--impersonate may be used to perform an action on behalf of another use.
For example, if another user requests a code review without using Malevich,
this command may be used by a reviewee to create a Malevich review on the users'
behalf. Must be used in conjunction with the --admin switch to ensure an audit
log.

--admin flag allows closing/deleting/reopening reviews that do not belong
to the current user (an audit log message is logged on server).

(*) --includebranchedfiles flag allows you to include full text of
branched and integrated files. By default, the review system only stores the
file names, because integrations can be very big, and including the fill text
for these files could bring large amount of data into the review database.
Do not use this option lightly! If the integration is big (more than 50 files)
review.exe will ask to confirm your action. Use --force to override this check.

If review.exe fails, it is often helpful to specify --verbose flag on the
command line for extra error output.");
        }

        /// <summary>
        /// The main function. Parses arguments, and if there is enough information, calls ProcessCodeReview.
        /// </summary>
        /// <param name="args"> Arguments passed by the system. </param>
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                DisplayHelp();
                return;
            }

            // First, rummage through the environment, path, and the directories trying to detect the source
            // control system.
            string sourceControlInstance = Environment.GetEnvironmentVariable("REVIEW_INSTANCE");

            SourceControlSettings sdSettings = SourceDepotInterface.GetSettings();
            SourceControlSettings p4Settings = PerforceInterface.GetSettings();
            SourceControlSettings tfsSettings = SourceControl.Tfs.Factory.GetSettings();
            SourceControlSettings svnSettings = SubversionInterface.GetSettings();

            string databaseServer = Environment.GetEnvironmentVariable("REVIEW_DATABASE");
            string command = null;

            // Now go through the command line to get other options.
            string link = null;
            string linkDescr = null;
            string description = null;

            bool force = false;
            bool admin = false;

            string changeId = null;
            string newChangeId = null;

            bool includeBranchedFiles = false;

            List<string> reviewers = new List<string>();
            List<string> invitees = new List<string>();

            SourceControl.SourceControlType? sourceControlType = null;
            SourceControlSettings settings = new SourceControlSettings();

            string impersonatedUserName = null;

            List<string> bugIds = new List<string>();

            bool verbose = false;
            bool preview = false;

            for (int i = 0; i < args.Length; ++i)
            {
                if (i < args.Length - 1)
                {
                    if (args[i].EqualsIgnoreCase("--sourcecontrol"))
                    {
                        if ("TFS".EqualsIgnoreCase(args[i + 1]))
                        {
                            sourceControlType = SourceControl.SourceControlType.TFS;
                        }
                        else if ("P4".EqualsIgnoreCase(args[i + 1]))
                        {
                            sourceControlType = SourceControl.SourceControlType.PERFORCE;
                        }
                        else if ("SD".EqualsIgnoreCase(args[i + 1]))
                        {
                            sourceControlType = SourceControl.SourceControlType.SD;
                        }
                        else if ("SVN".EqualsIgnoreCase(args[i + 1]))
                        {
                            sourceControlType = SourceControl.SourceControlType.SUBVERSION;
                        }
                        else
                        {
                            Console.WriteLine("error : source control '{0}' is not supported.", args[i + 1]);
                            return;
                        }
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--port"))
                    {
                        settings.Port = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--server"))
                    {
                        settings.Port = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--client"))
                    {
                        settings.Client = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--workspace"))
                    {
                        settings.Client = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--tfsdiffsource"))
                    {
                        string diff = args[i + 1];
                        if (diff.EqualsIgnoreCase("local"))
                        {
                            settings.Diff = SourceControlSettings.DiffSource.Local;
                        }
                        else if (diff.EqualsIgnoreCase("shelf"))
                        {
                            settings.Diff = SourceControlSettings.DiffSource.Server;
                        }
                        else
                        {
                            Console.WriteLine(
                                "error : unrecognized value of TFS diff source. Should be either shelf or local.");
                            return;
                        }

                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--user"))
                    {
                        settings.User = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--password"))
                    {
                        settings.Password = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--clientexe"))
                    {
                        settings.ClientExe = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--database"))
                    {
                        databaseServer = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--instance"))
                    {
                        sourceControlInstance = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--invite"))
                    {
                        if (!AddReviewers(invitees, args[i + 1]))
                            return;
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--link"))
                    {
                        link = args[i + 1];
                        ++i;

                        if (!(link.StartsWith(@"file://\\") || link.StartsWith("http://") ||
                            link.StartsWith("https://")))
                        {
                            Console.WriteLine("error : incorrect link specification : should start with http://, https://, or " +
                                              "file://. If the latter is used, the supplied file name should be UNC.");
                            return;
                        }
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--linkdescr"))
                    {
                        linkDescr = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--description"))
                    {
                        description = args[i + 1];
                        ++i;
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--impersonate"))
                    {
                        impersonatedUserName = args[++i];
                        continue;
                    }

                    if (args[i].EqualsIgnoreCase("--bugid"))
                    {
                        bugIds.Add(args[++i]);
                        continue;
                    }
                }

                if (args[i].EqualsIgnoreCase("--force"))
                {
                    force = true;
                    continue;
                }

                if (args[i].EqualsIgnoreCase("--admin"))
                {
                    admin = true;
                    continue;
                }

                if (args[i].EqualsIgnoreCase("--includebranchedfiles"))
                {
                    includeBranchedFiles = true;
                    continue;
                }

                if (args[i].EqualsIgnoreCase("--verbose"))
                {
                    verbose = true;
                    continue;
                }

                if (args[i].EqualsIgnoreCase("--preview"))
                {
                    preview = true;
                    continue;
                }

                if (args[i].EqualsIgnoreCase("help") ||
                    args[i].EqualsIgnoreCase("/help") ||
                    args[i].EqualsIgnoreCase("--help") ||
                    args[i].EqualsIgnoreCase("-help") ||
                    args[i].EqualsIgnoreCase("/h") ||
                    args[i].EqualsIgnoreCase("-h") ||
                    args[i].EqualsIgnoreCase("-?") ||
                    args[i].EqualsIgnoreCase("/?"))
                {
                    DisplayHelp();
                    return;
                }

                if (args[i].StartsWith("-"))
                {
                    Console.WriteLine("error : unrecognized flag: {0}", args[i]);
                    return;
                }

                if (command == null &&
                    args[i].EqualsIgnoreCase("review") ||
                    args[i].EqualsIgnoreCase("close") ||
                    args[i].EqualsIgnoreCase("delete") ||
                    args[i].EqualsIgnoreCase("reopen") ||
                    args[i].EqualsIgnoreCase("rename") ||
                    args[i].EqualsIgnoreCase("addlink"))
                {
                    command = args[i];
                    continue;
                }

                if (changeId == null)
                {
                    changeId = args[i];
                    continue;
                }

                if ("addlink".EqualsIgnoreCase(command))
                {
                    if (link == null)
                    {
                        link = args[i];
                        continue;
                    }
                    else if (linkDescr == null)
                    {
                        linkDescr = args[i];
                        continue;
                    }
                }

                if ("rename".EqualsIgnoreCase(command))
                {
                    if (newChangeId == null)
                    {
                        newChangeId = args[i];
                        continue;
                    }
                }

                if (command == null || "review".EqualsIgnoreCase(command))
                {
                    if (!AddReviewers(reviewers, args[i]))
                        return;

                    continue;
                }

                Console.WriteLine("error : {0} is not recognized. --help for help", args[i]);
                return;
            }

            string userName = impersonatedUserName ?? Environment.GetEnvironmentVariable("USERNAME");

            if (changeId == null)
            {
                Console.WriteLine("error : change list is required. Type 'review help' for help.");
                return;
            }

            if (databaseServer == null)
            {
                Console.WriteLine("error : database server is required. Type 'review help' for help.");
                return;
            }

            if (link == null && linkDescr != null)
            {
                Console.WriteLine("error : if you supply link description, the link must also be present.");
                return;
            }

            if (impersonatedUserName != null && !admin)
            {
                Console.WriteLine("error : --impersonate may only be used in conjunction with --admin.");
                return;
            }

            int sourceControlInstanceId;
            if (!Int32.TryParse(sourceControlInstance, out sourceControlInstanceId))
                sourceControlInstanceId = DefaultSourceControlInstanceId;

            // These commands do not require source control - get them out the way first.
            if (command != null)
            {
                CodeReviewDataContext context = new CodeReviewDataContext("Data Source=" + databaseServer +
                    ";Initial Catalog=CodeReview;Integrated Security=True");

                if (command.EqualsIgnoreCase("close"))
                {
                    MarkChangeListAsClosed(context, userName, sourceControlInstanceId, changeId, force, admin);
                    return;
                }
                else if (command.EqualsIgnoreCase("delete"))
                {
                    DeleteChangeList(context, userName, sourceControlInstanceId, changeId, force, admin);
                    return;
                }
                else if (command.EqualsIgnoreCase("rename"))
                {
                    RenameChangeList(context, userName, sourceControlInstanceId, changeId, newChangeId, admin);
                    return;
                }
                else if (command.EqualsIgnoreCase("reopen"))
                {
                    ReopenChangeList(context, userName, sourceControlInstanceId, changeId, admin);
                    return;
                }
                else if (command.EqualsIgnoreCase("addlink"))
                {
                    if (link != null)
                        AddAttachment(context, userName, changeId, link, linkDescr);
                    else
                        Console.WriteLine("You need to supply the link to add.");
                    return;
                }
            }

            // If we have the client, maybe we can guess the source control...
            if (sourceControlType == null && settings.ClientExe != null)
            {
                string clientExeFile = Path.GetFileName(settings.ClientExe);
                if ("sd.exe".EqualsIgnoreCase(clientExeFile))
                    sourceControlType = SourceControl.SourceControlType.SD;
                else if ("p4.exe".EqualsIgnoreCase(clientExeFile))
                    sourceControlType = SourceControl.SourceControlType.PERFORCE;
                else if ("tf.exe".EndsWith(clientExeFile, StringComparison.InvariantCultureIgnoreCase))
                    sourceControlType = SourceControl.SourceControlType.TFS;
                else if ("svn.exe".EqualsIgnoreCase(clientExeFile))
                    sourceControlType = SourceControl.SourceControlType.SUBVERSION;
            }

            // Attempt to detect the source control system.
            if (sourceControlType == null)
            {
                if (sdSettings.Port != null && sdSettings.Client != null && sdSettings.ClientExe != null)
                    sourceControlType = SourceControl.SourceControlType.SD;
                if (p4Settings.Port != null && p4Settings.Client != null && p4Settings.ClientExe != null)
                    sourceControlType = SourceControl.SourceControlType.PERFORCE;
                if (tfsSettings.Port != null)
                    sourceControlType = SourceControl.SourceControlType.TFS;
                if (svnSettings.Port != null && svnSettings.Client != null && svnSettings.ClientExe != null)
                    sourceControlType = SourceControl.SourceControlType.SUBVERSION;

                if (sourceControlType == null)
                {
                    Console.WriteLine("Could not determine the source control system.");
                    Console.WriteLine("User 'review help' for help with specifying it.");
                    return;
                }
            }

            // If source control is explicitly specified...
            if (sourceControlType == SourceControl.SourceControlType.TFS)
            {
                if (settings.Client != null)
                    tfsSettings.Client = settings.Client;
                if (settings.Port != null)
                    tfsSettings.Port = settings.Port;
                if (settings.User != null)
                    tfsSettings.User = settings.User;
                if (settings.Password != null)
                    tfsSettings.Password = settings.Password;
                if (settings.ClientExe != null)
                    tfsSettings.ClientExe = settings.ClientExe;
                if (settings.Diff != SourceControlSettings.DiffSource.Unspecified)
                    tfsSettings.Diff = settings.Diff;

                if (tfsSettings.Port == null)
                {
                    Console.WriteLine("Could not determine tfs server. Consider specifying it on the command line or " +
                        "in the environment.");
                    return;
                }

                if (tfsSettings.Client == null && tfsSettings.Diff == SourceControlSettings.DiffSource.Local)
                {
                    Console.WriteLine("Could not determine tfs workspace. Consider specifying it on the command line " +
                        "or in the environment.");
                    return;
                }
            }

            if (sourceControlType == SourceControl.SourceControlType.PERFORCE)
            {
                if (settings.Client != null)
                    p4Settings.Client = settings.Client;
                if (settings.Port != null)
                    p4Settings.Port = settings.Port;
                if (settings.User != null)
                    p4Settings.User = settings.User;
                if (settings.Password != null)
                    p4Settings.Password = settings.Password;
                if (settings.ClientExe != null)
                    p4Settings.ClientExe = settings.ClientExe;

                if (p4Settings.ClientExe == null)
                {
                    Console.WriteLine(
                        "Could not find p4.exe. Consider putting it in the path, or on the command line.");
                    return;
                }

                if (p4Settings.Port == null)
                {
                    Console.WriteLine("Could not determine the server port. " +
                        "Consider putting it on the command line, or in p4 config file.");
                    return;
                }

                if (p4Settings.Client == null)
                {
                    Console.WriteLine("Could not determine the perforce client. " +
                        "Consider putting it on the command line, or in p4 config file.");
                    return;
                }
            }

            if (sourceControlType == SourceControl.SourceControlType.SUBVERSION)
            {
                if (settings.Client != null)
                    svnSettings.Client = settings.Client;
                if (settings.Port != null)
                    svnSettings.Port = settings.Port;
                if (settings.User != null)
                    svnSettings.User = settings.User;
                if (settings.Password != null)
                    svnSettings.Password = settings.Password;
                if (settings.ClientExe != null)
                    svnSettings.ClientExe = settings.ClientExe;

                if (svnSettings.ClientExe == null)
                {
                    Console.WriteLine(
                        "Could not find svn.exe. Consider putting it in the path, or on the command line.");
                    return;
                }

                if (svnSettings.Port == null)
                {
                    Console.WriteLine("Could not determine the server Url. " +
                        "Consider putting it on the command line.");
                    return;
                }
            }

            if (sourceControlType == SourceControl.SourceControlType.SD)
            {
                if (settings.Client != null)
                    sdSettings.Client = settings.Client;
                if (settings.Port != null)
                    sdSettings.Port = settings.Port;
                if (settings.ClientExe != null)
                    sdSettings.ClientExe = settings.ClientExe;

                if (sdSettings.ClientExe == null)
                {
                    Console.WriteLine(
                        "Could not find sd.exe. Consider putting it in the path, or on the command line.");
                    return;
                }

                if (sdSettings.Port == null)
                {
                    Console.WriteLine("Could not determine the server port. " +
                        "Consider putting it on the command line, or in sd.ini.");
                    return;
                }

                if (sdSettings.Client == null)
                {
                    Console.WriteLine("Could not determine the source depot client. " +
                        "Consider putting it on the command line, or in sd.ini.");
                    return;
                }
            }

            try
            {
                ISourceControl sourceControl;
                IBugServer bugTracker = null;
                if (sourceControlType == SourceControl.SourceControlType.SD)
                    sourceControl = SourceDepotInterface.GetInstance(sdSettings.ClientExe, sdSettings.Port,
                        sdSettings.Client, sdSettings.Proxy);
                else if (sourceControlType == SourceControl.SourceControlType.PERFORCE)
                    sourceControl = PerforceInterface.GetInstance(p4Settings.ClientExe, p4Settings.Port,
                        p4Settings.Client, p4Settings.User, p4Settings.Password);
                else if (sourceControlType == SourceControl.SourceControlType.SUBVERSION)
                    sourceControl = SubversionInterface.GetInstance(svnSettings.ClientExe, svnSettings.Port, svnSettings.Client);
                else if (sourceControlType == SourceControl.SourceControlType.TFS)
                {
                    sourceControl = SourceControl.Tfs.Factory.GetISourceControl(
                        tfsSettings.Port, tfsSettings.Client, tfsSettings.ClientOwner, tfsSettings.User,
                        tfsSettings.Password, tfsSettings.Diff == SourceControlSettings.DiffSource.Server);
                    bugTracker = SourceControl.Tfs.Factory.GetIBugServer(
                        tfsSettings.Port, tfsSettings.Client, tfsSettings.ClientOwner,
                        tfsSettings.User, tfsSettings.Password);
                }
                else
                    throw new ApplicationException("Unknown source control system.");

                if (verbose)
                {
                    ILogControl logControl = sourceControl as ILogControl;
                    if (logControl != null)
                        logControl.SetLogLevel(LogOptions.ClientUtility);
                    else
                        Console.WriteLine("Note: client log requested, but not supported by the utility.");
                }

                if (!sourceControl.Connect())
                {
                    Console.WriteLine("Failed to connect to the source control system.");
                    return;
                }

                ProcessCodeReview(databaseServer, sourceControl, sourceControlInstanceId, changeId, reviewers, invitees,
                    link, linkDescr, description, bugTracker, bugIds, force, includeBranchedFiles, preview, impersonatedUserName);

                sourceControl.Disconnect();
            }
            catch (SourceControlRuntimeError)
            {
                // The error condition has already been printed out at the site where this has been thrown.
                Console.WriteLine("Code review has not been submitted!");
            }
            catch (SqlException ex)
            {
                Console.WriteLine("Could not connect to Malevich database. This could be a temporary");
                Console.WriteLine("network problem or a misconfigured database name. Please ensure that");
                Console.WriteLine("the database ({0}) is specified correctly.", databaseServer);
                Console.WriteLine("If this is a new Malevich server installation, please ensure that");
                Console.WriteLine("SQL Server TCP/IP protocol is enabled, and its ports are open in");
                Console.WriteLine("the firewall.");
                if (verbose)
                {
                    Console.WriteLine("Exception information (please include in bug reports):");
                    Console.WriteLine("{0}", ex);
                }
                else
                {
                    Console.WriteLine("Use --verbose flag to show detailed error information.");
                }
            }
        }
    }
}
