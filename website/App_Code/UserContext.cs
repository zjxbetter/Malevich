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
using System.Web.Caching;

using DataModel;

/// <summary>
/// Indicates the mode used to add new comments.
/// </summary>
public enum CommentClickMode
{
    /// <summary>
    /// Clicking anywhere within the line will create a new comment.
    /// </summary>
    SingleClickAnywhere = 0,
    /// <summary>
    /// Double-clicking anywhere within a line will create a new comment.
    /// </summary>
    DoubleClickAnywhere = 1,
    /// <summary>
    /// Clicking within the line number column will create a new comment.
    /// </summary>
    ClickLineNumber = 2,
}

/// <summary>
/// This class carries user context - preferences, etc - for Malevich users.
/// </summary>
public class UserContext
{
    /// <summary>
    /// Name of the user data context hash table in system cache.
    /// </summary>
    private const string UserContextTableName = "UserContextTable";

    /// <summary>
    /// The database key for the TextSize.
    /// </summary>
    public const string TEXT_SIZE = "textsize";

    /// <summary>
    /// The font to use when rendering the file view.
    /// </summary>
    public const string TEXT_FONT = "textfont";

    /// <summary>
    /// The database key for the HintMask.
    /// </summary>
    public const string HINT_MASK = "hintmask";

    /// <summary>
    /// The database key for the MaxLineLength.
    /// </summary>
    public const string MAX_LINE_LENGTH = "maxline";

    /// <summary>
    /// Spaces per tab.
    /// </summary>
    public const string SPACES_PER_TAB = "spacespertab";

    /// <summary>
    /// If this is set, use windiff-style file viewer.
    /// </summary>
    public const string UNIFIED_DIFF_VIEW = "unifieddiff";

    /// <summary>
    /// Determines comment create mode:
    ///   1. Single click anywhere creates new comment.
    ///   2. Double click anywhere creates new comment.
    ///   3. Single click within the line number column creates new comment.
    /// </summary>
    public const string COMMENT_CLICK_MODE = "commentClickMode";

    /// <summary>
    /// If set, remove comments with nothing in them.
    /// </summary>
    public const string AUTO_COLLAPSE_COMMENTS = "autocollapsecomments";

    /// <summary>
    /// User to which the context belongs. Just the name stripped off the domain etc.
    /// We get it using Windows authentication.
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    /// Size of text.
    /// 0 or null = small
    /// 1 = medium
    /// 2 = large
    /// </summary>
    public int? TextSize { get; set; }

    /// <summary>
    /// Font family to use to render file view.
    /// </summary>
    public string TextFont { get; set; }

    /// <summary>
    /// Maximum line length.
    /// </summary>
    public int? MaxLineLength { get; set; }

    /// <summary>
    /// Hints shown to user so far.
    /// </summary>
    public Int64? HintsMask { get; set; }

    /// <summary>
    /// Number of spaces to display for a tab. -1 = \t.
    /// </summary>
    public int? SpacesPerTab { get; set; }

    /// <summary>
    /// If true, use windiff-style view.
    /// </summary>
    public bool? UnifiedDiffView { get; set; }

    /// <summary>
    /// Determines comment create mode:
    ///   1. Single click anywhere creates new comment. (Default)
    ///   2. Double click anywhere creates new comment.
    ///   3. Single click within the line number column creates new comment.
    /// </summary>
    public CommentClickMode CommentClickMode { get; set; }

    /// <summary>
    /// If true, delete comments with nothing in them when user clicks elsewhere.
    /// </summary>
    public bool? AutoCollapseComments { get; set; }

    /// <summary>
    /// Trivial constructor.
    /// </summary>
    /// <param name="userName"> User name. </param>
    public UserContext(string userName)
    {
        UserName = userName;
        CommentClickMode = CommentClickMode.SingleClickAnywhere;
    }

    /// <summary>
    /// Gets the data context out of the system cache, or the database.
    /// </summary>
    /// <param name="user"> Authenticated user alias (no DOMAIN). </param>
    /// <param name="cache"> System cache. </param>
    /// <param name="context"> Data connection to the database. </param>
    /// <returns> User context. </returns>
    public static UserContext GetUserContext(string user, Cache cache, CodeReviewDataContext context)
    {
        Hashtable allUserContexts = (Hashtable)cache[UserContextTableName];
        if (allUserContexts == null)
            cache[UserContextTableName] = allUserContexts = new Hashtable();

        UserContext uc = (UserContext)allUserContexts[user];
        if (uc == null)
        {
            uc = new UserContext(user);
            var dataContexts = from cc in context.UserContexts
                               where cc.UserName == user
                               select cc;
            foreach (DataModel.UserContext dataContext in dataContexts)
            {
                if (dataContext.KeyName.Equals(UserContext.TEXT_SIZE, StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(dataContext.Value, out value))
                        uc.TextSize = value;
                }
                else if (dataContext.KeyName.Equals(UserContext.TEXT_FONT, StringComparison.OrdinalIgnoreCase))
                {
                    uc.TextFont = dataContext.Value;
                }
                else if (dataContext.KeyName.Equals(UserContext.HINT_MASK, StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(dataContext.Value, out value))
                        uc.HintsMask = value;
                }
                else if (dataContext.KeyName.Equals(UserContext.MAX_LINE_LENGTH, StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(dataContext.Value, out value))
                        uc.MaxLineLength = value;
                }
                else if (dataContext.KeyName.Equals(UserContext.SPACES_PER_TAB, StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(dataContext.Value, out value))
                        uc.SpacesPerTab = value;
                }
                else if (dataContext.KeyName.Equals(UserContext.UNIFIED_DIFF_VIEW, StringComparison.OrdinalIgnoreCase))
                {
                    bool value;
                    if (bool.TryParse(dataContext.Value, out value))
                        uc.UnifiedDiffView = value;
                }
                else if (dataContext.KeyName.Equals(UserContext.COMMENT_CLICK_MODE, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        uc.CommentClickMode = (CommentClickMode)Enum.Parse(
                            typeof(CommentClickMode), dataContext.Value);
                    }
                    catch (ArgumentNullException) { }
                    catch (ArgumentException) { }
                }
                // Compat with previous "double/single" click database entries
                else if (dataContext.KeyName.Equals("commentstyle", StringComparison.OrdinalIgnoreCase))
                {
                    int value;
                    if (int.TryParse(dataContext.Value, out value))
                    {
                        uc.CommentClickMode =
                            (value == 0 ? CommentClickMode.SingleClickAnywhere
                                        : CommentClickMode.DoubleClickAnywhere);

                        // Save translated setting
                        context.SetUserContext(UserContext.COMMENT_CLICK_MODE, uc.CommentClickMode.ToString());
                    }

                    // Remove old setting from database
                    context.UserContexts.DeleteOnSubmit(dataContext);
                    context.SubmitChanges();
                }
                else if (dataContext.KeyName.Equals(
                    UserContext.AUTO_COLLAPSE_COMMENTS,
                    StringComparison.OrdinalIgnoreCase))
                {
                    bool value;
                    if (bool.TryParse(dataContext.Value, out value))
                        uc.AutoCollapseComments = value;
                }
            }

            allUserContexts[user] = uc;
        }

        return uc;
    }
}
