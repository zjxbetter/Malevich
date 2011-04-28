//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Services;

using DataModel;
using Malevich;
using Malevich.Extensions;

/// <summary>
/// Receives comments as they are entered by reviewers.
/// </summary>
[WebService(Namespace = "http://tempuri.org/")]
[WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
[System.Web.Script.Services.ScriptService]
public class CommentsExchange : System.Web.Services.WebService
{
    /// <summary>
    /// Parser for the comment id string. Converts Javascript id to something we can search for in the database.
    /// </summary>
    private sealed class CommentId
    {
        /// <summary>
        /// File version.
        /// </summary>
        public int FileVersionId;

        /// <summary>
        /// Line number.
        /// </summary>
        public int Line;

        /// <summary>
        /// Comment's timestamp.
        /// </summary>
        public long LineStamp;

        /// <summary>
        /// Trivial constructor. Parses the string, sets up the structure.
        /// If the format of the input string is in error, it simply leaves
        /// the structure in a coherent form, but which will never be found in the
        /// database.
        /// </summary>
        /// <param name="commentId"></param>
        public CommentId(string commentId)
        {
            FileVersionId = -1;
            Line = -1;
            LineStamp = -1;

            string[] comment = commentId.Split('_');
            if (comment.Length != 5)
                return;

            Int32.TryParse(comment[1], out FileVersionId);
            Int32.TryParse(comment[2], out Line);
            Int64.TryParse(comment[3], out LineStamp);
        }

        /// <summary>
        /// Returns true if the structure has parsed the Javascript comment id correctly.
        /// </summary>
        /// <returns></returns>
        public bool HasParsed()
        {
            return FileVersionId != -1 && Line != -1 && LineStamp != -1;
        }
    }

    /// <summary>
    /// Gets the current user's name, normalized to an alias (sans the domain name).
    /// </summary>
    /// <returns> User alias, or null if user is not authenticated. </returns>
    private string GetUserAlias()
    {
        if (!HttpContext.Current.User.Identity.IsAuthenticated)
            return null;

        string userName = HttpContext.Current.User.Identity.Name;
        int bs = userName.IndexOf('\\');
        if (bs != -1)
            userName = userName.Substring(bs + 1);

        return userName;
    }

    /// <summary>
    /// Trivial constructor. Does nothing.
    /// </summary>
    public CommentsExchange()
    {
    }

    /// <summary>
    /// Adds a comment to the database.
    /// </summary>
    /// <param name="commentId"> String that looks as follows: base|diff_versionId_line_timestamp
    ///     For example: "base_60_7_1230110786780"
    /// </param>
    /// <param name="commentText"></param>
    [WebMethod]
    public void AddComment(string commentId, string commentText)
    {
        if (!HttpContext.Current.User.Identity.IsAuthenticated)
            return;

        CommentId cid = new CommentId(commentId);
        if (!cid.HasParsed())
            return;

        int? result = null;
        CodeReviewDataContext dataContext = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);
        dataContext.AddComment(cid.FileVersionId, cid.Line, cid.LineStamp, commentText, ref result);
        dataContext.Connection.Close();
        dataContext.Dispose();
    }

    /// <summary>
    /// Removes a comment from the database.
    /// </summary>
    /// <param name="commentId"> String that looks as follows: base|diff_fileId_line_timestamp
    ///     For example: "base_60_7_1230110786780"
    /// </param>
    [WebMethod]
    public void DeleteComment(string commentId)
    {
        if (!HttpContext.Current.User.Identity.IsAuthenticated)
            return;

        CommentId cid = new CommentId(commentId);

        CodeReviewDataContext dataContext = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);
        var commentQuery = from cm in dataContext.Comments
                           where cm.FileVersionId == cid.FileVersionId && cm.Line == cid.Line &&
                               cm.LineStamp == cid.LineStamp
                           select cm.Id;

        if (commentQuery.Count() == 1)
        {
            int id = commentQuery.Single();
            dataContext.DeleteComment(id);
        }
        dataContext.Connection.Close();
        dataContext.Dispose();
    }

    /// <summary>
    /// Queries the number of outstanding code reviews where the user is a reviewer.
    /// </summary>
    [WebMethod]
    public int GetNumberOfReviewsWhereIAmAReviewer()
    {
        string alias = GetUserAlias();
        if (alias == null)
            return 0;

        CodeReviewDataContext dataContext = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);

        int result = (from cc in dataContext.ChangeLists
                      join rr in dataContext.Reviewers on cc.Id equals rr.ChangeListId
                      where rr.ReviewerAlias == alias && cc.Stage == 0
                      select cc).Distinct().Count();

        dataContext.Connection.Close();
        dataContext.Dispose();

        return result;
    }

    /// <summary>
    /// Queries the number of outstanding code reviews where the user is a reviewee.
    /// </summary>
    [WebMethod]
    public int GetNumberOfReviewsWhereIAmTheReviewee()
    {
        string alias = GetUserAlias();
        if (alias == null)
            return 0;

        CodeReviewDataContext dataContext = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);

        int result = (from cc in dataContext.ChangeLists
                      where cc.UserName == alias && cc.Stage == 0
                      select cc).Distinct().Count();

        dataContext.Connection.Close();
        dataContext.Dispose();

        return result;
    }

    /// <summary>
    /// Queries the number of total outstanding code reviews.
    /// </summary>
    [WebMethod]
    public int GetNumberOfOpenReviews()
    {
        if (!HttpContext.Current.User.Identity.IsAuthenticated)
            return 0;

        CodeReviewDataContext dataContext = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);

        int result = (from cc in dataContext.ChangeLists where cc.Stage == 0 select cc).Distinct().Count();

        dataContext.Connection.Close();
        dataContext.Dispose();

        return result;
    }

    /// <summary>
    /// Records that a hint has been shown.
    /// </summary>
    /// <param name="hintNumber"></param>
    /// <returns></returns>
    [WebMethod]
    public void RecordHintShowing(int hintNumber)
    {
        string alias = GetUserAlias();
        if (alias == null)
            return;

        CodeReviewDataContext context = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);

        UserContext uc = UserContext.GetUserContext(alias, Context.Cache, context);

        long mask = 1 << (hintNumber - 1);
        uc.HintsMask = (uc.HintsMask == null ? 0 : uc.HintsMask.Value) | mask;

        context.SetUserContext(UserContext.HINT_MASK, uc.HintsMask.Value.ToString());

        context.Connection.Close();
        context.Dispose();
    }
}
