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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using DataModel;
using Malevich.Extensions;
using Malevich.Util;
using TableGen = Malevich.Util.TableGen;

/// <summary>
/// Ugly as hell, but this is copied here to remove the dependency on
/// SourceControl - which pulls in a dependency on TFS which we definitely
/// do not want on a web server.
/// 
/// Keep this in sync with SourceControl.ChangeFile.SourceControlAction.
/// 
/// What type of the change it is.
/// </summary>
public enum SourceControlAction
{
    ADD = 0,
    EDIT = 1,
    DELETE = 2,
    BRANCH = 3,
    INTEGRATE = 4,
    RENAME = 5
};

namespace Malevich
{
    /// <summary>
    /// Implements a TextReader-like class that (only two methods though: ReadLine and Close)
    /// that wraps around either a TextReader, or a bunch of diffs.
    /// </summary>
    //@TODO: Would be nice to have this behave like a forward iteration stream, could be used
    //       with .NET stream reader classes.
    sealed class StreamCombiner : IDisposable
    {
        // The base text reader.
        private TextReader BaseTextReader;

        // The diff reader or null if we're working with a single file.
        private TextReader DiffTextReader;

        // The line we're on in the base file.
        private int LineBase;

        // The line where the next section of diff starts.
        private int NextLine;

        // We are inside the diff area.
        private bool InDiffArea;

        // Matches the line number of the next change.
        private Regex DiffDecoder = new Regex(@"^([0-9]+)(,[0-9]+)?([a,d,c]).*$");

        /// <summary>
        /// Parses a line from the diff file which specifies the next line where the next diff section starts.
        /// </summary>
        /// <param name="text"> The line to parse. </param>
        /// <returns> The line number, or -1 if the line is not a section header. Usually this means that the diff file
        ///     has ended. </returns>
        private int ParseDiffLine(string text)
        {
            if (text == null)
                return -1;

            Match m = DiffDecoder.Match(text);
            if (m.Success)
            {
                int line = Int32.Parse(m.Groups[1].Value);
                // 'a' adds AFTER the line, but we do processing once we get to the line.
                // So we need to really get to the next line.
                if (m.Groups[3].Value.Equals("a"))
                    line += 1;

                return line;
            }

            return -1;
        }

        /// <summary>
        /// Constructor for file and a diff case. This is where the meat of this class is used.
        /// </summary>
        /// <param name="baseText"> The base of the file. </param>
        /// <param name="diffText"> The diff. </param>
        public StreamCombiner(string baseText, string diffText)
        {
            BaseTextReader = new StringReader(baseText == null ? "" : baseText);
            DiffTextReader = new StringReader(diffText == null ? "" : diffText);
            LineBase = 1;
            //InDiffArea = false;
            string DiffLine = DiffTextReader.ReadLine();
            NextLine = ParseDiffLine(DiffLine);
        }

        /// <summary>
        /// The wrapper case - just wraps a string.
        /// </summary>
        /// <param name="baseText"> The string to wrap. </param>
        public StreamCombiner(string baseText)
        {
            BaseTextReader = new StringReader(baseText == null ? "" : baseText);
            //DiffTextReader = null;
            LineBase = 1;
            NextLine = -1;
            //InDiffArea = false;
        }

        /// <summary>
        /// The wrapper case - just wraps a TextReader. Will Close it on Close. Nothing intelligent.
        /// </summary>
        /// <param name="baseReader"> TextReader to wrap. </param>
        public StreamCombiner(TextReader baseReader)
        {
            BaseTextReader = baseReader;
            DiffTextReader = null;
            LineBase = 1;
            NextLine = -1;
            //InDiffArea = false;
        }

        /// <summary>
        /// Frees all the resources.
        /// </summary>
        public void Close()
        {
            BaseTextReader.Close();
            if (DiffTextReader != null)
                DiffTextReader.Close();
        }

        /// <summary>
        /// Read one line of input.
        /// </summary>
        /// <returns> The line it has just read, or null for eof. </returns>
        public string ReadLine()
        {
            if (LineBase == NextLine)
                InDiffArea = true;

            if (InDiffArea)
            {
                for (; ; )
                {
                    string DiffLine = DiffTextReader.ReadLine();
                    if (DiffLine == null)
                    {
                        ++NextLine;
                        return BaseTextReader.ReadLine();
                    }
                    else if (DiffLine.StartsWith("<", StringComparison.InvariantCulture))
                    {
                        ++LineBase;
                        BaseTextReader.ReadLine();
                    }
                    else if (DiffLine.StartsWith("-", StringComparison.InvariantCulture))
                    {
                        continue;
                    }
                    else if (DiffLine.StartsWith(">", StringComparison.InvariantCulture))
                    {
                        return DiffLine.Substring(2);
                    }
                    else if (DiffLine.Equals("\\ No newline at end of file", StringComparison.InvariantCulture))
                    {
                        // This is a very annoying perforce thing. But we have to account for it.
                        continue;
                    }
                    else
                    {
                        NextLine = ParseDiffLine(DiffLine);
                        InDiffArea = false;
                        return ReadLine();
                    }
                }
            }

            ++LineBase;
            return BaseTextReader.ReadLine();
        }

        /// <summary>
        /// Used to enumerate every line in the stream until EOF.
        /// </summary>
        /// <returns>An IEnumerable that can be used to read each line sequentially.</returns>
        public IEnumerable<string> ReadLines()
        {
            for (string line = ReadLine(); line != null; line = ReadLine())
                yield return line;
            yield break;
        }

        /// <summary>
        /// Closes the stream.
        /// </summary>
        void IDisposable.Dispose()
        {
            Close();
        }
    }

    /// <summary>
    /// Represents the set of file diff viewing options.
    /// </summary>
    struct FileDiffViewOptions
    {
        /// <summary>
        /// The diff view should be unified (windiff style).
        /// </summary>
        public bool IsUnified { get; set; }

        /// <summary>
        /// The diff view should be side by side.
        /// </summary>
        public bool IsSplit { get { return !IsUnified; } }

        /// <summary>
        /// The base file should be displayed on the left.
        /// </summary>
        public bool IsBaseLeft { get; set; }

        /// <summary>
        /// The base file should be displayed on the right.
        /// </summary>
        public bool IsBaseRight { get { return !IsBaseLeft; } }

        /// <summary>
        /// Large blocks of unchanged lines should be omitted from the diff display.
        /// </summary>
        public bool OmitUnchangedLines { get; set; }

        /// <summary>
        /// Large blocks of unchanged lines should be shown in the diff display.
        /// </summary>
        public bool ShowUnchangedLines { get { return !OmitUnchangedLines; } }

        /// <summary>
        /// The click mode used for creating comments.
        /// </summary>
        public CommentClickMode CommentClickMode { get; set; }

        /// <summary>
        /// Should empty comments be auto-collapsed?
        /// </summary>
        public bool AutoCollapseComments { get; set; }
    }

    /// <summary>
    /// Indicates the type of difference encapsulated in a DiffItem object.
    /// </summary>
    internal enum DiffType
    {
        None,
        Unchanged,
        Changed,
        Added,
        Deleted,
    };

    /// <summary>
    /// Implementation of the Malevich code review web site.
    /// </summary>
    public partial class _Default : System.Web.UI.Page
    {
        /// <summary>
        /// This class is used solely for statistics queries in the database.
        /// 
        /// It supplies a type for the return values that consists of a user name and the number of occurences of records
        /// with this user in the table.
        /// 
        /// Note the warning about unassigned class members being disabled - this class is only being constructed using
        /// reflection as a target for LINQ query execution - hence no constructor, and therefore no initialization.
        /// </summary>
#pragma warning disable 649
        private sealed class StatQueryData
        {
            /// <summary>
            /// Name of the user.
            /// </summary>
            public string UserName;

            /// <summary>
            /// Frequency of this user in the table.
            /// </summary>
            public int Freq;
        }
#pragma warning restore 649

        /// <summary>
        /// In-memory representation of a comment in a form ready for rendering.
        /// </summary>
        private sealed class AbstractedComment
        {
            /// <summary>
            /// Version id of the file.
            /// </summary>
            public int VersionId;

            /// <summary>
            /// Comment's line.
            /// </summary>
            public int Line;

            /// <summary>
            /// Ordering within the line.
            /// </summary>
            public long LineStamp;

            /// <summary>
            /// Who has made this comment.
            /// </summary>
            public string UserName;

            /// <summary>
            /// Time when comment was made.
            /// </summary>
            public DateTime TimeStamp;

            /// <summary>
            /// Whether the comment is read-only.
            /// A comment is read-only if it has been submitted.
            /// </summary>
            public bool IsReadOnly;

            /// <summary>
            /// The text of the comment.
            /// </summary>
            public string CommentText;

            /// <summary>
            /// Trivial constructor. Sets the fields.
            /// </summary>
            /// <param name="versionId"> File version id. </param>
            /// <param name="line"> Line. </param>
            /// <param name="lineStamp"> Line stamp. </param>
            /// <param name="userName"> User name of the commenter. </param>
            /// <param name="timeStamp"> Time when comment was made. </param>
            /// <param name="isReadOnly"> Whether the comment is read-only. </param>
            /// <param name="commentText"> Text of the comment. </param>
            public AbstractedComment(int versionId, int line, long lineStamp, string userName, DateTime timeStamp,
                bool isReadOnly, string commentText)
            {
                VersionId = versionId;
                Line = line;
                LineStamp = lineStamp;
                UserName = userName;
                TimeStamp = timeStamp;
                IsReadOnly = isReadOnly;
                CommentText = commentText;
            }

            /// <summary>
            /// Comment Id to be used in HTML.
            /// </summary>
            /// <param name="prefix"> String to prefix the comment with - should be "base_" or "diff_". </param>
            /// <returns> The id to be used by JavaScript. </returns>
            public string JavaScriptId(string prefix)
            {
                return prefix + "_" + VersionId + "_" + Line + "_" + LineStamp;
            }
        }

        /// <summary>
        /// This class is used to extract partial information from the version history.
        /// It is primarily designed to avoid sucking in the actual text of the file.
        /// </summary>
        private sealed class AbstractedFileVersion
        {
            /// <summary>
            /// The id of the version.
            /// </summary>
            public int Id;

            /// <summary>
            /// File action.
            /// </summary>
            public SourceControlAction Action;

            /// <summary>
            /// Base source control revision number.
            /// </summary>
            public int Revision;

            /// <summary>
            /// Time stamp.
            /// </summary>
            public DateTime? TimeStamp;

            /// <summary>
            /// Text or binary.
            /// </summary>
            public bool IsText;

            /// <summary>
            /// Diff or full text.
            /// </summary>
            public bool IsFullText;

            /// <summary>
            /// Increment or revision base.
            /// </summary>
            public bool IsRevisionBase;

            /// <summary>
            /// Whether the revision has a text body. This is true for add and edit text files,
            /// and MAY be try for text branch and integrate.
            /// </summary>
            public bool HasTextBody;

            /// <summary>
            /// Trivial constructor.
            /// </summary>
            /// <param name="id"> The version id. </param>
            /// <param name="action"> The action (add, edit, delete). </param>
            /// <param name="revision"> The base revision of this file. 0 for adds. </param>
            /// <param name="timeStamp"> The time stamp of the file. </param>
            /// <param name="isText"> Is this a text file? </param>
            /// <param name="isFullText"> Is this a diff or a full text? </param>
            /// <param name="isRevisionBase"> Is this a base revision? </param>
            /// <param name="hasTextBody"> Is there a body? False by default for branch and integrate. </param>
            public AbstractedFileVersion(int id, int action, int revision, DateTime? timeStamp, bool isText,
                bool isFullText, bool isRevisionBase, bool hasTextBody)
            {
                Id = id;
                Action = (SourceControlAction)action;
                Revision = revision;
                TimeStamp = timeStamp;
                IsText = isText;
                IsFullText = isFullText;
                IsRevisionBase = isRevisionBase;
                HasTextBody = hasTextBody;
            }
        }

        /// <summary>
        /// Keeps information discovered in the process of generating the table that can be utilized for giving user
        /// helpful hints.
        /// </summary>
        private sealed class HintDataContext
        {
            /// <summary>
            /// Comments by people other than the user discovered.
            /// </summary>
            public bool HaveCommentsByOthers;// = false;

            /// <summary>
            /// Comments by the user discovered.
            /// </summary>
            public bool HaveCommentsByUser;// = false;

            /// <summary>
            /// More than one file version in file view.
            /// </summary>
            public bool HaveMultipleVersions;// = false;

            /// <summary>
            /// We're in the diff table.
            /// </summary>
            public bool InDiffView;// = false;

            /// <summary>
            /// Am I a change author?
            /// </summary>
            public bool IsChangeAuthor;// = false;

            /// <summary>
            /// Am I an official reviewer?
            /// </summary>
            public bool IsChangeReviewer;// = false;

            /// <summary>
            /// Change is not active.
            /// </summary>
            public bool IsChangeInactive;// = false;

            /// <summary>
            /// Do we have any needs work votes?
            /// </summary>
            public bool HaveNeedsWorkVotes;// = false;

            /// <summary>
            /// We're in change view.
            /// </summary>
            public bool InChangeView;// = false;

            /// <summary>
            /// We're in dashboard view.
            /// </summary>
            public bool InDashboard;// = false;
        }

        /// <summary>
        /// The current user's context.
        /// </summary>
        private UserContext _currentUserContext;
        private UserContext CurrentUserContext
        {
            get
            {
                if (_currentUserContext == null)
                    _currentUserContext = UserContext.GetUserContext(AuthenticatedUserAlias, Cache, DataContext);
                return _currentUserContext;
            }
        }

        /// <summary>
        /// Default line encoder: no syntax highlighting, etc.
        /// </summary>
        private class DefaultEncoder : ILineEncoder
        {
            /// <summary>
            /// Encodes the string. Unlike standard HtmlEncode, our custom version preserves spaces correctly.
            /// Also, converts tabs to "\t", and breaks lines in chunks of maxLineWidth non-breaking segments.
            /// </summary>
            /// <param name="s"> The string to encode. </param>
            /// <param name="maxLineWidth"> The maximum width. </param>
            /// <param name="tabValue"> Text string to replace tabs with. </param>
            /// <returns> The string which can be safely displayed in HTML. </returns>
            public static string EncodeLine(string s, int maxLineWidth, string tabValue)
            {
                if (s == null)
                    return null;

                s = s.Replace("\t", tabValue);

                StringBuilder sb = new StringBuilder();
                for (int pos = 0; pos < s.Length; )
                {
                    int charsToGet = s.Length - pos;
                    if (charsToGet > maxLineWidth)
                        charsToGet = maxLineWidth;
                    sb.Append(HttpUtility.HtmlEncode(s.Substring(pos, charsToGet)));
                    pos += charsToGet;
                    if (s.Length - pos > 0)
                        sb.Append("<br/>");
                }

                return sb.ToString();
            }

            /// <summary>
            /// Does nothing - exists just to satisfy the interface requirements.
            /// </summary>
            /// <returns></returns>
            public TextReader GetEncoderCssStream()
            {
                return null;
            }

            /// <summary>
            /// Does nothing, just satisfies ILineEncoder interface.
            /// </summary>
            public void Dispose()
            {
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// Forwards to static EncodeLine method.
            /// </summary>
            string ILineEncoder.EncodeLine(string line, int maxLineLength, string tabSubstitute)
            {
                return EncodeLine(line, maxLineLength, tabSubstitute);
            }

            /// <summary>
            /// Does nothing - exists just to satisfy the interface requirements.
            /// </summary>
            /// <returns></returns>
            TextReader ILineEncoder.GetEncoderCssStream()
            {
                return null;
            }
        }

        // Vertical small font size in pixels.
        private const int DefaultSmallFontSizeVr = 10;

        // Horizontal small font size in pixels.
        private const int DefaultSmallFontSizeHz = 6;

        // Vertical medium font size in pixels.
        private const int DefaultMediumFontSizeVr = 12;

        // Horizontal medium font size in pixels.
        private const int DefaultMediumFontSizeHz = 7;

        // Vertical large font size in pixels.
        private const int DefaultLargeFontSizeVr = 14;

        // Horizontal large font size in pixels.
        private const int DefaultLargeFontSizeHz = 8;

        // Font family.
        private const string DefaultFontFamily = "monospace";

        /// <summary>
        /// Parser for the font configuration in app settings.
        /// </summary>
        private Regex FontParser = new Regex("^(?<fontname>[a-zA-Z '\",]+)\\(" +
            "(?<smallfontsizex>\\d{1,2}):(?<smallfontsizey>\\d{1,2})," +
            "(?<medfontsizex>\\d{1,2}):(?<medfontsizey>\\d{1,2})," +
            "(?<largefontsizex>\\d{1,2}):(?<largefontsizey>\\d{1,2})" +
            "\\)$");

        // Horizontal padding in diff table, in pixels.
        private const int DiffTableElementPaddingHz = 1;

        // The difference between length of the line and the size of the comment box, in pixels.
        private const int CommentTextWrapperSizeDelta = -120;

        // The difference between length of the line and the size of the comment table, in pixels.
        private const int CommentTableSizeDelta = -20;

        // Unix epoch base for time conversions (in UTC).
        private static DateTime unixEpochOrigin = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        // Set in _Default's .ctor.
        private int DefaultMaxLineLength;

        /// <summary>
        /// Maximum line size, in characters.
        /// </summary>
        private int MaxLineLength
        {
            get
            {
                var uc = CurrentUserContext;
                var len = (uc.MaxLineLength != null) ? uc.MaxLineLength.Value : DefaultMaxLineLength;
                if (len == 0)
                    len = int.MaxValue;
                return len;
            }
        }

        /// <summary>
        /// The file diff view options for the current table.
        /// </summary>
        private FileDiffViewOptions DiffViewOptions
        { get; set; }

        // Max length of the description we output in the change list.
        private int MaxDescriptionLength = 120;

        // Max length of the review comment we output in the change list.
        private int MaxReviewCommentLength = 80;

        // The data connection.
        private CodeReviewDataContext DataContext = new CodeReviewDataContext(
            System.Configuration.ConfigurationManager.ConnectionStrings[Config.ConnectionString].ConnectionString);

        // The diff program.
        private string DiffExe = System.Configuration.ConfigurationSettings.AppSettings["diffExe"];

        // Differ arguments.
        private string DiffArgsBase = System.Configuration.ConfigurationSettings.AppSettings["diffArgsBase"];

        // Additional arguments to the differ program when white space.
        private string DiffArgsIgnoreWhiteSpace =
            System.Configuration.ConfigurationSettings.AppSettings["diffArgsIgnoreWhiteSpace"];

        // Authenticated alias, sans domain.
        private string AuthenticatedUserAlias;

        // Hints discovered during the generation of the table.
        private HintDataContext HintsData = new HintDataContext();

        // Styles for the line encoder.
        private TextReader encoderStyles;

        /// <summary>
        /// Overrides the dispose function to get rid of the database connection context.
        /// </summary>
        public override void Dispose()
        {
            if (encoderStyles != null)
                encoderStyles.Dispose();

            DataContext.Connection.Close();
            DataContext.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// Trivial constructor. Initializes key constants from the AppSettings section.
        /// </summary>
        public _Default()
        {
            if (!Int32.TryParse(System.Configuration.ConfigurationSettings.AppSettings["maxLineLength"],
                out DefaultMaxLineLength))
            {
                DefaultMaxLineLength = 120;
            }
            if (!Int32.TryParse(System.Configuration.ConfigurationSettings.AppSettings["maxDescriptionLength"],
                out MaxDescriptionLength))
            {
                MaxDescriptionLength = 120;
            }
            if (!Int32.TryParse(System.Configuration.ConfigurationSettings.AppSettings["maxReviewCommentLength"],
                out MaxReviewCommentLength))
            {
                MaxReviewCommentLength = 80;
            }
        }

        /// <summary>
        /// Returns the source control id for the current web site, or -1, if the web site does not match.
        /// </summary>
        /// <returns> Source control id or -1. </returns>
        private int GetSourceControlId()
        {
            string path = Request.ApplicationPath;
            SourceControl[] sourceControl = (from sc in DataContext.SourceControls
                                             where path.Equals(sc.WebsiteName)
                                             select sc).ToArray();

            return sourceControl.Length > 0 ? sourceControl[0].Id : -1;
        }

        /// <summary>
        /// Gets the abstracted list of file versions (everything except for the actual text).
        /// </summary>
        /// <param name="fid"> The file id. </param>
        /// <returns> The array of AbstractedFileVersions. </returns>
        private AbstractedFileVersion[] GetVersionsAbstract(int fid)
        {
            return (from vr in DataContext.FileVersions
                    where vr.FileId == fid
                    orderby vr.Id
                    select new AbstractedFileVersion(
                        vr.Id, vr.Action, vr.Revision, vr.TimeStamp, vr.IsText,
                        vr.IsFullText, vr.IsRevisionBase, vr.Text != null)).ToArray();
        }

        /// <summary>
        /// Gets the abstracted list of file versions that contain text (everything except for the actual text).
        /// </summary>
        /// <param name="fid"> The file id. </param>
        /// <returns> The array of AbstractedFileVersions. </returns>
        private AbstractedFileVersion[] GetVersionsWithTextAbstract(int fid)
        {
            var versionsQuery = from vr in DataContext.FileVersions
                                where vr.FileId == fid && vr.Text != null
                                orderby vr.Id
                                select new AbstractedFileVersion(vr.Id, vr.Action, vr.Revision, vr.TimeStamp, vr.IsText,
                                    vr.IsFullText, vr.IsRevisionBase, true);

            return versionsQuery.ToArray();
        }

        /// <summary>
        /// Abbreviates text to a given number of characters. Also combines multiple lines in a single one (replaces
        /// EOL characters with spaces. It does not pay attention to the word breaks. HtmlEncodes the results.
        /// </summary>
        /// <param name="text"> The text to abbreviate. </param>
        /// <returns> The abbreviated string. </returns>
        private string AbbreviateToOneLine(string text, int maxLineLen)
        {
            text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (text.Length > maxLineLen)
                text = text.Substring(0, maxLineLen - "...".Length) + "...";
            return Server.HtmlEncode(text);
        }

        /// <summary>
        /// Creates a string representation of a timestamp. This can later be processed by JavaScript to covert
        /// from UTC to local.
        /// </summary>
        /// <param name="ts"> Time stamp to wrap. </param>
        /// <returns> Html element (as string) wrapping the time stamp text. </returns>
        private static string WrapTimeStamp(DateTime ts)
        {
            TimeSpan diff = ts - unixEpochOrigin;
            return "<span name=\"timestamp\" id=\"timestamp\" ticks=\"" + Math.Floor(diff.TotalSeconds) + "\">" + ts +
                " UTC</span>";
        }

		/// <summary>
		/// Same as WrapTimeStamp but only displays the date
		/// </summary>
		/// <param name="ts"> DateTime stamp to wrap. </param>
		/// <returns> Html element (as string) wrapping the date stamp text. </returns>
		private static string WrapDateStamp(DateTime ts)
		{
			TimeSpan diff = ts - unixEpochOrigin;
			return "<span name=\"timestamp\" id=\"timestamp\" ticks=\"" + Math.Floor(diff.TotalSeconds) + "\">" + ts.ToShortDateString() +
				" UTC</span>";
		}

        /// <summary>
        /// Creates and adds a new label to the active page.
        /// </summary>
        /// <param name="contents">The label contents.</param>
        /// <returns>The new label.</returns>
        private Label AddLabel(string contents)
        {
            var label = new Label() { Text = contents };
            ActivePage.Controls.Add(label);
            return label;
        }

        /// <summary>
        /// Creates and adds a new hyperlink to the active page.
        /// </summary>
        /// <param name="label"> The text label of the hyperlink. </param>
        /// <param name="query"> The query, starting with "?". </param>
        /// <returns>The new hyperlink.</returns>
        private HyperLink AddLink(string label, string query)
        {
            HyperLink link = new HyperLink()
            {
                Text = label,
                NavigateUrl = Request.FilePath + query
            };
            ActivePage.Controls.Add(link);
            return link;
        }

        /// <summary>
        /// Display the error report.
        /// </summary>
        /// <param name="errorReport"> The error to print. </param>
        private void ErrorOut(string errorReport)
        {
            AddLabel("<font color=red>" + errorReport + "</font>");
        }

        /// <summary>
        /// Displays the header for a page.
        /// </summary>
        /// <param name="title"> The title to show. </param>
        private void DisplayPageHeader(string title)
        {
            Page.Header.Title = title;
            Master.Title = title;
        }

        /// <summary>
        /// Create a link that has the appearance of a button.
        /// Currently disabled, as display does not quite work right.
        /// </summary>
        private HyperLink CreateLinkButton(string text, string url)
        {
            return new HyperLink()
            {
                Text = text,
                NavigateUrl = url,
            };
        }

        /// <summary>
        /// Displays context-sensitive help.
        /// </summary>
        /// <param name="sourceUrl"> The URL from which the request came. </param>
        private void DisplayHelp(string sourceUrl)
        {
            DisplayPageHeader("Malevich help");

            string url = Server.HtmlDecode(sourceUrl);

            ActivePage.Controls.Add(
                CreateLinkButton("Back...", Server.UrlDecode(sourceUrl)));

            AddLabel("<br><br>");

            if (url.Contains("cid")) // Change list view
            {
                AddLabel("<p>This page displays the details of a change list, " +
                    "the history of the review iterations, and the current vote.</p>");

                AddLabel("<p>Most importantly, it allows both the reviewers as well as casual browsers to submit " +
                    "the review iterations.</p>");

                AddLabel("<p>A <b>review iteration</b> is a collection of comments made in the file views " +
                    "accompanied by one top level comment, which can be entered on this page.</p>");

                AddLabel("<p>Usually, most of the comments are made in the file views (you can get there by clicking on " +
                    "any one of the file names in this view). These comments can be made simply by clicking on any " +
                    "line and typing. However, <b>they are not visible to the change author and other reviewers until " +
                    "the whole iteration is submitted from this page</b>.</p>");

                AddLabel("<p>If a review iteration comes from a reviewer, the reviewer has an option to enter a vote. " +
                    "A vote can be one of:</p>");

                Table t = new Table();
                ActivePage.Controls.Add(t);
                t.Width = new Unit(100, UnitType.Percentage);

                TableRow r = new TableRow();
                t.Rows.Add(r);
                TableCell c = new TableCell();
                r.Cells.Add(c);
                c.Text = "LGTM";
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "'Looks Good To Me' - in the reviewer's opinion, the change is ready to be submitted. " +
                    "No further iterations are necessary.";

                r = new TableRow();
                t.Rows.Add(r);
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "LGTM with minor tweaks";
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "The reviewer has recommended a few changes, but does not feel strongly about them - these are " +
                    "mere suggestions. The change can be checked in with or without following the recommendations, " +
                    "and no further iterations are necessary.";

                r = new TableRow();
                t.Rows.Add(r);
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "Needs work";
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "The reviewer things that the change is not ready yet. The comments in this review iteration " +
                    "should be addressed, and the reviewer should be allowed to re-examine the result. The review should " +
                    "not be closed until there is even one vote that says 'Needs work'.";

                r = new TableRow();
                t.Rows.Add(r);
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "Non-scoring comment";
                c = new TableCell();
                r.Cells.Add(c);
                c.Text = "The reviewer withholds his or her opinion. This type of vote is typically used to abrogate the " +
                    "responsibility for the code review - for example, if the reviewer is very busy.</p>";

                AddLabel("<p>An author of the change list (the reviewee) can also use this page to submit the review " +
                    "iteration. Reviewees use commenting system to enter responses to the review iterations submitted " +
                    "by others. It is a good practice to respond in this way at the very minimum to the review " +
                    "iterations with 'Needs work' votes. Reviewees can not vote on the code review - their " +
                    "comments are always entered with the 'Non-scoring' vote.</p>");

                AddLabel("<p>In addition to reviewers and reviewees, any casual browser can enter comments. " +
                    "However, they can not vote on the review. Their review iterations are filed with the 'Non-scoring' " +
                    "vote. However, anybody (except for the people who are already part of the review) can join " +
                    "the review and become the 'official' reviewer by following 'I want to review this change' link.</p>");
            }
            else if (url.Contains("fid")) // File view
            {
                AddLabel("<p>This page allows you to enter comments for the code, in the code.</p>");

                AddLabel("<p>Comments can be entered on any line simply by clicking on that line and typing. " +
                    "Comments that has been entered before the current review iteration is submitted can be edited " +
                    "and deleted by clicking on the comment box and then clicking on the appropriate button.</p>");

                AddLabel("<p>However, comments made here <b>are not visible to anyone other than their author until " +
                    "the review iteration is submitted from the change view page</b>.</p>");

                AddLabel("<p>After the review iteration is submitted, the comments made as part of that iteration " +
                    "are no longer editable or removable. It is however possible to respond to the comments in any " +
                    "of the previous review iterations by clicking on the comment body.</p>");

                AddLabel("<p>Every file may have multiple versions, all accessible from this screen.</p>");

                AddLabel("<p>Files that are being added typically would have one version on the first review " +
                    "iteration, and more versions would be added as they are changed and the review resubmitted " +
                    "in the course of responding to the reviewer comments.</p>");

                AddLabel("<p>Files that are being edited would have the new version, as well as the base version " +
                    "from the source control.</p>");

                AddLabel("<p>To distinguish between the versions, they are shown with a UTC time of when this file was " +
                    "either last modified before the review was submitted (perforce and SD), or recorded in the shelf " +
                    "set (TFS).</p>");

                AddLabel("<p>This screen can show the differences between any two files - simply select the version that " +
                    "you want to see on the right, and the version that you want to see on the left, and the " +
                    "color-coded view of the difference similar to windiff will be displayed.</p>");

                AddLabel("<p>Red color means that the line has been deleted, yellow that the line has been changed, " +
                    "and green means that the line has been added.</p>");

                AddLabel("<p><b>Quick navigation tip:</b> To quickly scroll to a change, hover over the first or last " +
                    "visible change on a page, and an arrow will appear. Click on this arrow to scroll. If there are " +
                    "no comments visible on the screen that contains the very top or the very bottom file, however over " +
                    "the first or the last line.</p>");

                AddLabel("<p><b>Hint:</b> You can control the size of the font in the file viewer from " +
                    "the settings page.</p>");
            }
            else // Dashboard
            {
                AddLabel("<h2>Welcome to Malevich!</h2>");

                AddLabel("<p>Malevich is a web-based, point-and-click code review system designed for use by individuals " +
                    "and small teams. Its goal is making the cost of a comment as close to zero as possible: easy " +
                    "commenting encourages thorough code reviews.</p>");

                AddLabel("Reviewing code in Malevich is easy indeed. A reviewer can see both the original as well as " +
                    "the new revision of a file in a browser. To comment on a line of code, he or she simply clicks on " +
                    "that line, and starts typing. Submitting comments makes them visible to the person who requested " +
                    "the code review, as well as to all other reviewers.</p>");

                AddLabel("<p>Learn more about using Malevich here:</p>");

                var l = new HyperLink();
                ActivePage.Controls.Add(l);
                l.Text = "http://www.codeplex.com/Malevich/Wiki/View.aspx?title=Usage%20walkthrough";
                l.NavigateUrl = "http://www.codeplex.com/Malevich/Wiki/View.aspx?title=Usage%20walkthrough";

                AddLabel("<br><br><br>");

                AddLabel("<p><b>Note:</b> Malevich help is context-sensitive: clicking on the help link on every page " +
                    "where it is available will give you information on how to use that page.</p>");

                AddLabel("<br>");

                AddLabel("<p>This page shows the list of currently active reviews for the logged (or explicitly " +
                    "requested) user, as well as the recent history of the code reviews that have been completed.</p>");

                AddLabel("<p>To look up a dashboard for someone else, specify that user's name " +
                    "using the query parameter, like this: " + Request.FilePath + "?alias=useralias</p>");

                AddLabel("<p>To look up all active reviews, do this:" + Request.FilePath + "?alias=*</p>");

                AddLabel("<p>You can also look up a specific change list/shelf set by using CL query parameter as follows:"
                    + Request.FilePath + "?CL=clnumberorshelfsetname</p>");

                AddLabel("<p>Otherwise, click on any of the links to see the details of the change list and the " +
                    "review comments for it.</p>");
            }

            AddLabel("<br><br>");

            ActivePage.Controls.Add(
                CreateLinkButton("Back...", Server.UrlDecode(sourceUrl)));

        }

        private class OptionSection : WebControl
        {
            
            public string Header;
            public string Description;
            public WebControl Body;

            public OptionSection(bool collapsed = true)
                : base(HtmlTextWriterTag.Fieldset)
            {
                CssClass = "OptionSection collapsible " + (collapsed ? "collapsed" : "");
                Body = new Panel() { CssClass = "collapsible_element" };
            }

            public OptionSection AddTo(Control ctrl)
            {
                ctrl.Add(this);
                return this;
            }

            protected override void OnLoad(EventArgs e)
            {
                // Header
                new WebControl(HtmlTextWriterTag.Legend)
                    .Add(new Literal() { Text = Header })
                    .AddTo(this);
                // Description
                if (!Description.IsNullOrEmpty())
                {
                    new Literal() { Text = Description }
                        .AddTo(this);
                    new WebControl(HtmlTextWriterTag.P).AddTo(this);
                }
                // Body
                Body.AddTo(this);
                base.OnLoad(e);
            }
        }

        /// <summary>
        /// Displays and allows changing user preferences.
        /// </summary>
        /// <param name="sourceUrl"> The URL from which the request came. </param>
        private void DisplaySettings(string sourceUrl)
        {
            DisplayPageHeader("Personalize Malevich to your taste!");

            Panel page = new Panel() { CssClass = "CssSettingsPage" };
            ActivePage.AddBreak();
            ActivePage.Controls.Add(page);

            UserContext uc = CurrentUserContext;

            var settings = new Panel() { CssClass = "Accordion Settings" };
            page.Add(settings);

            // Fonts
            string fonts = System.Configuration.ConfigurationSettings.AppSettings["fonts"];
            if (fonts != null)
            {
                var section = new OptionSection()
                    {
                        Header = "File Viewer Font",
                        Description = "Choose the font to use when viewing files or diffs."
                    }
                    .AddTo(settings);

                RadioButtonList textFonts = new RadioButtonList() { ID = "TextFont" }
                    .AddTo(section.Body);

                string[] fontValues = fonts.Split(';');
                foreach (string font in fontValues)
                {
                    Match m = FontParser.Match(font);
                    if (!m.Success)
                        continue;

                    string fontName = m.Groups["fontname"].Value;

                    ListItem item = new ListItem();
                    item.Text = fontName;
                    item.Value = fontName;
                    if (uc.TextFont != null && uc.TextFont.EqualsIgnoreCase(fontName))
                        item.Selected = true;

                    textFonts.Items.Add(item);
                }
            }

            {   // Font size
                var section = new OptionSection()
                    { 
                        Header = "File Viewer Font Size",
                        Description = "Choose a font for viewing files and diffs."
                    }
                    .AddTo(settings);

                RadioButtonList textSize = new RadioButtonList() { ID = "TextSize" }
                    .AddTo(section.Body);

                int defaultTextSize = 0;
                if (uc.TextSize != null)
                    defaultTextSize = uc.TextSize.Value;

                string[] textSizes = { "small", "medium", "large" };
                for (int i = 0; i < textSizes.Length; ++i)
                {
                    ListItem item = new ListItem();
                    item.Text = textSizes[i];
                    item.Value = textSizes[i];
                    if (i == defaultTextSize)
                        item.Selected = true;

                    textSize.Items.Add(item);
                }
            }

            {   // Max chars per line
                var section = new OptionSection()
                    {
                        Header = "File Viewer Line Length",
                        Description = "Choose number of characters per line in the file viewer and differ."
                    }
                    .AddTo(settings);

                "Enter a value between 80 and 160, or clear the value to revert to the project default<p/>"
                    .AsLiteral()
                    .AddTo(section.Body);
                TextBox tb = new TextBox()
                {
                    ID = "MaxLineLength",
                    Text = (uc.MaxLineLength != null ? uc.MaxLineLength.Value.ToString() : "")
                };
                section.Body.Add(tb);
            }

            // Spaces in a tab
            string tabOverrideAllowed = System.Configuration.ConfigurationSettings.AppSettings["allowTabOverride"];
            if ("true".Equals(tabOverrideAllowed))
            {
                var section = new OptionSection()
                    {
                        Header = "File Viewer Tab Display Format",
                        Description = "Enter '\\t', 'n spaces', or nothing for site default.<p/>"
                    }
                    .AddTo(settings);

                var tb = new TextBox() { ID = "SpacesPerTab" };
                if (uc.SpacesPerTab != null)
                    tb.Text = uc.SpacesPerTab.Value == -1 ? "\\t" : (uc.SpacesPerTab.Value + " spaces");

                section.Body.Add(tb);
            }

            {   // Unified view
                var section = new OptionSection()
                    {
                        Header = "File Viewer Diff Style",
                        Description = "Use windiff-style differ?"
                    }
                    .AddTo(settings);

                RadioButtonList windiff = new RadioButtonList() { ID = "UnifiedViewer" };
                section.Body.Add(windiff);

                windiff.Items.Add(new ListItem() { Text = "Yes", Value = "yes" });
                windiff.Items.Add(new ListItem() { Text = "No", Value = "no" });

                windiff.Items[(uc.UnifiedDiffView ?? false) ? 0 : 1].Selected = true;
            }

            {   // Comment creation mouse-click options.
                var section = new OptionSection()
                    {
                        Header = "Commenting Mode",
                        Description = "Choose how comments are created."
                    }
                    .AddTo(settings);

                var rbl = new RadioButtonList() { ID = "CommentClickMode" };
                section.Body.Add(rbl);

                rbl.Items.Add(new ListItem()
                {
                    Text = "Single click anywhere creates comment.",
                    Value = CommentClickMode.SingleClickAnywhere.ToString(),
                });

                rbl.Items.Add(new ListItem()
                {
                    Text = "Double click anywhere creates comment.",
                    Value = CommentClickMode.DoubleClickAnywhere.ToString(),
                });

                rbl.Items.Add(new ListItem()
                {
                    Text = "Single click on line number creates comment",
                    Value = CommentClickMode.ClickLineNumber.ToString(),
                });

                rbl.Items[(int)uc.CommentClickMode].Selected = true;
            }

            {   // Empty comment collapse
                var section = new OptionSection()
                    {
                        Header = "Comment Collapsing",
                        Description = "Automatically collapse empty comments?"
                    }
                    .AddTo(settings);

                RadioButtonList autoCollapse = new RadioButtonList() { ID = "AutoCollapseComments" };
                section.Body.Add(autoCollapse);

                autoCollapse.Items.Add(new ListItem() { Text = "Yes", Value = "yes" });
                autoCollapse.Items.Add(new ListItem() { Text = "No", Value = "no" });

                autoCollapse.Items[(uc.AutoCollapseComments ?? true) ? 0 : 1].Selected = true;
            }

            {   // Hints
                var section = new OptionSection()
                    {
                        Header = "Hints",
                        Description = "Show hints?"
                    }
                    .AddTo(settings);

                RadioButtonList hints = new RadioButtonList() { ID = "Hints" };
                section.Body.Add(hints);

                hints.Items.Add(new ListItem() { Text = "Yes", Value = "yes" });
                hints.Items.Add(new ListItem() { Text = "No", Value = "no" });
                if ((uc.HintsMask ?? 0) != -1)
                    hints.Items.Add(new ListItem() { Text = "Reset hints", Value = "reset" });

                hints.Items[(uc.HintsMask ?? 0) != -1 ? 0 : 1].Selected = true;
            }

            var buttons = new Panel() { CssClass = "CssButtonBar" }
                .AddTo(page);

            var submit = new Button()
            {
                Text = "Submit",
                CssClass = "button",
            }.AddTo(buttons);
            submit.Click += new EventHandler(changeUserPrefs_Clicked);

            var cancel = new Button()
            {
                Text = "Cancel",
                CssClass = "button",
            }.AddTo(buttons);
            cancel.Click += new EventHandler(delegate(object sender, EventArgs e)
            {
                Response.Redirect(sourceUrl);
            });
        }

        /// <summary>
        /// Appends a comment. The HTML structure produced by this function MUST be kept in sync with the one
        /// produced by createNewComment in comments.js.
        /// </summary>
        /// <param name="sb"> The StringBuilder where the comment should be appended. </param>
        /// <param name="comment"> The comment which should be appended. </param>
        private void AddCommentToCell(StringBuilder sbOut, AbstractedComment comment, string prefix)
        {
            if (comment.UserName.EqualsIgnoreCase(AuthenticatedUserAlias))
                HintsData.HaveCommentsByOthers = true;
            else
                HintsData.HaveCommentsByUser = true;

            StringBuilder sb = new StringBuilder();
            string commentId = comment.JavaScriptId(prefix);
            string commentText = Server.HtmlEncode(comment.CommentText);

            // This is mirrored in comments.js to maintain consistent spacing.
            // IE does not properly obey margin top and bottom values in CSS,
            // so use conditional IE comments to insert directly.
            sb.Append(
    "<div id=\"{commentId}\" class=\"Comment\">");

            if (!comment.IsReadOnly)
                sb.Append(
      "<div class=\"Edit\" style=\"display:none;\">" +
        "<textarea rows=\"6\" cols=\"0\" " +
                  "onkeyup=\"textAreaAutoSize(\'{commentId}\');\">{commentText}</textarea>" +
        "<div class=\"Buttons\">" +
          "<input class=\"button\" type=\"button\" value=\"submit\" " +
                  "onclick=\"onSubmitComment(event);\"/>" +
          "<input class=\"button\" type=\"button\" value=\"cancel\" " +
                  "onclick=\"onCancelComment(event);\"/>" +
          "<input class=\"button\" type=\"button\" value=\"remove\" " +
                  "onclick=\"onRemoveComment(event);\"/>" +
        "</div>" +
      "</div>");

            sb.Append(
      "<div class=\"Display\">" +
        "<div class=\"Header\">" +
          "{userName} on {timeStamp}:" +
        "</div>" +
        "<div class=\"Body\">" +
          "{commentText}" +
        "</div>" +
      "</div>" +
    "</div>");

            sb.Replace("{commentId}", commentId + "_comment");
            sb.Replace("{userName}", comment.UserName);
            sb.Replace("{commentText}", commentText);
            sb.Replace("{timeStamp}", WrapTimeStamp(comment.TimeStamp));

            sbOut.Append(sb);
        }

        /// <summary>
        /// Writes out one line and comments, if any.
        /// </summary>
        /// <param name="cell"> The table cell to use. </param>
        /// <param name="line"> Current line. </param>
        /// <param name="comments"> Comments. </param>
        /// <param name="commentIndex"> [In/Out] Current index into comments array. </param>
        private IEnumerable<Control> EncodeLineTextAndComments(
            ILineEncoder encoder,
            string prefix,
            int lineNumber,
            string lineText,
            AbstractedComment[] comments,
            ref int commentIndex)
        {
            StringBuilder lineHtml = new StringBuilder(encoder.EncodeLine(lineText, int.MaxValue, TabValue));

            StringBuilder commentsHtml = new StringBuilder();
            while (commentIndex < comments.Length && comments[commentIndex].Line == lineNumber)
            {
                AddCommentToCell(commentsHtml, comments[commentIndex], prefix);
                ++commentIndex;
            }

            return new Control[2]
            {
                new Literal() { Text = "<pre class=\"Code\">" + lineHtml.ToString() + "</pre>" },
                new Literal() { Text = commentsHtml.ToString() }
            };
        }

        /// <summary>
        /// Writes out one line and comments, if any.
        /// </summary>
        /// <param name="cell"> The table cell to use. </param>
        /// <param name="line"> Current line. </param>
        /// <param name="comments"> Comments. </param>
        /// <param name="commentIndex"> [In/Out] Current index into comments array. </param>
        private IEnumerable<Control> EncodeLineTextAndComments(
            ILineEncoder encoder,
            string prefix,
            LineAndComments line)
        {
            StringBuilder lineHtml = new StringBuilder(encoder.EncodeLine(line.LineText, int.MaxValue, TabValue));

            StringBuilder commentsHtml = new StringBuilder();
            if (line.Comments != null)
            {
                foreach (var comment in line.Comments)
                    AddCommentToCell(commentsHtml, comment, prefix);
            }

            return new Control[2]
            {
                new Literal() { Text = "<pre class=\"Code\">" + lineHtml.ToString() + "</pre>" },
                new Literal() { Text = commentsHtml.ToString() }
            };
        }

        /// <summary>
        /// Generates the HTML that represents the current line and any associated comments.
        /// </summary>
        /// <param name="file">The file (and current line) to generate the HTML for.</param>
        /// <returns>Generated HTML.</returns>
        private IEnumerable<Control> EncodeLineTextAndComments(
            DiffFileInfo file)
        {
            return EncodeLineTextAndComments(
                file.Encoder,
                file.BaseOrDiff.ToString().ToLowerCultureInvariant(),
                file.CurLineNum,
                file.CurLine,
                file.Comments,
                ref file.NextCommentIndex);
        }

        private string _TabValue;
        private static bool? _TabValueOverridePermitted = null;

        /// <summary>
        /// Computes the string which is used for representing tabs. The default is '\t', but it could be overridden
        /// in user context and in web.config file.
        /// </summary>
        /// <param name="uc"> User's settings. </param>
        /// <returns> A string to be used for substituting the tabs. </returns>
        private string TabValue
        {
            get
            {
                if (_TabValueOverridePermitted == null)
                {
                    string tabOverrideAllowed = System.Configuration.ConfigurationSettings.AppSettings["allowTabOverride"];
                    _TabValueOverridePermitted = "true".Equals(tabOverrideAllowed);
                }

                if (_TabValue == null)
                {
                    if (_TabValueOverridePermitted.Value && CurrentUserContext.SpacesPerTab != null)
                    {
                        _TabValue = new string(' ', CurrentUserContext.SpacesPerTab.Value);
                    }
                    else
                    {
                        string tabValue = "  \\t";
                        string spacesPerTab = System.Configuration.ConfigurationSettings.AppSettings["spacesPerTab"];
                        if (spacesPerTab != null)
                        {
                            int value;
                            if (int.TryParse(spacesPerTab, out value) && value > 0)
                                tabValue = new string(' ', value);
                        }
                        _TabValue = tabValue;
                    }
                }

                return _TabValue;
            }
        }

        /// <summary>
        /// Represents a block of lines of a particular difference type.
        /// </summary>
        class DiffItem : ICloneable
        {
            /// <summary>
            /// The starting line number within the base file.
            /// </summary>
            public int BaseStartLineNumber;

            /// <summary>
            /// The type of difference represented.
            /// </summary>
            public DiffType DiffType;

            /// <summary>
            /// The number of lines removed from the base file.
            /// </summary>
            public int BaseLineCount;

            /// <summary>
            /// The number of lines added to the diff file.
            /// </summary>
            public int DiffLineCount;

            /// <summary>
            /// Generates a sequence of DiffItems representing differences in rawDiffStream. Includes unchanged blocks.
            /// </summary>
            /// <returns>A DiffItem generator.</returns>
            public static IEnumerable<DiffItem> EnumerateDifferences(StreamCombiner rawDiffStream)
            {
                return EnumerateDifferences(rawDiffStream, true);
            }

            /// <summary>
            /// Generates a sequence of DiffItems representing differences in rawDiffStream.
            /// </summary>
            /// <param name="includeUnchangedBlocks">
            /// Indicates whether to generate DiffItems for unchanged blocks.
            /// </param>
            /// <returns>A DiffItem generator.</returns>
            public static IEnumerable<DiffItem> EnumerateDifferences(
                StreamCombiner rawDiffStream, bool includeUnchangedBlocks)
            {
                DiffItem prevItem = null;
                DiffItem item = null;
                string line = null;
                do
                {
                    line = rawDiffStream == null ? null : rawDiffStream.ReadLine();

                    if (line != null && line.StartsWith("<"))
                    {
                        ++item.BaseLineCount;
                    }
                    else if (line != null && line.StartsWith("-"))
                    {
                        continue;
                    }
                    else if (line != null && line.StartsWith(">"))
                    {
                        ++item.DiffLineCount;
                    }
                    else if (line != null && line.Equals("\\ No newline at end of file"))
                    {   // This is a very annoying perforce thing. But we have to account for it.
                        continue;
                    }
                    else
                    {
                        if (item != null)
                        {
                            if (item.DiffLineCount == 0)
                                item.DiffType = DiffType.Deleted;
                            else if (item.BaseLineCount == 0)
                                item.DiffType = DiffType.Added;
                            else
                                item.DiffType = DiffType.Changed;

                            yield return item;
                            prevItem = item;
                            item = null;
                        }

                        if (line != null)
                        {
                            item = new DiffItem();

                            Match m = DiffDecoder.Match(line);
                            if (!m.Success)
                                yield break;

                            item.BaseStartLineNumber = Int32.Parse(m.Groups[1].Value);

                            // 'a' adds AFTER the line, but we do processing once we get to the line.
                            // So we need to really get to the next line.
                            if (m.Groups[3].Value.Equals("a"))
                                item.BaseStartLineNumber += 1;
                        }

                        if (includeUnchangedBlocks)
                        {
                            var unchangedItem = new DiffItem();
                            unchangedItem.DiffType = DiffType.Unchanged;
                            unchangedItem.BaseStartLineNumber =
                                prevItem == null ? 1 : prevItem.BaseStartLineNumber + prevItem.BaseLineCount;
                            unchangedItem.BaseLineCount = item == null ?
                                int.MaxValue : item.BaseStartLineNumber - unchangedItem.BaseStartLineNumber;
                            unchangedItem.DiffLineCount = unchangedItem.BaseLineCount;

                            if (unchangedItem.BaseLineCount != 0)
                                yield return unchangedItem;
                        }
                    }
                } while (line != null);
            }

            /// <summary>
            /// Trivial constructor.
            /// </summary>
            public DiffItem()
            {
                BaseStartLineNumber = 1;
                //DiffLineCount = 0;
                //BaseLineCount = 0;
            }

            /// <summary>
            /// Matches the line number of the next change.
            /// </summary>
            private static Regex DiffDecoder = new Regex(@"^([0-9]+)(,[0-9]+)?([a,d,c]).*$");

            /// <summary>
            /// Clones the object.
            /// </summary>
            public object Clone()
            {
                return new DiffItem()
                {
                    BaseLineCount = this.BaseLineCount,
                    BaseStartLineNumber = this.BaseStartLineNumber,
                    DiffLineCount = this.DiffLineCount,
                    DiffType = this.DiffType,
                };
            }
        }

        /// <summary>
        /// Returns an encoder for a given file name.
        /// </summary>
        static ILineEncoder GetEncoderForFile(string fileName)
        {
            ILineEncoder encoder = null;

            int extIndex = fileName.LastIndexOf('.');
            if (extIndex != -1)
            {
                string ext = fileName.Substring(extIndex + 1).ToLowerCultureInvariant();
                string highlighterPath = System.Configuration.ConfigurationSettings.AppSettings["encoder_" + ext];
                if (highlighterPath != null)
                {
                    Assembly encoderAssem = Assembly.LoadFrom(highlighterPath);
                    Type encoderType = encoderAssem.GetType("LineEncoderFactory");
                    ILineEncoderFactory factory = (ILineEncoderFactory)Activator.CreateInstance(encoderType);
                    encoder = factory.GetLineEncoder(ext);
                }
            }

            if (encoder == null)
                encoder = new DefaultEncoder();

            return encoder;
        }

        /// <summary>
        /// A small enum to indicate the role a file is playing in a comparison.
        /// </summary>
        enum BaseOrDiff
        {
            Base,
            Diff
        }

        /// <summary>
        /// Encapsulates information for a file being diff'ed.
        /// </summary>
        class DiffFileInfo
        {
            /// <summary>
            /// The stream from which to read the file's text.
            /// </summary>
            public StreamCombiner File;

            /// <summary>
            /// The encoder for displaying the file's text.
            /// </summary>
            public ILineEncoder Encoder;

            /// <summary>
            /// The file ID as found in the database.
            /// </summary>
            public int Id;

            /// <summary>
            /// The ID used in HTML for javascript to use.
            /// </summary>
            public string ScriptId;

            /// <summary>
            /// The comments associated with this file.
            /// </summary>
            public AbstractedComment[] Comments;

            /// <summary>
            /// The index in Comments of the next comment for either the current or a later line.
            /// </summary>
            public int NextCommentIndex;

            /// <summary>
            /// The text of the current line.
            /// </summary>
            public string CurLine;

            /// <summary>
            /// The line number for the current line.
            /// </summary>
            public int CurLineNum;

            /// <summary>
            /// Indicates if this file is the base or diff in the comparison.
            /// </summary>
            public BaseOrDiff BaseOrDiff;

            /// <summary>
            /// Creates a DiffFileInfo for a file being compared.
            /// </summary>
            /// <param name="file">The file stream.</param>
            /// <param name="encoder">The line encoder.</param>
            /// <param name="id">The file ID within the database.</param>
            /// <param name="comments">The array of comments for the file.</param>
            /// <param name="baseOrDiff">What role the file plays within the comparison.</param>
            public DiffFileInfo(
                StreamCombiner file,
                ILineEncoder encoder,
                int id,
                AbstractedComment[] comments,
                BaseOrDiff baseOrDiff)
            {
                File = file;
                Encoder = encoder;
                Id = id;
                ScriptId = baseOrDiff.ToString().ToLowerCultureInvariant() + "_" + Id.ToString() + "_";
                Comments = comments;
                //CurLineNum = 0;
                //NextCommentIndex = 0;
                BaseOrDiff = baseOrDiff;
            }

            /// <summary>
            /// Moves to the next line in the file.
            /// </summary>
            /// <returns>false if EOF is reached; true otherwise.</returns>
            public bool MoveNextLine()
            {
                string line = File.ReadLine();
                if (line != null)
                {
                    CurLine = line;
                    ++CurLineNum;
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }


        /// <summary>
        /// Returns a row that indicates that an unchanged portion of
        /// the file has been omitted.
        /// </summary>
        private TableGen.Row GetUnchangedLinesBreak(int columnCount)
        {
            var row = new TableGen.Row(1) { ApplyColumnStyles = false };
            row.AppendCSSClass("CssOmittedLinesNotice");
            row[0].ColumnSpan = columnCount;

            row[0].Add((IEnumerable<WebControl>)new WebControl[2]
            {
                new Label()
                {
                    Text = "Unchanged block omitted. "
                },
                new HyperLink()
                {
                    NavigateUrl = Request.Url.ToString() + "&showAllLines=true",
                    Text = "Show entire file",
                }
            });

            return row;
        }

        private struct LineAndComments
        {
            public int LineNum;
            public string LineText;
            public IList<AbstractedComment> Comments;
        }

        private static LineAndComments GetLineAndComments(DiffFileInfo file)
        {
            var res = new LineAndComments();
            res.LineNum = file.CurLineNum;
            res.LineText = file.CurLine;
            List<AbstractedComment> comments = null;

            while (file.NextCommentIndex < file.Comments.Length &&
                   file.Comments[file.NextCommentIndex].Line == res.LineNum)
            {
                if (comments == null)
                    comments =  new List<AbstractedComment>();
                comments.Add(file.Comments[file.NextCommentIndex++]);
            }

            if (!comments.IsNullOrEmpty())
                res.Comments = comments;

            return res;
        }

        /// <summary>
        /// Generates the diff view for two file revisions.
        /// </summary>
        /// <param name="baseFile"> The base file. </param>
        /// <param name="baseId"> The database id of the file. </param>
        /// <param name="baseComments"> The set of comments associated with the base file. </param>
        /// <param name="baseHeader"> The caption for the base file column. </param>
        /// <param name="diffFile"> The diff file. </param>
        /// <param name="diffId"> The database id of the diff. </param>
        /// <param name="diffComments"> The set of comments associated with the diff file. </param>
        /// <param name="diffHeader"> The caption for the changed file column. </param>
        /// <param name="baseIsLeft"> True if the base file is left column. </param>
        /// <param name="rawDiff"> Stream containing raw diff.exe output. </param>
        /// <param name="fileName"> The name of the file being compared. </param>
        private Control GenerateFileDiffView(
            StreamCombiner baseFile, int baseId, AbstractedComment[] baseComments, string baseHeader,
            StreamCombiner diffFile, int diffId, AbstractedComment[] diffComments, string diffHeader,
            bool baseIsLeft, StreamCombiner rawDiff, string fileName)
        {
            bool isSingleFileView = baseId == diffId;

            {   // Get user-configurable settings
                UserContext uc = CurrentUserContext;

                DiffViewOptions = new FileDiffViewOptions()
                {
                    IsBaseLeft = baseIsLeft,
                    IsUnified = isSingleFileView ? false : uc.UnifiedDiffView ?? false,
                    OmitUnchangedLines = (Request.QueryString["showAllLines"] ?? "false") != "true",
                    CommentClickMode = uc.CommentClickMode,
                    AutoCollapseComments = uc.AutoCollapseComments ?? true, // default to auto collapse
                };
            }

            ILineEncoder baseEncoder = GetEncoderForFile(fileName);
            ILineEncoder diffEncoder = GetEncoderForFile(fileName);

            Master.FindControl<Panel>("RootDivElement").Style[HtmlTextWriterStyle.Width] = "95%";

            #region View table initialization
            TableGen.Table fileView;
            if (isSingleFileView)
            {   // Single file view
                fileView = new TableGen.Table(new string[2] { "Num Base", "Txt Base" })
                {
                    ID = "fileview",
                    EnableViewState = false,
                    CssClass = "CssFileView CssFileViewSingle"
                };
                fileView.ColumnGroup.ColumnNameIndexMap = new KeyValuePair<string, int>[4]
                {
                    new KeyValuePair<string, int>("Num Base", 0),
                    new KeyValuePair<string, int>("Txt Base", 1),
                    new KeyValuePair<string, int>("Num Diff", 0),
                    new KeyValuePair<string, int>("Txt Diff", 1),
                };
            }
            else if (DiffViewOptions.IsSplit)
            {   // Split file diff
                fileView = new TableGen.Table(new string[4]
                {
                    "Num " + (DiffViewOptions.IsBaseLeft ? "Base" : "Diff"),
                    "Txt " + (DiffViewOptions.IsBaseLeft ? "Base" : "Diff"),
                    "Num " + (DiffViewOptions.IsBaseLeft ? "Diff" : "Base"),
                    "Txt " + (DiffViewOptions.IsBaseLeft ? "Diff" : "Base"),
                });
            }
            else
            {   // Inline file diff
                fileView = new TableGen.Table(new string[3]
                {
                    "Num " + (DiffViewOptions.IsBaseLeft ? "Base" : "Diff"),
                    "Num " + (DiffViewOptions.IsBaseLeft ? "Diff" : "Base"),
                    "Txt",
                });
                fileView.ColumnGroup.ColumnNameIndexMap = new KeyValuePair<string, int>[4]
                {
                    new KeyValuePair<string, int>("Num " + (DiffViewOptions.IsBaseLeft ? "Base" : "Diff"), 0),
                    new KeyValuePair<string, int>("Num " + (DiffViewOptions.IsBaseLeft ? "Diff" : "Base"), 1),
                    new KeyValuePair<string, int>("Txt Base", 2),
                    new KeyValuePair<string, int>("Txt Diff", 2),
                };
            }

            fileView.AppendCSSClass("CssFileView");
            if (!isSingleFileView)
                fileView.AppendCSSClass(DiffViewOptions.IsSplit ? "CssFileViewSplit" : "CssFileViewUnified");
            fileView.EnableViewState = false;
            fileView.ID = "fileview";
            fileView.Attributes["maxLineLen"] = MaxLineLength.ToString();

            AddJScriptCreateCommentOnClickAttribute(fileView);

            // Add the table header
            var fileViewHeaderGroup = fileView.CreateRowGroup();
            fileViewHeaderGroup.IsHeader = true;
            fileView.Add(fileViewHeaderGroup);

            var fileViewHeader = new TableGen.Row(isSingleFileView ? 1 : 2);
            fileViewHeader[0].ColumnSpan = 2;

            fileViewHeader[DiffViewOptions.IsSplit ? 0 : 1].Add(new HyperLink()
            {
                Text = baseHeader,
                NavigateUrl = Request.FilePath + "?vid=" + baseId,
            });

            if (!isSingleFileView)
            {
                fileViewHeader[1].ColumnSpan = 2;
                fileViewHeader[1].Add(new HyperLink()
                {
                    Text = diffHeader,
                    NavigateUrl = Request.FilePath + "?vid=" + diffId,
                });
            }

            fileViewHeader.AppendCSSClass("Header");
            fileViewHeaderGroup.AddRow(fileViewHeader);

            #endregion

            var baseFileInfo = new DiffFileInfo(baseFile, baseEncoder, baseId, baseComments, BaseOrDiff.Base);
            var diffFileInfo = new DiffFileInfo(diffFile, diffEncoder, diffId, diffComments, BaseOrDiff.Diff);

            int curRowNum = 1;
            int curRowGroupNum = 1;

            string baseScriptIdPrefix = baseFileInfo.ScriptId;
            string diffScriptIdPrefix = diffFileInfo.ScriptId;

            foreach (var diffItem in DiffItem.EnumerateDifferences(rawDiff))
            {
                bool atStart = diffItem.BaseStartLineNumber == 1;
                bool atEnd = diffItem.BaseLineCount == int.MaxValue;

                var baseLines = new List<LineAndComments>();
                for (int i = 0; i < diffItem.BaseLineCount && baseFileInfo.MoveNextLine(); ++i)
                    baseLines.Add(GetLineAndComments(baseFileInfo));

                var diffLines = new List<LineAndComments>();
                if (isSingleFileView)
                {
                    diffLines = baseLines;
                }
                else
                {
                    for (int i = 0; i < diffItem.DiffLineCount && diffFileInfo.MoveNextLine(); ++i)
                        diffLines.Add(GetLineAndComments(diffFileInfo));
                }

                var baseLinesLength = baseLines.Count();
                var diffLinesLength = diffLines.Count();

                // The end is the only case where the DiffInfo line counts may be incorrect. If there are in fact
                // zero lines then just continue, which should cause the foreach block to end and we'll continue
                // like the DiffItem never existed.
                if (atEnd && diffItem.DiffType == DiffType.Unchanged && baseLinesLength == 0 && diffLinesLength == 0)
                    continue;

                var curGroup = fileView.CreateRowGroup();
                curGroup.AppendCSSClass(diffItem.DiffType.ToString());
                curGroup.ID = "rowgroup" + (curRowGroupNum++).ToString();
                fileView.AddItem(curGroup);

                var numPasses = 1;
                if (DiffViewOptions.IsUnified && diffItem.DiffType != DiffType.Unchanged)
                    numPasses = 2;

                for (int pass = 1; pass <= numPasses; ++pass)
                {
                    int lastLineWithComment = 0;
                    int nextLineWithComment = 0;
                    for (int i = 0; i < Math.Max(baseLinesLength, diffLinesLength); ++i)
                    {
                        var row = curGroup.CreateRow();
                        if (pass == 1)
                        {
                            if (DiffViewOptions.IsUnified && diffItem.DiffType != DiffType.Unchanged)
                                row.AppendCSSClass("Base");
                        }
                        else if (pass == 2)
                        {
                            Debug.Assert(DiffViewOptions.IsUnified);
                            if (DiffViewOptions.IsUnified && diffItem.DiffType != DiffType.Unchanged)
                                row.AppendCSSClass("Diff");
                        }

                        if (pass == 1)
                        {
                            // Check if we should omit any lines.
                            if (diffItem.DiffType == DiffType.Unchanged &&
                                DiffViewOptions.OmitUnchangedLines)
                            {
                                int contextLineCount = 50;
                                if (baseLinesLength >= ((atStart || atEnd) ? contextLineCount : contextLineCount*2))
                                {
                                    if (i >= nextLineWithComment)
                                    {
                                        lastLineWithComment = nextLineWithComment;
                                        if (isSingleFileView)
                                        {
                                            nextLineWithComment = baseLines.IndexOfFirst(x => !x.Comments.IsNullOrEmpty(), i);
                                        }
                                        else
                                        {
                                            nextLineWithComment = Math.Min(
                                                baseLines.IndexOfFirst(x => !x.Comments.IsNullOrEmpty(), i),
                                                diffLines.IndexOfFirst(x => !x.Comments.IsNullOrEmpty(), i));
                                        }
                                    }
                                    if (((atStart && i == 0) || (i - lastLineWithComment == contextLineCount)) &&
                                        ((atEnd && nextLineWithComment == baseLinesLength) || (nextLineWithComment - i > contextLineCount)))
                                    {
                                        // Skip a bunch of lines!
                                        row = GetUnchangedLinesBreak(fileView.ColumnCount);
                                        row.ID = "row" + (curRowNum++).ToString();
                                        curGroup.AddItem(row);
                                        i = nextLineWithComment - ((atEnd && nextLineWithComment == baseLinesLength) ? 0 : 50);
                                        continue;
                                    }
                                }
                            }
                        }

                        if (i < baseLinesLength && pass == 1)
                        {
                            string scriptId = baseScriptIdPrefix + baseLines[i].LineNum.ToString();
                            row["Num Base"].ID = scriptId + "_linenumber";
                            row["Num Base"].Text = baseLines[i].LineNum.ToString();
                            row["Txt Base"].ID = scriptId;
                            row["Txt Base"].Add(EncodeLineTextAndComments(baseFileInfo.Encoder, "base", baseLines[i]));
                        }
                        if (i < diffLinesLength && !isSingleFileView)
                        {
                            string scriptId = diffScriptIdPrefix + diffLines[i].LineNum.ToString();
                            if (DiffViewOptions.IsSplit || pass == 2 || (pass == 1 && diffItem.DiffType == DiffType.Unchanged))
                            {
                                row["Num Diff"].ID = scriptId + "_linenumber";
                                row["Num Diff"].Text = diffLines[i].LineNum.ToString();
                            }
                            if (DiffViewOptions.IsSplit || pass == 2)
                            {
                                row["Txt Diff"].ID = scriptId;
                                row["Txt Diff"].Add(EncodeLineTextAndComments(diffFileInfo.Encoder, "diff", diffLines[i]));
                            }
                        }
                        row.ID = "row" + (curRowNum++).ToString();
                        curGroup.AddItem(row);
                    }
                }
            }

            encoderStyles = baseEncoder.GetEncoderCssStream();

            baseEncoder.Dispose();
            diffEncoder.Dispose();

            return fileView;
        }

        /// <summary>
        /// Will add the attribute to trigger a call to onMouseClick in comments.js.
        /// </summary>
        /// <param name="ctrl">Control to add the attribute to.</param>
        private void AddJScriptCreateCommentOnClickAttribute(WebControl ctrl)
        {
            bool clickLineNumber = DiffViewOptions.CommentClickMode == CommentClickMode.ClickLineNumber;
            ctrl.Attributes.Add(
                DiffViewOptions.CommentClickMode == CommentClickMode.DoubleClickAnywhere ? "ondblclick" : "onclick",
                "onMouseClick(event, " +
                    DiffViewOptions.AutoCollapseComments.ToString().ToLowerCultureInvariant() + ", " +
                    clickLineNumber.ToString().ToLowerCultureInvariant() + ");");

        }

        /// <summary>
        /// Computes the displayed name of the file version.
        /// Should be kept in sync with the AbstractFileVersion version.
        /// </summary>
        /// <param name="name"> The base name of a file (excluding the path). </param>
        /// <param name="version"> The version. </param>
        /// <returns> The string to display. </returns>
        private string ComputeMoniker(string name, FileVersion version)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(name);
            sb.Append('#');
            sb.Append(version.Revision.ToString());
            if (!version.IsRevisionBase)
            {
                if (version.TimeStamp != null)
                    sb.Append(" " + WrapTimeStamp(version.TimeStamp.Value) + " ");
                if (version.Action == (int)SourceControlAction.ADD)
                    sb.Append(" ADD ");
                else if (version.Action == (int)SourceControlAction.EDIT)
                    sb.Append(" EDIT ");
                else if (version.Action == (int)SourceControlAction.DELETE)
                    sb.Append(" DELETE ");
                else if (version.Action == (int)SourceControlAction.BRANCH)
                    sb.Append(" BRANCH ");
                else if (version.Action == (int)SourceControlAction.INTEGRATE)
                    sb.Append(" INTEGRATE ");
                else if (version.Action == (int)SourceControlAction.RENAME)
                    sb.Append(" RENAME ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Computes the displayed name of the file version.
        /// Should be kept in sync with the FileVersion version.
        /// </summary>
        /// <param name="name"> The base name of a file (excluding the path). </param>
        /// <param name="version"> The version. </param>
        /// <returns> The string to display. </returns>
        private string ComputeMoniker(string name, AbstractedFileVersion version)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(name);
            sb.Append('#');
            sb.Append(version.Revision.ToString());
            if (!version.IsRevisionBase)
            {
                if (version.TimeStamp != null)
                    sb.Append(" " + WrapTimeStamp(version.TimeStamp.Value) + " ");
                if (version.Action == SourceControlAction.ADD)
                    sb.Append(" ADD ");
                else if (version.Action == SourceControlAction.EDIT)
                    sb.Append(" EDIT ");
                else if (version.Action == SourceControlAction.DELETE)
                    sb.Append(" DELETE ");
                else if (version.Action == SourceControlAction.BRANCH)
                    sb.Append(" BRANCH ");
                else if (version.Action == SourceControlAction.INTEGRATE)
                    sb.Append(" INTEGRATE ");
                else if (version.Action == SourceControlAction.RENAME)
                    sb.Append(" RENAME ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Produces a stream for a file version. This may involve reading the database for the base for the version,
        /// if the version is a diff.
        /// </summary>
        /// <param name="version"> The version to stream. </param>
        /// <returns> The resultant full-text stream. </returns>
        private StreamCombiner GetFileStream(FileVersion version)
        {
            if (version.IsFullText)
                return new StreamCombiner(version.Text);

            var baseVersionQuery = from fl in DataContext.FileVersions
                                   where fl.FileId == version.FileId && fl.Revision == version.Revision && fl.IsRevisionBase
                                   select fl;
            if (baseVersionQuery.Count() != 1)
            {
                ErrorOut("Base revision not found. This can happen if the file changed type from binary to text " +
                    "as part of this change. If this is not the case, it might be a bug - report it!");
                return null;
            }

            FileVersion baseVersion = baseVersionQuery.Single();

            return new StreamCombiner(baseVersion.Text, version.Text);
        }

        /// <summary>
        /// Saves the text of a file version to a temp file.
        /// </summary>
        /// <param name="version"> The version to save. </param>
        /// <returns> The name of the file, or null if it fails. </returns>
        private TempFile SaveToTempFile(FileVersion version)
        {
            StreamCombiner reader = GetFileStream(version);
            if (reader == null)
                return null;

            TempFile file = new TempFile();

            using (reader)
            using (StreamWriter writer = new StreamWriter(file.FullName))
            {
                foreach (string line in reader.ReadLines())
                    writer.WriteLine(line);
            }

            // We created the temp file, but because of the security settings for %TEMP% might not have access to it.
            // Grant it explicitly.
            AddFileSecurity(file.FullName, WellKnownSidType.AuthenticatedUserSid, FileSystemRights.Read,
                AccessControlType.Allow);
            AddFileSecurity(file.FullName, WellKnownSidType.CreatorOwnerSid, FileSystemRights.FullControl,
                AccessControlType.Allow);

            return file;
        }

        /// <summary>
        /// Returns an array of comments for a given version. Comments either belong to a specific review id,
        /// or to all submitted comments against this version. The array is sorted according to line number, and then
        /// line stamp.
        /// </summary>
        /// <param name="fileVersionId"> The file to look up. </param>
        /// <param name="baseReviewId"> The base review id. </param>
        /// <returns></returns>
        private AbstractedComment[] GetComments(int fileVersionId, int baseReviewId)
        {
            var myCommentsQuery = from cm in DataContext.Comments
                                  where cm.FileVersionId == fileVersionId
                                  join rv in DataContext.Reviews on cm.ReviewId equals rv.Id
                                  where cm.ReviewId == baseReviewId || rv.IsSubmitted
                                  select new AbstractedComment(cm.FileVersionId, cm.Line, cm.LineStamp, rv.UserName,
                                      rv.TimeStamp, cm.ReviewId != baseReviewId, cm.CommentText);

            AbstractedComment[] comments = myCommentsQuery.ToArray();
            Array.Sort(comments, delegate(AbstractedComment c1, AbstractedComment c2)
            {
                int res = c1.Line.CompareTo(c2.Line);
                if (res == 0)
                    res = c1.LineStamp.CompareTo(c2.LineStamp);
                return res;
            });

            return comments;
        }

        /// <summary>
        /// Adds an ACL entry on the specified file for the specified account.
        /// </summary>
        /// <param name="fileName"> File name. </param>
        /// <param name="account"> The sid for the account. </param>
        /// <param name="rights"> What rights to grant (e.g. read). </param>
        /// <param name="controlType"> Grant or deny? </param>
        public static void AddFileSecurity(string fileName, WellKnownSidType account,
            FileSystemRights rights, AccessControlType controlType)
        {
            // Get a FileSecurity object that represents the
            // current security settings.
            FileSecurity fSecurity = File.GetAccessControl(fileName);

            // Added the FileSystemAccessRule to the security settings.
            fSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(account, null), rights, controlType));

            // Set the new access settings.
            File.SetAccessControl(fileName, fSecurity);
        }

        /// <summary>
        /// Computes and displays the diff between two distinct versions. Uses unix "diff" command to actually
        /// produce the diff. This is relatively slow operation and involves spooling the data into two temp
        /// files and running an external process.
        /// </summary>
        /// <param name="left"> File version on the left. </param>
        /// <param name="right"> File version on the right. </param>
        /// <param name="name"> Base name (no path) of the file for display purposes. </param>
        /// <param name="baseReviewId"> The id of the base review. </param>
        /// <param name="ignoreWhiteSpaces"> Whether to ignore white spaces. </param>
        private void DisplayDiff(FileVersion left, FileVersion right, string name, int baseReviewId, bool ignoreWhiteSpaces)
        {
            using (var leftFile = SaveToTempFile(left))
            using (var rightFile = SaveToTempFile(right))
            {
                if (leftFile == null)
                    return;

                if (rightFile == null)
                    return;

                string args = leftFile.FullName + " " + rightFile.FullName;
                if (ignoreWhiteSpaces && !string.IsNullOrEmpty(DiffArgsIgnoreWhiteSpace))
                    args = DiffArgsIgnoreWhiteSpace + " " + args;

                if (!string.IsNullOrEmpty(DiffArgsBase))
                    args = DiffArgsBase + " " + args;

                string stderr = null;
                string result = null;

                using (Process diff = new Process())
                {
                    diff.StartInfo.UseShellExecute = false;
                    diff.StartInfo.RedirectStandardError = true;
                    diff.StartInfo.RedirectStandardOutput = true;
                    diff.StartInfo.CreateNoWindow = true;
                    diff.StartInfo.FileName = DiffExe;
                    diff.StartInfo.Arguments = args;
                    diff.Start();

                    result = Malevich.Util.CommonUtils.ReadProcessOutput(diff, false, out stderr);
                }

                if (!stderr.IsNullOrEmpty())
                {
                    ErrorOut("Diff failed.");
                    ErrorOut(stderr);

                    return;
                }

                using (StreamCombiner leftStream = new StreamCombiner(new StreamReader(leftFile.FullName)))
                using (StreamCombiner rightStream = new StreamCombiner(new StreamReader(rightFile.FullName)))
                using (StreamCombiner rawDiffStream = new StreamCombiner(result))
                {
                    ActivePage.Controls.Add(GenerateFileDiffView(
                        leftStream, left.Id, GetComments(left.Id, baseReviewId), ComputeMoniker(name, left),
                        rightStream, right.Id, GetComments(right.Id, baseReviewId), ComputeMoniker(name, right),
                        true, rawDiffStream, name));
                }
            }
        }

        /// <summary>
        /// Returns the id for the current open review for the given user, or 0 if it does not exist.
        /// </summary>
        /// <param name="userName"> The user name. </param>
        /// <param name="changeId"> The change list for which to retrieve the base review. </param>
        /// <returns> The id of the review, or 0. </returns>
        private int GetBaseReviewId(string userName, int changeId)
        {
            var reviewQuery = from rv in DataContext.Reviews
                              where rv.ChangeListId == changeId && rv.UserName == userName && !rv.IsSubmitted
                              select rv.Id;
            if (reviewQuery.Count() != 1)
                return 0;
            return reviewQuery.Single();
        }

        /// <summary>
        /// Creates a row of links with the next targets for the file view.
        /// </summary>
        /// <param name="file"> This file. </param>
        /// <param name="files"> All navigable files in the change list. </param>
        /// <param name="vid1"> Version Id for the left pane. </param>
        /// <param name="vid1"> Version Id for the right pane. </param>
        /// <param name="ignoreWhiteSpaces"> If true, the diff ignores white spaces. </param>
        /// <param name="tableIndex"> A unique id of the table. </param>
        private void BuildNavigationTable(ChangeFile file, IQueryable<ChangeFile> files, int vid1, int vid2,
            bool ignoreWhiteSpaces, int tableIndex)
        {
            var navBar = ActivePage.New<Panel>()
                .AppendCSSClass("CssNavBarHoriz CssFileViewNavBar");

            // Back to change ...
            navBar.Add(new HyperLink()
            {
                Text = "Back to change " + file.ChangeList.CL,
                NavigateUrl = Request.FilePath + "?cid=" + file.ChangeListId
            });

            // Next file drop down list
            if (files.Count() > 1)
            {
                var nextFileBar = navBar.New<Panel>();
                nextFileBar.Style[HtmlTextWriterStyle.Display] = "inline";

                nextFileBar.Add(new Label()
                {
                    Text = "NextFile: "
                });

                string dropDownId = "NextFile" + tableIndex;

                DropDownList list = new DropDownList()
                {
                    AutoPostBack = true,
                    ID = dropDownId,
                };
                nextFileBar.Add(list);

                list.SelectedIndexChanged += (sender, e) =>
                {
                    DropDownList nextFile = Content.FindControl<DropDownList>(dropDownId);
                    Response.Redirect(Request.FilePath + "?fid=" + nextFile.SelectedValue);
                };

                foreach (ChangeFile f in files)
                {
                    ListItem item = new ListItem();
                    item.Text = f.ServerFileName;
                    item.Value = f.Id.ToString();
                    list.Items.Add(item);
                    if (f.Id == file.Id)
                        item.Selected = true;
                }
            }

            // Ignore/show whitespace differences button
            if (vid1 != vid2 && !string.IsNullOrEmpty(DiffArgsIgnoreWhiteSpace))
            {
                var showHideWhiteSpace = navBar.Add(new HyperLink()
                {
                    Text = ignoreWhiteSpaces ? "Show all differences"
                                             : "Ignore white space differences",
                    NavigateUrl = Request.FilePath + "?fid=" + file.Id + "&vid1=" + vid1 + "&vid2=" + vid2 +
                                  (ignoreWhiteSpaces ? "" : "&difftype=ignorespace"),
                });
            }

            navBar.Add(new Label()
            {
                Text = "F7/F8 - Previous/Next change",
            });
        }

        /// <summary>
        /// Displays one or two versions of a file in a diff-like view.
        /// </summary>
        /// <param name="fid"> File id </param>
        /// <param name="userName"> User name </param>
        /// <param name="vid1"> Version id on the left. If 0, the base version. </param>
        /// <param name="vid2"> Version id on the right. If 0, the latest change. </param>
        /// <param name="ignoreWhiteSpace"> Whether the diff algorithm should ignore white space. </param>
        private void DisplayFile(int fid, string userName, int vid1, int vid2, bool ignoreWhiteSpace)
        {
            var fileQuery = from fl in DataContext.ChangeFiles where fl.Id == fid select fl;
            if (fileQuery.Count() != 1)
            {
                ErrorOut("No such file!");
                return;
            }

            HintsData.InDiffView = true;

            ChangeFile file = fileQuery.Single();

            AbstractedFileVersion[] versions = GetVersionsWithTextAbstract(fid);
            if (versions.Length == 0)
            {
                ErrorOut("No versions submitted for this file.");
                return;
            }

            if (vid1 == 0)
            {
                // By default, shows the diff between the last reviewed version of a file and the
                // latest version of that file.
                var latestReview = GetLatestUserReviewForChangeList(userName, file.ChangeListId);
                if (latestReview != null)
                {
                    foreach (var version in versions)
                    {
                        if (latestReview.TimeStamp.CompareTo(version.TimeStamp) > 0)
                            vid1 = version.Id;
                    }

                    // If the user has reviewed the latest change, they're probably taking another
                    // look, so show them the diff between the latest and latest-1 versions.
                    if (vid1 == versions[versions.Length - 1].Id && versions.Length > 1)
                        vid1 = versions[versions.Length - 2].Id;
                }

                // If no version picked above (because user has not reviewed the changelist)
                // then just show the user the diff of the entire file change history.
                if (vid1 == 0)
                    vid1 = versions[0].Id;
            }

            if (vid2 == 0)
                vid2 = versions[versions.Length - 1].Id;

            DisplayPageHeader(file.ServerFileName);

            // Enclosing div (used to get shrink-to-contents behaviour for inner div).
            var fileVersionsDivOuter = new Panel() { CssClass = "CssOuterDiv" };
            ActivePage.Controls.Add(fileVersionsDivOuter);

            var fileVersionsDiv = new Panel() { CssClass = "CssFileVersions" };
            fileVersionsDivOuter.Add(fileVersionsDiv);

            var fileVersionsFieldSet = new OptionSection(false) { Header = "File Versions" }
                .AddTo(fileVersionsDiv);

            string name = file.ServerFileName;
            int lastForwardSlash = name.LastIndexOf('/');
            if (lastForwardSlash >= 0)
                name = name.Substring(lastForwardSlash + 1);

            RadioButtonList leftList = new RadioButtonList();
            fileVersionsFieldSet.Body.Add(leftList);
            leftList.ID = "LeftFileVersion";
            leftList.Items.AddRange(
                (from ver in versions
                 select new ListItem(ComputeMoniker(name, ver), ver.Id.ToString()) { Selected = ver.Id == vid1 }).ToArray());

            RadioButtonList rightList = new RadioButtonList();
            fileVersionsFieldSet.Body.Add(rightList);
            rightList.ID = "RightFileVersion";
            rightList.Items.AddRange(
                (from ver in versions
                 select new ListItem(ComputeMoniker(name, ver), ver.Id.ToString()) { Selected = ver.Id == vid2 }).ToArray());

            if (versions.Length > 1)
                HintsData.HaveMultipleVersions = true;

            var selectVersionsPanel = new Panel() { CssClass = "ButtonPanel" }
                .AddTo(fileVersionsFieldSet.Body);

            var selectVersions = new Button()
            {
                Text = "Select",
                ID = "selectversionsbutton",
                CssClass = "button"
            }.AddTo(selectVersionsPanel);
            selectVersions.Click += new EventHandler(selectVersions_Clicked);

            var filesQuery = (from fl in DataContext.ChangeFiles
                              where fl.ChangeListId == file.ChangeListId
                              join vr in DataContext.FileVersions on fl.Id equals vr.FileId
                              where vr.Text != null
                              orderby vr.Id
                              select fl).Distinct();

            AddLabel("<br><br>");
            BuildNavigationTable(file, filesQuery, vid1, vid2, ignoreWhiteSpace, 0);

            if (!Page.IsPostBack)
            {
                int baseReviewId = GetBaseReviewId(userName, file.ChangeListId);

                ClientScript.RegisterClientScriptBlock(this.GetType(), "username",
                    "<script type=\"text/javascript\">var username = \"" + userName + "\";</script>");

                if (vid1 == vid2) // The same file - diff will be null.
                {
                    var versionQuery = from vr in DataContext.FileVersions
                                       where vr.Id == vid1
                                       select vr;
                    if (versionQuery.Count() == 1)
                    {
                        FileVersion version = versionQuery.Single();
                        using (StreamCombiner text = GetFileStream(version))
                        {
                            if (text != null)
                            {
                                ActivePage.Controls.Add(GenerateFileDiffView(
                                    text, vid1, GetComments(vid1, baseReviewId), name,
                                    null, vid1, null, null,
                                    true, null, name));
                            }
                            else
                            {
                                ErrorOut("No file here...");
                            }
                        }
                    }
                    else
                    {
                        ErrorOut("Multiple files detected here. Suspect a bug - please report!");
                    }
                }
                else
                {
                    var versionQuery = from vr in DataContext.FileVersions
                                       where vr.Id == vid1 || vr.Id == vid2
                                       select vr;
                    if (versionQuery.Count() == 2)
                    {
                        FileVersion[] leftRight = versionQuery.ToArray();
                        FileVersion left, right;
                        if (leftRight[0].Id == vid1)
                        {
                            left = leftRight[0];
                            right = leftRight[1];
                        }
                        else
                        {
                            left = leftRight[1];
                            right = leftRight[0];
                        }

                        if (left.Revision == right.Revision)
                        {
                            //@TODO: I disabled this due to some issue I was having, but I can't remember what it
                            //       was. Need to sit down and figure out what the issue was.
                            //if (left.IsFullText && !right.IsFullText && !ignoreWhiteSpaces)
                            //{
                            //    StreamCombiner leftReader = new StreamCombiner(left.Text);
                            //    StreamCombiner rightReader = new StreamCombiner(left.Text, right.Text);
                            //    StreamCombiner rawDiffReader = new StreamCombiner(right.Text);
                            //    ActivePage.Controls.Add(GenerateFileDiffView(
                            //        leftReader, vid1, GetComments(vid1, baseReviewId),
                            //        ComputeMoniker(name, left), rightReader, vid2, GetComments(vid2, baseReviewId),
                            //        ComputeMoniker(name, right), true, rawDiffReader, name));
                            //    leftReader.Close();
                            //    rightReader.Close();
                            //    rawDiffReader.Close();
                            //}
                            //else if (!left.IsFullText && right.IsFullText && !ignoreWhiteSpaces)
                            //{
                            //    StreamCombiner leftReader = new StreamCombiner(right.Text, left.Text);
                            //    StreamCombiner rightReader = new StreamCombiner(right.Text);
                            //    StreamCombiner rawDiffReader = new StreamCombiner(left.Text);
                            //    ActivePage.Controls.Add(GenerateFileDiffView(
                            //        rightReader, vid2, GetComments(vid2, baseReviewId),
                            //        ComputeMoniker(name, right), leftReader, vid1, GetComments(vid1, baseReviewId),
                            //        ComputeMoniker(name, left), false, rawDiffReader, name));
                            //    leftReader.Close();
                            //    rightReader.Close();
                            //    rawDiffReader.Close();
                            //}
                            //else
                            {
                                DisplayDiff(left, right, name, baseReviewId, ignoreWhiteSpace);
                            }
                        }
                        else
                        {
                            DisplayDiff(left, right, name, baseReviewId, ignoreWhiteSpace);
                        }
                    }
                    else
                    {
                        ErrorOut("Could not get the two files. Either a bug, or incorrect version specified!");
                    }
                }

                AddLabel("<br>");
            }
            BuildNavigationTable(file, filesQuery, vid1, vid2, ignoreWhiteSpace, 1);
        }

        /// <summary>
        /// Downloads a version of a file.
        /// </summary>
        /// <param name="fid"> File Id. </param>
        /// <param name="vid"> Version Id. </param>
        private void DownloadFile(int vid)
        {
            var versionQuery = from vr in DataContext.FileVersions
                               where vr.Id == vid
                               select vr;
            if (versionQuery.Count() != 1)
            {
                ErrorOut("No file here...");
                return;
            }

            FileVersion version = versionQuery.Single();
            using (var tempFile = SaveToTempFile(version))
            {
                if (tempFile == null || !File.Exists(tempFile.FullName))
                {
                    ErrorOut(string.Format("Temp file {0} found...", tempFile.FullName));
                    return;
                }

                string name = version.ChangeFile.ServerFileName;
                int lastForwardSlash = name.LastIndexOf('/');
                if (lastForwardSlash >= 0)
                    name = name.Substring(lastForwardSlash + 1);

                //@TODO: Need to figure out when to delete this file.
                tempFile.ShouldDelete = false;

                Response.Clear();
                Response.AddHeader("Content-Disposition", "attachment; filename=" + name);
                Response.AddHeader("Content-Length", (new FileInfo(tempFile.FullName)).Length.ToString());
                Response.ContentType = "application/octet-stream";
                Response.WriteFile(tempFile.FullName);
                Response.Flush();
                Response.End(); // **WARNING** this method ends all further page processing. No code for the
                                //             current request will run after this is called. Think of this
                                //             as throwing a ThreadAbort.
            }
        }

        /// <summary>
        /// Displays files from a change list by adding a row with details regarding the file to a table.
        /// </summary>
        /// <param name="file"> The top-level file data (names). </param>
        /// <param name="lastVersion"> The last version of the file. </param>
        /// <param name="hasTextBody"> Whether to display the file as a hyperlink </param>
        /// <param name="latestReview">The latest review that the user has submitted for this changelist. May be null.</param>
        private TableRow GetChangeFileRow(
            DataModel.ChangeFile file,
            AbstractedFileVersion lastVersion,
            bool hasTextBody,
            Review latestReview)
        {
            TableRow row = new TableRow();
            if (!file.IsActive)
                row.AppendCSSClass("CssInactiveFile");

            TableCell cell = new TableCell();
            // If the latest version of this file has a timestamp greater than the latest review,
            // then present a "new!" icon in the list to allow the user to quickly determine what
            // has changed since they last reviewed this change.
            if (latestReview != null &&
                lastVersion.TimeStamp != null &&
                latestReview.TimeStamp.CompareTo(lastVersion.TimeStamp) < 0)
            {
                cell.AppendCSSClass("CssNewIcon");
                cell.Controls.Add(new Image()
                {
                    ImageUrl = "~/images/new_icon.png",
                    AlternateText = "This file has changed since your last review submission."
                });
            }
            row.Cells.Add(cell);

            cell = new TableCell();
            row.Cells.Add(cell);

            string moniker = null;
            if (lastVersion == null)
            {
                moniker = file.ServerFileName + "#" + " (no versions found)";
            }
            else if (lastVersion.Action == SourceControlAction.DELETE)
            {
                moniker = file.ServerFileName + "#" + lastVersion.Revision + " DELETE";
            }
            else if (lastVersion.Action == SourceControlAction.BRANCH)
            {
                moniker = file.ServerFileName + "#" + lastVersion.Revision + " BRANCH";
            }
            else if (lastVersion.Action == SourceControlAction.INTEGRATE)
            {
                moniker = file.ServerFileName + "#" + lastVersion.Revision + " INTEGRATE";
            }
            else if (lastVersion.Action == SourceControlAction.RENAME)
            {
                moniker = file.ServerFileName + "#" + lastVersion.Revision + " RENAME";
            }
            else
            {
                moniker = file.ServerFileName + "#" + lastVersion.Revision +
                    ((lastVersion.Action == SourceControlAction.ADD) ? " ADD" : " EDIT");
            }

            if (hasTextBody)
            {
                HyperLink link = new HyperLink();
                cell.Controls.Add(link);
                link.NavigateUrl = Request.FilePath + "?fid=" + file.Id;
                link.Text = moniker;
            }
            else
            {
                cell.Text = moniker;
            }

            return row;
        }

        /// <summary>
        /// Returns a row with title and body as two cells.
        /// </summary>
        /// <param name="title"> The text for the first cell (usually, a description). </param>
        /// <param name="body"> The text for the second cell (usually, the information). </param>
        private TableRow GetChangeDescriptionRow(string title, string body)
        {
            TableRow row = new TableRow();
            row.AppendCSSClass("CssTopAligned");

            TableCell cell = new TableCell() { Text = title };
            row.Cells.Add(cell);
            cell.AppendCSSClass("CssTitle");

            cell = new TableCell() { Text = "<pre>" + body + "</pre>" };
            row.Cells.Add(cell);
            cell.AppendCSSClass("CssBody");

            return row;
        }

        /// <summary>
        /// Gets the very last submitted review for a given user.
        /// </summary>
        /// <param name="changeListId"> Change list id (database-side). </param>
        /// <param name="userName"> User alias. </param>
        /// <returns> The review structure. </returns>
        private Review GetLastReview(int changeListId, string userName)
        {
            int? lastReviewId = (from rv in DataContext.Reviews
                                 where rv.ChangeListId == changeListId && rv.UserName == userName && rv.IsSubmitted
                                 orderby rv.Id
                                 select (int?)rv.Id).Max();
            if (lastReviewId == null)
                return null;
            var reviewQuery2 = from r2 in DataContext.Reviews where r2.Id == lastReviewId select r2;
            return reviewQuery2.Single();
        }

        /// <summary>
        /// Lists the comments made in various files.
        /// </summary>
        /// <param name="myReviewId"> The Id of the review to display. </param>
        /// <returns> Number of comments displayed. </returns>
        private int DisplayReviewLineComments(int myReviewId)
        {
            var commentsQuery = from cm in DataContext.Comments
                                where cm.ReviewId == myReviewId
                                join fv in DataContext.FileVersions on cm.FileVersionId equals fv.Id
                                orderby fv.FileId, cm.FileVersionId, cm.Line, cm.LineStamp
                                select cm;

            Comment[] comments = commentsQuery.ToArray();
            if (comments.Length == 0)
                return 0;

            HintsData.HaveCommentsByUser = true;

            var page = ActivePage.New<Panel>("CssReviewLineComments");
            Table table = null;

            int currentFileVersionId = 0;
            int currentLine = 0;
            foreach (Comment comment in comments)
            {
                if (currentFileVersionId != comment.FileVersionId)
                {
                    // Comments apply to next file in the review.
                    FileVersion version = comment.FileVersion;
                    string name = version.ChangeFile.ServerFileName;
                    name = name.Substring(name.LastIndexOf('/') + 1);
                    string url = Request.FilePath + "?fid=" + version.FileId +
                                 "&vid1=" + version.Id + "&vid2=" + version.Id;

                    HyperLink link = new HyperLink()
                        {
                            Text = ComputeMoniker(name, comment.FileVersion),
                            NavigateUrl = url,
                            CssClass = "CssFileName"
                        };

                    page.Add(link);

                    currentFileVersionId = version.Id;
                    currentLine = 0;

                    table = page.New<Table>();
                }

                var row = table.New<TableRow>();
                var lineCell = row.New<TableCell>("CssLineNumber");

                if (currentLine != comment.Line)
                {
                    currentLine = comment.Line;
                    lineCell.Text = "Line " + currentLine + ":";
                }

                row.New<TableCell>("CssLineComment").Add(Server.HtmlEncode(comment.CommentText).AsDiv());
            }

            return comments.Length;
        }

        /// <summary>
        /// Returns the latest review of a changelist by a particular user.
        /// </summary>
        /// <param name="userName">Reviewer</param>
        /// <param name="changeListId">Changelist</param>
        /// <returns>Latest review. Null if no such review exists.</returns>
        private Review GetLatestUserReviewForChangeList(string userName, int changeListId)
        {
            var reviewQuery = from r in DataContext.Reviews
                              where r.ChangeListId == changeListId
                              where r.UserName == userName
                              where r.IsSubmitted == true
                              orderby r.TimeStamp descending
                              select r;

            return reviewQuery.FirstOrDefault();
        }

        /// <summary>
        /// Displays files from a change list by adding a row with details regarding the file to a table.
        /// </summary>
        /// <param name="table"> Table where the data goes. </param>
        /// <param name="attachment"> The attachment. </param>
        private void AddAttachmentRow(Table page, Attachment attachment)
        {
            TableRow row = new TableRow();
            page.Rows.Add(row);

            TableCell cell = new TableCell();
            row.Cells.Add(cell);

            cell = new TableCell();
            row.Cells.Add(cell);
            cell.AppendCSSClass("CssAttachment");

            HyperLink link = new HyperLink();
            cell.Controls.Add(link);

            link.NavigateUrl = attachment.Link;
            link.Text = WrapTimeStamp(attachment.TimeStamp) + "&nbsp;&nbsp;" +
                (attachment.Description != null ? attachment.Description : Server.HtmlEncode(attachment.Link));
        }

        /// <summary>
        /// Displays the change list composition. This is called when the main table shows the details of one
        /// change list.
        /// </summary>
        /// <param name="cid"> Change Id. This is relative to the database, not source control. </param>
        /// <param name="userName"> User name. </param>
        private void DisplayChange(int cid, string userName)
        {
            var changeQuery = from ch in DataContext.ChangeLists where ch.Id == cid select ch;
            if (changeQuery.Count() != 1)
            {
                ErrorOut("Could not find this change in the system!");
                return;
            }

            HintsData.InChangeView = true;

            ChangeList changeList = changeQuery.Single();

            DisplayPageHeader("Change list " + changeList.CL);

            Table table = new Table();
            table.AppendCSSClass("CssChangeListDetail");

            ActivePage.Controls.Add(table);

            table.Rows.Add(GetChangeDescriptionRow("Date:", WrapTimeStamp(changeList.TimeStamp)));
            if (changeList.UserClient != null && changeList.UserClient != String.Empty)
                table.Rows.Add(GetChangeDescriptionRow("Client:", changeList.UserClient));
            var userRow = GetChangeDescriptionRow("User:", changeList.UserName);
            userRow.Cells[1].Add(new Label() { Text = userRow.Cells[1].Text });
            table.Rows.Add(userRow);
            table.Rows.Add(GetChangeDescriptionRow("Status:", changeList.Stage == 0 ? "Pending" : "Submitted"));
            if (changeList.Description != null && changeList.Description != String.Empty)
                table.Rows.Add(GetChangeDescriptionRow("Description:", Server.HtmlEncode(changeList.Description)));
            table.Rows.Add(GetChangeDescriptionRow("Files:", ""));

            var latestReview = GetLatestUserReviewForChangeList(userName, cid);
            foreach (ChangeFile file in
                (from fl in DataContext.ChangeFiles where fl.ChangeListId == cid select fl))
            {
                var versions = GetVersionsAbstract(file.Id);
                bool hasTextBody = (from ver in versions where ver.HasTextBody select ver).Count() != 0;
                table.Rows.Add(GetChangeFileRow(file, versions.LastOrDefault(), hasTextBody, latestReview));
            }

            var attachments = (from ll in DataContext.Attachments 
                               where ll.ChangeListId == cid
                               orderby ll.TimeStamp
                               select ll);

            if (attachments.Count() > 0)
            {
                table.Rows.Add(GetChangeDescriptionRow("Links:", ""));

                foreach (Attachment a in attachments)
                    AddAttachmentRow(table, a);
            }

            AddLabel("<h3>Review history</h3>");
            Table reviewResults = new Table();

            ActivePage.Controls.Add(reviewResults);

            reviewResults.AppendCSSClass("CssChangeListReviewHistory");

            var allReviewsQuery = from rr in DataContext.Reviews
                                  where rr.ChangeListId == cid && rr.IsSubmitted
                                  orderby rr.TimeStamp
                                  select rr;

            foreach (Review review in allReviewsQuery)
            {
                TableRow row = new TableRow();
                reviewResults.Rows.Add(row);

                row.AppendCSSClass("CssTopAligned");

                TableCell dateCell = new TableCell();
                row.Cells.Add(dateCell);

                dateCell.AppendCSSClass("CssDate");
                dateCell.Text = WrapTimeStamp(review.TimeStamp);

                TableCell nameCell = new TableCell();
                row.Cells.Add(nameCell);

                nameCell.AppendCSSClass("CssName");
                nameCell.Text = review.UserName;

                TableCell verdictCell = new TableCell();
                row.Cells.Add(verdictCell);

                verdictCell.AppendCSSClass("CssScore");

                HyperLink reviewTarget = new HyperLink();
                verdictCell.Controls.Add(reviewTarget);

                if (review.OverallStatus == 0)
                    HintsData.HaveNeedsWorkVotes = true;

                reviewTarget.Text = Malevich.Util.CommonUtils.ReviewStatusToString(review.OverallStatus);
                reviewTarget.NavigateUrl = Request.FilePath + "?rid=" + review.Id;

                if (review.CommentText != null)
                {
                    TableCell abbreviatedCommentCell = new TableCell();
                    row.Cells.Add(abbreviatedCommentCell);
                    abbreviatedCommentCell.AppendCSSClass("CssComment");
                    abbreviatedCommentCell.Text = AbbreviateToOneLine(review.CommentText, MaxReviewCommentLength);
                }
            }

			//Todo: modify stage logic, stage 1 is pending...
            if (changeList.Stage != 0)
            {
                HintsData.IsChangeInactive = true;

                AddLabel(String.Format("<br>This review has been {0}.", (changeList.Stage == 2 ? "closed" : "deleted")));
                return;
            }

            AddLabel("<h3>Vote so far</h3>");

            TableGen.Table reviewerVote = new TableGen.Table(2) { CssClass = "CssChangeListVoteHistory" };
            ActivePage.Controls.Add(reviewerVote);

            var reviewerQuery = from rv in DataContext.Reviewers where rv.ChangeListId == cid select rv;
            Reviewer[] reviewers = reviewerQuery.ToArray();
            bool iAmAReviewer = false;
            foreach (Reviewer reviewer in reviewers)
            {
                var row = reviewerVote.CreateRow();
                reviewerVote.AddItem(row);

                row[0].Text = reviewer.ReviewerAlias;

                if (userName.EqualsIgnoreCase(reviewer.ReviewerAlias))
                {
                    DropDownList list = new DropDownList() { ID = "verdictlist" };
                    row[1].Add(list);

                    list.Items.Add(new ListItem() { Text = "Needs work" });				
                    list.Items.Add(new ListItem() { Text = "LGTM with minor tweaks" });
                    list.Items.Add(new ListItem() { Text = "LGTM" });
                    list.Items.Add(new ListItem() { Text = "Non-scoring comment" });

                    iAmAReviewer = true;
                }
                else if (!Page.IsPostBack)
                {
                    Review review = GetLastReview(cid, reviewer.ReviewerAlias);
                    if (review == null)
                    {
                        row[1].Text = "Have not looked";
                    }
                    else
                    {
                        row[1].Add(new HyperLink()
                        {
                            Text = Malevich.Util.CommonUtils.ReviewStatusToString(review.OverallStatus),
                            NavigateUrl = Request.FilePath + "?rid=" + review.Id,
                        });
                    }
                }
            }

            bool iOwnTheChange = changeList.UserName.EqualsIgnoreCase(userName);
            if (!iAmAReviewer && !iOwnTheChange)
            {
                AddLabel("<br>");
                AddLink("I would like to review this change.", "?cid=" + changeList.Id + "&action=makemereviewer")
                    .AppendCSSClass("button");
            }

            if (iOwnTheChange)
            {
                HintsData.IsChangeAuthor = true;
                AddLabel("<h3>My response</h3>");
            }
            else
            {
                HintsData.IsChangeReviewer = true;
                AddLabel("<h3>My comments</h3>");
            }

            TextBox commentTextBox = new TextBox();
            ActivePage.Controls.Add(commentTextBox);
            commentTextBox.TextMode = TextBoxMode.MultiLine;
            commentTextBox.AppendCSSClass("CssGeneralCommentInputBox");
            commentTextBox.ID = "reviewcommenttextbox";

            AddLabel("<br>");

            Button submitReviewButton =
                new Button() { ID = "submitreviewbutton", Text = "Submit", CssClass = "button" };
            ActivePage.Controls.Add(submitReviewButton.As(HtmlTextWriterTag.P));
            submitReviewButton.Click += new EventHandler(
                delegate(object sender, EventArgs e) { submitReview_Clicked(cid, sender, e); });

            AddLabel("<br>");

            int myReviewId = GetBaseReviewId(userName, cid);
            if (myReviewId == 0)
                return;

            DisplayReviewLineComments(myReviewId);
        }

        /// <summary>
        /// Generates HTML to redirect the user to the change list table.
        /// </summary>
        /// <param name="cid"> The change list to show. </param>
        /// <param name="cl"> The name of the change list. </param>
        private void RedirectToChange(int cid, string cl)
        {
            AddLabel("<br>");
            AddLink("Click here to continue to " + cl, "?cid=" + cid);
            // This code does not work - it's a bug in IE. The meta tag gets generated,
            // and the browser redirects - only it redirects to the same - exactly the same
            // table with the same query parameters.
            // string url = Request.FilePath + "?cid=" + cid;
            // NewLabel("<br>Redirecting to change list " + cl + " in 10 seconds.<br>");
            // System.Web.UI.HtmlControls.HtmlMeta redirect = new System.Web.UI.HtmlControls.HtmlMeta();
            // Header.Controls.AddText(redirect);
            // redirect.HttpEquiv = "refresh";
            // redirect.Content = "5;" + url;
        }

        /// <summary>
        /// AddText a user to the list of reviewers.
        /// </summary>
        /// <param name="cid"> Change Id. </param>
        /// <param name="userName"> Alias of a user to add. </param>
        private void AddToReviewers(int cid, string userName)
        {
            var changeQuery = from ch in DataContext.ChangeLists where ch.Id == cid select ch;
            if (changeQuery.Count() != 1)
            {
                ErrorOut("Could not find this change in the system!");
                return;
            }

            ChangeList changeList = changeQuery.Single();
            if (changeList.Stage != 0)
            {
                AddLabel("This change list is no longer active.");
                RedirectToChange(cid, changeList.CL);
                return;
            }

            int? result = null;
            DataContext.AddReviewer(userName, cid, ref result);

            AddLabel("Thank you for agreeing to review this change list!");
            RedirectToChange(cid, changeList.CL);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="review"></param>
        /// <param name="change"></param>
        /// <param name="action"> If action is "confirm" we display Confirm/Cancel buttons. Otherwise it's
        ///     "Back to the change list" link. </param>
        /// <param name="uniquefier"> Postfix to make button ids unique. Needs to be different for every invokation
        ///     on the same table. </param>
        private void AddDisplayReviewControls(Review review, ChangeList change, string action, int uniquefier)
        {
            if ("confirm".EqualsIgnoreCase(action))
            {
                Table buttonTable = new Table();

                ActivePage.Add(("<b>Note:</b> After you submit the review, you will not be able to modify it, or any " +
                    "comments within. All new comments will go into a separate review which will be appended to the " +
                    "conversation.").As(HtmlTextWriterTag.P));

                Button confirm = new Button() { Text = "Confirm", CssClass = "button",  };
                confirm.Text = "Confirm";
                confirm.Click += new EventHandler(delegate(object sender, EventArgs e)
                {
                    DataContext.SubmitReview(review.Id);
					Response.Redirect(Request.FilePath);	// reload the dashboard
                });

                Button cancel = new Button() { Text = "Cancel", CssClass = "button" };
                cancel.Text = "Cancel";
                cancel.Click += new EventHandler(delegate(object sender, EventArgs e)
                {
                    Response.Redirect(Request.FilePath + "?cid=" + change.Id);
                });

                ActivePage.Add(new Panel() { CssClass = "CssButtonBar" }
                    .Add(confirm)
                    .Add(cancel)
                    .As(HtmlTextWriterTag.P));
            }
            else
            {
                AddLink("Back to change " + change.CL, "?cid=" + change.Id);
            }
        }

        /// <summary>
        /// Displays the review. If action is "confirm", allows user to submit or cancel it.
        /// </summary>
        /// <param name="rid"> Review id. </param>
        /// <param name="action"> Action - could be "confirm" to allow submitting,
        ///     anything else just dusplays the review.</param>
        /// <param name="userName"> User name. </param>
        private void DisplayReview(int rid, string action, string userName)
        {
            Review review = (from rv in DataContext.Reviews where rv.Id == rid select rv).FirstOrDefault();
            if (review == null)
            {
                ErrorOut("No such review!");
                return;
            }
            ChangeList change = review.ChangeList;
            DisplayPageHeader(review.UserName + " on CL " + change.CL + " -- " +
                Malevich.Util.CommonUtils.ReviewStatusToString(review.OverallStatus));
            ActivePage
                .Add(Server.HtmlEncode(review.CommentText)
                    .As(HtmlTextWriterTag.Div)
                    .AppendCSSClass("CssReviewSummary"));
            DisplayReviewLineComments(rid);
            AddDisplayReviewControls(review, change, action, 2);
        }

        /// <summary>
        /// Used to create alternating row shading, if desired, within the .css file.
        /// </summary>
        /// <param name="row">The table whose rows to apply the CssStyle to.</param>
        private static void ApplyRowBackgroundCssStyle(Table table)
        {
            int RowCount = 0;
            foreach (TableRow row in table.Rows)
            {
                if ((RowCount++ % 2) == 0)
                    row.AppendCSSClass("Even");
            }
        }

        /// <summary>
        /// Populates one row of data with the details of the passed change list.
        /// Used when displaying the list of change lists in review. Called for both
        /// the CLs where the user is the reviewer, as well as the reviewee.
        /// </summary>
        /// <param name="changeList"> The change list to process. </param>
        private TableRow GetReviewRow(ChangeList changeList, bool includeUserName, bool includeCloseButton = false)
        {
        	const int NeedsWork = 0, Closed = 2, NonScoringComment = 3, UnmarkedComment = 4;
    
			TableRow row = new TableRow();
            if (changeList.Stage != 0)
                row.AppendCSSClass("Closed");

			// Added by CBOBO
			if (!includeUserName && includeCloseButton)
			{
				if (changeList.Stage == Closed)
				{
					LiteralControl literalControl = new LiteralControl("Closed");
					TableCell cell = new TableCell();
					cell.Controls.Add(literalControl);
					cell.Width = 60;
					row.Cells.Add(cell);
				}
				else 
				{
					bool notPending = true;
					// Check if all reviews are Non-scoring comments or Unmarked Comments
					var submittedReviewList = changeList.Reviews.Where(rv => rv.IsSubmitted);
					if (submittedReviewList.Count() == 0
						|| submittedReviewList.All(rv => (rv.OverallStatus == NonScoringComment 
						|| rv.OverallStatus == UnmarkedComment))) 
					{
						row.Cells.Add(new TableCell() { CssClass = "Pending" }
							.Add("Pending".As(HtmlTextWriterTag.Span)));
						notPending = false;
					}
					else // Look for Needs work in reviews
					{
						var reviewers = changeList.Reviewers.ToList();
						foreach (var curReviewer in reviewers)
						{
							var curReviewList = submittedReviewList.Where(rv => rv.UserName == curReviewer.ReviewerAlias);

							if (curReviewList.Count() != 0	// No Reviews
								&& curReviewList.Last().OverallStatus == NeedsWork)
							{
								row.Cells.Add(new TableCell() { CssClass = "NeedsWork" }
									.Add("Needs Work".As(HtmlTextWriterTag.Span)));
								notPending = false;
								break;
							}
						}	
					}

					// Closable
					if (notPending)
					{
						HyperLink closeBtn = new HyperLink();
						closeBtn.NavigateUrl = Request.FilePath + "?cid=" + changeList.Id + "&action=close";
						closeBtn.Text = "[Close]";
						TableCell cell = new TableCell();
						cell.Controls.Add(closeBtn);
						cell.Width = 60;
						row.Cells.Add(cell);
					}
				}
			}
			// End Added by CBOBO

            // Time stamp
            row.Cells.Add(new TableCell() { CssClass = "ShortDate" }
				.Add(changeList.TimeStamp.ToShortDateString().As(HtmlTextWriterTag.Span)));

            // Author
			if (includeUserName)
            {
                row.Cells.Add(new TableCell() { CssClass = "Author" }
                    .Add(changeList.UserName.As(HtmlTextWriterTag.Span)));
            }

            // Change list ID
            row.Cells.Add(new TableCell() { CssClass = "ChangeListName" }
                .Add(new HyperLink()
                    {
                        NavigateUrl = Request.FilePath + "?cid=" + changeList.Id,
                        Text = Server.HtmlEncode(changeList.CL)
                    }.As(HtmlTextWriterTag.Div)));

            // Description
            row.Cells.Add(new TableCell() { CssClass = "Description" }
                .Add(Server.HtmlEncode(changeList.Description).As(HtmlTextWriterTag.Span)));

            return row;
        }

		/// <summary>
		/// Helper for Literal Control Cell Creation. Cell to be added into a row
		/// </summary>
		/// <param name="text">Lable for cell.</param>
		/// <param name="width">cell width.</param>
		/// <returns></returns>
		private TableCell CreateLiteralControlCell(String text, int width )
		{
			LiteralControl literalControl = new LiteralControl(text);
			TableCell cell = new TableCell();
			cell.Controls.Add(literalControl);
			cell.Width = width;
			return cell;
		}

        /// <summary>
        /// Common code for emitting sets of changes for use in the dashboard view.
        /// </summary>
        /// <param name="sectionTitle">Title of the section containing the table. Can be null for no title.</param>
        /// <param name="reviews">The reviews to emit into the table.</param>
        /// <param name="includeReviewOwner">Whether to include the review owner.</param>
        /// <returns></returns>
        private WebControl CreateReviewsSectionCommon(string sectionTitle, IEnumerable<ChangeList> reviews, bool includeReviewOwner, bool includeCoseButton = false)
        {
            Panel sectionDiv = new Panel() { CssClass = "ChangesSummarySection" };

            if (sectionTitle != null)
                sectionDiv.AddText("<p>" + sectionTitle + ":", "Title");

            Table table = new Table();
            sectionDiv.Controls.Add(table);

            foreach (ChangeList changeList in reviews)
                table.Rows.Add(GetReviewRow(changeList, includeReviewOwner, includeCoseButton));

            ApplyRowBackgroundCssStyle(table);

            return sectionDiv;
        }


        /// <summary>
        /// Common part for displaying the user dashboard.
        /// </summary>
        /// <param name="userName"> User name. </param>
        /// <param name="userIsMe"> Whether current user is the reader. </param>
        private void DisplayReviewsCommon(
            string userName,
            bool userIsMe,
            string usersChangesTitle,
            string usersPendingReviewsTitle,
            string usersCompletedReviewsTitle,
            string recentHistoryTitle)
        {
            DisplayPageHeader("Code reviews for " + userName);

            HintsData.InDashboard = true;

            DateTime historyThreshold = DateTime.Now.AddDays(-7);
            Dictionary<int, ChangeList> allClosedChangeLists = new Dictionary<int, ChangeList>();
            int sourceControlId = GetSourceControlId();

            var myChangesQuery = sourceControlId == -1 ?
                from mc in DataContext.ChangeLists
                where userName.Equals(mc.UserName) && (mc.Stage == 0 || mc.TimeStamp > historyThreshold)
                orderby mc.Stage ascending, mc.TimeStamp
                select mc :
                from mc in DataContext.ChangeLists
                where mc.SourceControlId == sourceControlId && userName.Equals(mc.UserName) &&
                    (mc.Stage == 0 || mc.TimeStamp > historyThreshold)
                select mc;

            ActivePage.Controls.Add(CreateReviewsSectionCommon(usersChangesTitle, myChangesQuery, false, true));

            var myReviewsQuery = sourceControlId == -1 ?
                from rv in DataContext.Reviewers
                where userName.Equals(rv.ReviewerAlias)
                join ch in DataContext.ChangeLists on rv.ChangeListId equals ch.Id
                where ch.Stage == 0 || ch.TimeStamp > historyThreshold
                orderby ch.TimeStamp descending
                select ch :
                from rv in DataContext.Reviewers
                where userName.Equals(rv.ReviewerAlias)
                join ch in DataContext.ChangeLists on rv.ChangeListId equals ch.Id
                where ch.SourceControlId == sourceControlId && (ch.Stage == 0 || ch.TimeStamp > historyThreshold)
                select ch;

            var myOpenReviewsQuery = from cl in myReviewsQuery
                                     where cl.Stage == 0
                                     select cl;

            var myPendingReviews = new List<ChangeList>();
            var myCompletedReviews = new List<ChangeList>();

            // This will split the set of active reviews into pending and complete for the user.
            // If a changelist has no review from a user or the review is earlier than the changelist
            // (which would happen if the dev updated the changelist based on review feedback) then
            // the review request is deemed as pending for the user. Otherwise, the review request
            // is deemed to be complete. This allows a user to quickly separate those review requests
            // that she has completed from those that are still awaiting her feedback.
            foreach (var curChangelist in myOpenReviewsQuery)
            {
                var latestReview = GetLatestUserReviewForChangeList(userName, curChangelist.Id);

                // Both timestamps should be UTC.
                if (latestReview != null && curChangelist.TimeStamp.CompareTo(latestReview.TimeStamp) < 0)
                {
                    if (curChangelist.TimeStamp > historyThreshold)
                        myCompletedReviews.Add(curChangelist);
                }
                else
                {
                    myPendingReviews.Add(curChangelist);
                }
            }

            ActivePage.Controls.Add(CreateReviewsSectionCommon(usersPendingReviewsTitle, myPendingReviews, false));
            ActivePage.Controls.Add(CreateReviewsSectionCommon(usersCompletedReviewsTitle, myCompletedReviews, false));

            var myClosedReviewsQuery = from cl in myReviewsQuery
                                       where cl.Stage != 0
                                       select cl;

            ActivePage.Controls.Add(CreateReviewsSectionCommon(recentHistoryTitle, myClosedReviewsQuery, false));
        }

        /// <summary>
        /// Displays all the changes by the user as well as the ones the user was asked to review.
        /// </summary>
        /// <param name="userName"> User name. </param>
        private void DisplayReviewsForUser(string userName)
        {
            DisplayReviewsCommon(
                userName,
                false,
                userName + "'s changes",
                userName + "'s pending reviews",
                userName + "'s completed reviews",
                userName + "'s recent history");
        }

        /// <summary>
        /// Displays all the changes by the user as well as the ones the user was asked to review.
        /// </summary>
        /// <param name="userName"> User name. </param>
        private void DisplayMyReviews(string userName)
        {
            DisplayReviewsCommon(
                userName,
                true,
                "My changes",
                "My pending reviews",
                "My completed reviews",
                "Recent history");
        }

        /// <summary>
        /// Displays currently active reviews for the team.
        /// </summary>
        private void DisplayAllOpenReviews()
        {
            DisplayPageHeader("Active code reviews");

            HintsData.InDashboard = true;

            Table page = new Table();
            ActivePage.Controls.Add(page);
            page.EnableViewState = false;

            int sourceControlId = GetSourceControlId();
            var allChangesQuery = sourceControlId != -1 ?
                from mc in DataContext.ChangeLists
                where mc.Stage == 0 && mc.SourceControlId == sourceControlId
                orderby mc.TimeStamp descending
                select mc :
                from mc in DataContext.ChangeLists
                where mc.Stage == 0
                orderby mc.TimeStamp descending
                select mc;

            ActivePage.Controls.Add(CreateReviewsSectionCommon(null, allChangesQuery, false));
        }

        /// <summary>
        /// Displays the review history (all reviews for the team).
        /// </summary>
        private void DisplayAllReviews()
        {
            DisplayPageHeader("Code review history");

            HintsData.InDashboard = true;

            Table page = new Table();
            ActivePage.Controls.Add(page);
            page.EnableViewState = false;

            int sourceControlId = GetSourceControlId();
            var allChangesQuery = sourceControlId != -1 ?
                from mc in DataContext.ChangeLists
                where mc.SourceControlId == sourceControlId
                orderby mc.TimeStamp descending
                select mc :
                from mc in DataContext.ChangeLists
                orderby mc.TimeStamp descending
                select mc;

            ActivePage.Controls.Add(CreateReviewsSectionCommon(null, allChangesQuery, false));
        }

        /// <summary>
        /// Displays review history for a user.
        /// </summary>
        /// <param name="alias"> User name for whom to display, or null if current user. </param>
        /// <param name="userName"> Current user's name. </param>
        /// <param name="author"> Whether it is for the author or for the reviewer. </param>
        private void DisplayReviewHistory(string alias, string userName, bool author)
        {
            if (alias != null)
                userName = alias;

            if (author)
                DisplayPageHeader("Change lists history for " + userName);
            else
                DisplayPageHeader("Reviews history for " + userName);

            HintsData.InDashboard = true;

            Table page = new Table();
            ActivePage.Controls.Add(page);
            page.EnableViewState = false;

            int sourceControlId = GetSourceControlId();
            var myChangesQuery = author ? (sourceControlId == -1 ?
                from mc in DataContext.ChangeLists
                where userName.Equals(mc.UserName)
                select mc :
                from mc in DataContext.ChangeLists
                where mc.SourceControlId == sourceControlId && userName.Equals(mc.UserName)
                select mc) : (sourceControlId == -1 ?
                from rv in DataContext.Reviews
                where userName.Equals(rv.UserName)
                join ch in DataContext.ChangeLists on rv.ChangeListId equals ch.Id
                where !userName.Equals(ch.UserName)
                select ch :
                from rv in DataContext.Reviews
                where userName.Equals(rv.UserName)
                join ch in DataContext.ChangeLists on rv.ChangeListId equals ch.Id
                where ch.SourceControlId == sourceControlId && !userName.Equals(ch.UserName)
                orderby ch.TimeStamp descending
                select ch).Distinct();

            ActivePage.Controls.Add(CreateReviewsSectionCommon(null, myChangesQuery, false));
        }

        /// <summary>
        /// Displays statistics
        /// </summary>
        /// <param name="sourceUrl"> URL to use for the back link. Can be null. </param>
        /// <param name="history"> What type of history to display. Can be null. </param>
        private void DisplayStats(string sourceUrl, string history)
        {
            DisplayPageHeader("Code review statistics");

            if (sourceUrl != null)
            {
                ActivePage.Controls.Add(
                    CreateLinkButton("Back...", Server.UrlDecode(sourceUrl)));

                AddLabel("<br><br>");
            }

            int activeReviews;
            int totalReviews;
            int totalFiles;
            int comments;

            int sourceControlId = GetSourceControlId();
            if (sourceControlId == -1)
            {
                activeReviews = (from cl in DataContext.ChangeLists where cl.Stage == 0 select cl).Count();
                totalReviews = (from cl in DataContext.ChangeLists select cl).Count();
                totalFiles = (from fl in DataContext.ChangeFiles select fl).Count();
                comments = (from cm in DataContext.Comments select cm).Count();
            }
            else
            {
                activeReviews = (from cl in DataContext.ChangeLists
                                 where cl.SourceControlId == sourceControlId && cl.Stage == 0
                                 select cl).Count();
                totalReviews = (from cl in DataContext.ChangeLists
                                where cl.SourceControlId == sourceControlId
                                select cl).Count();
                totalFiles = (from fl in DataContext.ChangeFiles
                              join cl in DataContext.ChangeLists on fl.ChangeListId equals cl.Id
                              where cl.SourceControlId == sourceControlId
                              select fl).Count();
                comments = (from cm in DataContext.Comments
                            join rv in DataContext.Reviews on cm.ReviewId equals rv.Id
                            join cl in DataContext.ChangeLists on rv.ChangeListId equals cl.Id
                            where cl.SourceControlId == sourceControlId
                            select cm).Count();
            }

            Table header = new Table();
            ActivePage.Controls.Add(header);

            TableRow row = new TableRow();
            header.Rows.Add(row);

            TableCell cell = new TableCell();
            cell.AppendCSSClass("CssStatsHeaderCell");
            row.Cells.Add(cell);
            cell.Text =
                "<b>Database stats:</b><br><br>" +
                "Active Code reviews: " + activeReviews + "<br>" +
                "Total Code reviews: " + totalReviews + "<br>" +
                "Files reviewed: " + totalFiles + "<br>" +
                "Comments submitted: " + comments;

            cell = new TableCell();
            cell.AppendCSSClass("CssStatsHeaderCell");
            row.Cells.Add(cell);

            var topSubmitters = sourceControlId == -1 ?
                DataContext.ExecuteQuery<StatQueryData>(
                    "SELECT TOP(5) * FROM (SELECT UserName, COUNT(*) AS Freq FROM ChangeList GROUP BY UserName) AS t " +
                    "ORDER BY Freq DESC") :
                DataContext.ExecuteQuery<StatQueryData>(
                    "SELECT TOP(5) * FROM (SELECT UserName, COUNT(*) AS Freq FROM ChangeList " +
                    "WHERE SourceControlId = {0} GROUP BY UserName) AS t ORDER BY Freq DESC", sourceControlId);

            StringBuilder sb = new StringBuilder("<b>Most CLs:</b><br><br>");
            foreach (StatQueryData stat in topSubmitters)
                sb.Append(stat.UserName + ": " + stat.Freq + "<br>");

            cell.Text = sb.ToString();

            cell = new TableCell();
            cell.AppendCSSClass("CssStatsHeaderCell");
            row.Cells.Add(cell);

            var topReviewers = sourceControlId == -1 ?
                DataContext.ExecuteQuery<StatQueryData>(
                    "SELECT TOP(5) * FROM (SELECT Review.UserName, COUNT(*) AS Freq FROM Review " +
                    "INNER JOIN ChangeList ON Review.ChangeListId = ChangeList.Id " +
                    "WHERE ChangeList.UserName <> Review.UserName " +
                    "GROUP BY Review.UserName) AS t " +
                    "ORDER BY Freq DESC") :
                DataContext.ExecuteQuery<StatQueryData>(
                    "SELECT TOP(5) * FROM (SELECT Review.UserName, COUNT(*) AS Freq FROM Review " +
                    "INNER JOIN ChangeList ON Review.ChangeListId = ChangeList.Id " +
                    "WHERE ChangeList.SourceControlId = {0} AND ChangeList.UserName <> Review.UserName " +
                    "GROUP BY Review.UserName) AS t " +
                    "ORDER BY Freq DESC", sourceControlId);

            sb = new StringBuilder("<b>Most reviews:</b><br><br>");
            foreach (StatQueryData stat in topReviewers)
                sb.Append(stat.UserName + ": " + stat.Freq + "<br>");

            cell.Text = sb.ToString();

            Table reviewsHeader = new Table();
            ActivePage.Controls.Add(reviewsHeader);
            TableRow reviewsHeaderRow = new TableRow();
            reviewsHeader.Rows.Add(reviewsHeaderRow);

            TableCell firstCell = new TableCell();
            reviewsHeaderRow.Cells.Add(firstCell);
            firstCell.AppendCSSClass("CssStatsHistoryHeader");
            TableCell secondCell = new TableCell();
            reviewsHeaderRow.Cells.Add(secondCell);
            secondCell.AppendCSSClass("CssStatsHistoryHeader");
            TableCell thirdCell = new TableCell();
            reviewsHeaderRow.Cells.Add(thirdCell);
            thirdCell.AppendCSSClass("CssStatsHistoryHeader");
            HyperLink second = new HyperLink();
            secondCell.Controls.Add(second);
            HyperLink third = new HyperLink();
            thirdCell.Controls.Add(third);

            DateTime? date = null;
            if ("lastweek".Equals(history))
            {
                date = DateTime.Now.AddDays(-7);

                firstCell.Text = "Reviews last week";

                second.Text = "Last month...";
                second.NavigateUrl = Request.FilePath + "?action=stats&history=lastmonth" +
                    (sourceUrl != null ? "&sourceUrl=" + Server.UrlEncode(sourceUrl) : "");

                third.Text = "Active...";
                third.NavigateUrl = Request.FilePath + "?action=stats" +
                    (sourceUrl != null ? "&sourceUrl=" + Server.UrlEncode(sourceUrl) : "");
            }
            else if ("lastmonth".Equals(history))
            {
                date = DateTime.Now.AddDays(-30);

                firstCell.Text = "Reviews last month";

                second.Text = "Last week...";
                second.NavigateUrl = Request.FilePath + "?action=stats&history=lastweek" +
                    (sourceUrl != null ? "&sourceUrl=" + Server.UrlEncode(sourceUrl) : "");

                third.Text = "Active...";
                third.NavigateUrl = Request.FilePath + "?action=stats" +
                    (sourceUrl != null ? "&sourceUrl=" + Server.UrlEncode(sourceUrl) : "");
            }
            else
            {
                firstCell.Text = "Active reviews";

                second.Text = "Last week...";
                second.NavigateUrl = Request.FilePath + "?action=stats&history=lastweek" +
                    (sourceUrl != null ? "&sourceUrl=" + Server.UrlEncode(sourceUrl) : "");

                third.Text = "Last month...";
                third.NavigateUrl = Request.FilePath + "?action=stats&history=lastmonth" +
                    (sourceUrl != null ? "&sourceUrl=" + Server.UrlEncode(sourceUrl) : "");
            }

            Table page = new Table();
            ActivePage.Controls.Add(page);
            page.EnableViewState = false;

            var allChangesQuery = date == null ?
                (sourceControlId != -1 ?
                from mc in DataContext.ChangeLists
                where mc.Stage == 0 && mc.SourceControlId == sourceControlId
                select mc :
                from mc in DataContext.ChangeLists
                where mc.Stage == 0
                orderby mc.TimeStamp descending
                select mc) :
                (sourceControlId != -1 ?
                from mc in DataContext.ChangeLists
                where mc.SourceControlId == sourceControlId && mc.TimeStamp > date
                select mc :
                from mc in DataContext.ChangeLists
                where mc.TimeStamp > date
                orderby mc.TimeStamp descending
                select mc);

            ActivePage.Controls.Add(CreateReviewsSectionCommon(null, allChangesQuery, true));

            ApplyRowBackgroundCssStyle(page);

            if (sourceUrl != null)
            {
                AddLabel("<br>");
                ActivePage.Controls.Add(
                    CreateLinkButton("Back...", Server.UrlDecode(sourceUrl)));
            }
        }

        /// <summary>
        /// Generates the table. This is the main entry point.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void Page_Load(object sender, EventArgs e)
        {
            if (!HttpContext.Current.User.Identity.IsAuthenticated)
            {
                ErrorOut("I do not know you");
                return;
            }

            string userName = HttpContext.Current.User.Identity.Name;
            int bs = userName.IndexOf('\\');
            if (bs != -1)
                userName = userName.Substring(bs + 1);

            AuthenticatedUserAlias = userName;

            // Top-right corner menu actions.
            string action = Request.QueryString["action"];
            if ("stats".EqualsIgnoreCase(action))
            {
                DisplayStats(Request.QueryString["sourceUrl"], Request.QueryString["history"]);
                return;
            }

            if ("settings".EqualsIgnoreCase(action))
            {
                DisplaySettings(Request.QueryString["sourceUrl"]);
                return;
            }

            // Change list search.
            string changeList = Request.QueryString["cl"];
            if (changeList != null)
            {
                int sourceControlId = GetSourceControlId();
                var cidQuery = sourceControlId == -1 ?
                    from c in DataContext.ChangeLists where c.CL.Equals(changeList) select c :
                    from c in DataContext.ChangeLists
                    where c.SourceControlId == sourceControlId && c.CL.Equals(changeList)
                    select c;
                int numberOfChanges = cidQuery.Count();
                if (numberOfChanges == 0)
                {
                    ErrorOut("Change list not found.");
                    return;
                }

                if (numberOfChanges > 1)
                {
                    AddLabel("Multiple changes found for CL " + changeList + " . Please pick one below:");
                    Table t = new Table();
                    foreach (ChangeList ch in cidQuery)
                    {
                        TableRow r = new TableRow();
                        t.Rows.Add(r);
                        TableCell c = new TableCell();
                        r.Cells.Add(c);
                        HyperLink l = new HyperLink();
                        c.Controls.Add(l);
                        l.NavigateUrl = Request.FilePath + "?cid=" + ch.Id;
                        l.Text = ch.CL;
                        c = new TableCell();
                        r.Cells.Add(c);
                        c.Text = ch.UserName;
                        c = new TableCell();
                        r.Cells.Add(c);
                        c.Text = AbbreviateToOneLine(ch.Description, MaxDescriptionLength);
                    }
                    return;
                }

                ChangeList change = cidQuery.Single();
                Response.Redirect(Request.FilePath + "?cid=" + change.Id);
                return;
            }

            // Change list display.
            string changeId = Request.QueryString["cid"];
            if (changeId != null)
            {
                int cid;
                if (!Int32.TryParse(changeId, out cid))
                {
                    ErrorOut("Change id specified incorrectly. Should be a number, but is not.");
                    return;
                }

				// Added by CBOBO
				if ("close".EqualsIgnoreCase(action))
				{
					DataContext.SubmitChangeList(cid);
					Response.Redirect(Request.FilePath); // reload the dashboard
					return;
				}
				// End Added by CBOBO


                if ("makemereviewer".EqualsIgnoreCase(action))
                    AddToReviewers(cid, userName);
                else
                    DisplayChange(cid, userName);
                return;
            }

            // File display.
            string fileId = Request.QueryString["fid"];
            if (fileId != null)
            {
                int fid;
                if (!Int32.TryParse(fileId, out fid))
                {
                    ErrorOut("File id specified incorrectly. Should be a number, but is not.");
                    return;
                }

                int vid1;
                if (!Int32.TryParse(Request.QueryString["vid1"], out vid1))
                    vid1 = 0;
                int vid2;
                if (!Int32.TryParse(Request.QueryString["vid2"], out vid2))
                    vid2 = 0;

                DisplayFile(fid, userName, vid1, vid2, "ignorespace".EqualsIgnoreCase(Request.QueryString["difftype"]));
                return;
            }

            // File download.
            string versionId = Request.QueryString["vid"];
            if (versionId != null)
            {
                int vid;
                if (!Int32.TryParse(versionId, out vid))
                    ErrorOut("Version id specified incorrectly. Should be a number, but is not.");
                else
                    DownloadFile(vid);
                return;
            }

            // Review.
            string reviewId = Request.QueryString["rid"];
            if (reviewId != null)
            {
                int rid;
                if (!Int32.TryParse(reviewId, out rid))
                {
                    ErrorOut("Review is specified incorrectly. Should be a number, but is not.");
                    return;
                }

                DisplayReview(rid, action, userName);
                return;
            }

            // Dashboard.
            string alias = Request.QueryString["alias"];
            if ("history".EqualsIgnoreCase(action))
            {
                // Historical views
                if ("*".Equals(alias))
                    DisplayAllReviews();
                else
                    DisplayReviewHistory(alias, userName, "author".EqualsIgnoreCase(Request.QueryString["role"]));
            }
            else
            {
                // Contemporary views.
                if ("*".Equals(alias))
                    DisplayAllOpenReviews();
                else if (alias != null)
                    DisplayReviewsForUser(alias);
                else
                    DisplayMyReviews(userName);
            }
        }

        private ContentPlaceHolder Content
        {
            get { return Master.Main; }
        }

        /// <summary>
        /// Saves the change in user prefs.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void changeUserPrefs_Clicked(object sender, EventArgs e)
        {

            UserContext uc = CurrentUserContext;

            RadioButtonList textFont = Content.FindControl<RadioButtonList>("TextFont");
            if (textFont != null && textFont.SelectedItem != null)
            {
                string font = textFont.SelectedItem.Value;
                uc.TextFont = font;
                DataContext.SetUserContext(UserContext.TEXT_FONT, font);
            }

            RadioButtonList textSize = Content.FindControl<RadioButtonList>("TextSize");
            string selection = textSize.SelectedItem.Value;

            int selectedTextSize = 0;
            if ("medium".Equals(selection))
                selectedTextSize = 1;
            else if ("large".Equals(selection))
                selectedTextSize = 2;

            int defaultTextSize = 0;

            if (uc.TextSize != null)
                defaultTextSize = uc.TextSize.Value;

            if (selectedTextSize != defaultTextSize)
            {
                uc.TextSize = selectedTextSize;
                DataContext.SetUserContext(UserContext.TEXT_SIZE, selectedTextSize.ToString());
            }

            TextBox lineLen = (TextBox)Content.FindControl("MaxLineLength");
            int liveLenValue;
            if (int.TryParse(lineLen.Text, out liveLenValue))
            {
                //@TODO: overload '0' to mean 'fit to screen'.
                if (liveLenValue < 80)
                    liveLenValue = 80;
                else if (liveLenValue > 160)
                    liveLenValue = 160;
            }
            else
            {
                liveLenValue = -1;
            }

            if (liveLenValue != -1 && (uc.MaxLineLength == null || uc.MaxLineLength.Value != liveLenValue))
            {
                uc.MaxLineLength = liveLenValue;
                DataContext.SetUserContext(UserContext.MAX_LINE_LENGTH, liveLenValue.ToString());
            }
            else if (liveLenValue == -1 && uc.MaxLineLength != null)
            {
                uc.MaxLineLength = null;
                DataContext.SetUserContext(UserContext.MAX_LINE_LENGTH, null);
            }

            TextBox tabs = (TextBox)Content.FindControl("SpacesPerTab");
            if (tabs != null)
            {
                string input = tabs.Text;
                if (String.IsNullOrEmpty(input))
                {
                    if (uc.SpacesPerTab != null)
                    {
                        uc.SpacesPerTab = null;
                        DataContext.SetUserContext(UserContext.SPACES_PER_TAB, null);
                    }
                }
                else if ("\\t".Equals(input))
                {
                    if (uc.SpacesPerTab == null || uc.SpacesPerTab.Value != -1)
                    {
                        uc.SpacesPerTab = -1;
                        DataContext.SetUserContext(UserContext.SPACES_PER_TAB, "-1");
                    }
                }
                else
                {
                    string[] input2 = input.Split(' ');
                    int value;
                    if (input2.Length == 2 && ("spaces".Equals(input2[1]) || "space".Equals(input2[1])) &&
                        int.TryParse(input2[0], out value) && value > 0 && value < 20)
                    {
                        if (uc.SpacesPerTab == null || uc.SpacesPerTab.Value != value)
                        {
                            uc.SpacesPerTab = value;
                            DataContext.SetUserContext(UserContext.SPACES_PER_TAB, value.ToString());
                        }
                    }
                }
            }

            RadioButtonList unifiedViewer = (RadioButtonList)Content.FindControl("UnifiedViewer");
            selection = unifiedViewer.SelectedItem.Value;
            bool unifiedViewerSav = uc.UnifiedDiffView != null ? uc.UnifiedDiffView.Value : false;
            bool unifiedViewerVal = false;
            if (selection.Equals("yes"))
                unifiedViewerVal = true;

            if (unifiedViewerVal != unifiedViewerSav)
            {
                uc.UnifiedDiffView = unifiedViewerVal;
                DataContext.SetUserContext(UserContext.UNIFIED_DIFF_VIEW, unifiedViewerVal.ToString());
            }

            {   // Comment click mode
                RadioButtonList commentClickMode = (RadioButtonList)Content.FindControl("CommentClickMode");
                CommentClickMode clickModeOrig = uc.CommentClickMode;
                uc.CommentClickMode = (CommentClickMode)
                    Enum.Parse(typeof(CommentClickMode), commentClickMode.SelectedItem.Value);

                if (uc.CommentClickMode != clickModeOrig)
                    DataContext.SetUserContext(UserContext.COMMENT_CLICK_MODE, uc.CommentClickMode.ToString());
            }

            {   // Empty comment auto collapse
                RadioButtonList autoCollapse = (RadioButtonList)Content.FindControl("AutoCollapseComments");
                selection = autoCollapse.SelectedItem.Value;
                bool? originalCollapseComments = uc.AutoCollapseComments;
                if (selection.Equals("no"))
                    uc.AutoCollapseComments = false;
                else
                    uc.AutoCollapseComments = true;

                if (uc.AutoCollapseComments != originalCollapseComments)
                    DataContext.SetUserContext(UserContext.AUTO_COLLAPSE_COMMENTS, uc.AutoCollapseComments.ToString());
            }

            RadioButtonList hints = (RadioButtonList)Content.FindControl("Hints");
            selection = hints.SelectedItem.Value;
            long hintValue = (uc.HintsMask != null) ? uc.HintsMask.Value : 0;
            long hintValueSav = hintValue;

            if (selection.Equals("no"))
                hintValue = -1;
            else if (selection.Equals("reset") || (selection.Equals("yes") && hintValue == -1))
                hintValue = 0;

            if (hintValue != hintValueSav)
            {
                uc.HintsMask = hintValue;
                DataContext.SetUserContext(UserContext.HINT_MASK, hintValue.ToString());
            }

            Response.Redirect(Server.UrlDecode(Request.QueryString["sourceUrl"]));
        }

        /// <summary>
        /// Processes the selection of different file version(s) for displaying in diff pane.
        /// Called during the postback. Redirects to a table with query string specifying the new file versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void selectVersions_Clicked(object sender, EventArgs e)
        {
            RadioButtonList left = Content.FindControl<RadioButtonList>("LeftFileVersion");
            int leftVid = Int32.Parse(left.SelectedItem.Value);
            RadioButtonList right = Content.FindControl<RadioButtonList>("RightFileVersion");
            int rightVid = Int32.Parse(right.SelectedItem.Value);

            Response.Redirect(Request.FilePath + "?fid=" + Request.QueryString["fid"] +
                "&vid1=" + leftVid + "&vid2=" + rightVid);
        }

        /// <summary>
        /// Submits the overall review. This is really called from inside a closure that is a true event handler.
        /// The idea is to mix the changeId in.
        /// </summary>
        /// <param name="changeId"> The cid of the change which review is being submitted. </param>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void submitReview_Clicked(int changeId, object sender, EventArgs e)
        {
			// Todo: Check if review is closed before submitting
            DropDownList verdictList = Content.FindControl<DropDownList>("verdictlist");
            int verdict = (verdictList != null) ? verdictList.SelectedIndex : 4;
            TextBox commentText = Content.FindControl<TextBox>("reviewcommenttextbox");
            int? reviewId = null;
            DataContext.AddReview(changeId, commentText.Text, (byte)verdict, ref reviewId);

            Response.Redirect(Request.FilePath + "?rid=" + reviewId + "&action=confirm");
        }

        /// <summary>
        /// Represents a font instance specified in the config file.
        /// </summary>
        class ConfigFont
        {
            /// <summary>
            /// The font name.
            /// </summary>
            public string Name;

            /// <summary>
            /// The small/med/large font vertical sizes.
            /// </summary>
            public int[] VSizes;

            /// <summary>
            /// The small/med/large font horizontal sizes.
            /// </summary>
            public int[] HSizes;
        }

        static List<ConfigFont> ConfigFonts;

        /// <summary>
        /// Renders the dynamic elements of the style. This is stricly the sizing part of the diff table at the moment.
        /// It only is used on that table.
        /// </summary>
        public void RenderStyle()
        {
            if (Request.QueryString["fid"] == null)
                return;

            if (ConfigFonts == null)
            {
                string configFontList = System.Configuration.ConfigurationSettings.AppSettings["fonts"];
                if (configFontList != null)
                {
                    string[] fonts = configFontList.Split(';');
                    var cfgFonts = new List<ConfigFont>(fonts.Length);
                    foreach (string font in fonts)
                    {
                        Match m = FontParser.Match(font);
                        if (!m.Success)
                            continue;

                        try
                        {
                            cfgFonts.Add(new ConfigFont()
                            {
                                Name = m.Groups["fontname"].Value,
                                VSizes = new int[3]
                            {
                                int.Parse(m.Groups["smallfontsizey"].Value),
                                int.Parse(m.Groups["medfontsizey"].Value),
                                int.Parse(m.Groups["largefontsizey"].Value),
                            },
                                HSizes = new int[3]
                            {
                                int.Parse(m.Groups["smallfontsizex"].Value),
                                int.Parse(m.Groups["medfontsizex"].Value),
                                int.Parse(m.Groups["largefontsizex"].Value),
                            },
                            });
                        }
                        catch (FormatException) { continue; }
                        catch (ArgumentNullException) { continue; }
                        catch (OverflowException) { continue; }
                    }
                    ConfigFonts = cfgFonts;
                }
                else
                {
                    var cfgFonts = new List<ConfigFont>(1);
                    cfgFonts.Add(new ConfigFont()
                    {
                        Name = DefaultFontFamily,
                        VSizes = new int[3]
                    {
                        DefaultSmallFontSizeVr,
                        DefaultMediumFontSizeVr,
                        DefaultLargeFontSizeVr,
                    }
                    });
                    ConfigFonts = cfgFonts;
                }
            }

            UserContext uc = CurrentUserContext;

            ConfigFont theFont = ConfigFonts[0];

            if (uc.TextFont != null)
            {
                var userFont = (from f in ConfigFonts
                                where f.Name.EqualsIgnoreCase(uc.TextFont)
                                select f).FirstOrDefault();
                if (userFont != null)
                    theFont = userFont;
            }

            Response.Write("<style>\n");

            //// MaxValue used to indicate "best fit".
            bool stretchToFit = MaxLineLength == int.MaxValue;

            var numColWidth = 6 * theFont.HSizes[uc.TextSize ?? 0] + 4;
            var txtColWidth = MaxLineLength * theFont.HSizes[uc.TextSize ?? 0] + 4;

            // Table width
            Response.Write(@"
table.CssFileViewSplit
{
    table-layout: fixed;
    width: " + (numColWidth * 2 + txtColWidth * 2).ToString() + @"px;
}
");
            // Line text width
            Response.Write(@"
table.CssFileView colgroup col.Txt,
table.CssFileView tr td.Txt,
table.CssFileView tr td.Txt pre
{
    width: " + txtColWidth.ToString() + @"px;
}
");

            // Line num width
            Response.Write(@"
table.CssFileView colgroup col.Num,
table.CssFileView tr td.Num
{
    width: " + numColWidth.ToString() + @"px;
}
");

            // Cell styles
            Response.Write(@"
table.CssFileView tr td.Txt,
table.CssFileView tr td.Num,
table.CssFileView tr td pre
{
font-family: " + theFont.Name + @";
font-size: " + theFont.VSizes[uc.TextSize ?? 0] + @"px;
}
");

            if (encoderStyles != null)
            {
                Response.Write(encoderStyles.ReadToEnd());
                encoderStyles.Close();
                encoderStyles = null;
            }

            Response.Write("</style>");
        }

        /// <summary>
        /// Inserts the header HTML file into the table. This allows skinning (branding) of Malevich to integrate it
        /// seamlessly into existing tool chain.
        /// 
        /// The text of the file are dumped in the very beginning of the table as is.
        /// </summary>
        public void RenderHeaderSkin()
        {
            string skinDir = System.Configuration.ConfigurationSettings.AppSettings["skinDirectory"];
            if (skinDir == null)
                return;

            string fileName = System.IO.Path.Combine(skinDir, "header.html");
            if (System.IO.File.Exists(fileName))
                Response.WriteFile(fileName);
        }

        /// <summary>
        /// Inserts the footer HTML file into the table. This allows skinning (branding) of Malevich to integrate it
        /// seamlessly into existing tool chain.
        /// 
        /// The text of the file are dumped in the very end of the table as is.
        /// </summary>
        public void RenderFooterSkin()
        {
            string skinDir = System.Configuration.ConfigurationSettings.AppSettings["skinDirectory"];
            if (skinDir == null)
                return;

            string fileName = System.IO.Path.Combine(skinDir, "footer.html");
            if (System.IO.File.Exists(fileName))
                Response.WriteFile(fileName);
        }

        /// <summary>
        /// Data for the javascript hints.
        /// </summary>
        public void RenderHintScriptSupport()
        {
            UserContext uc = CurrentUserContext;
            if (uc.HintsMask != null && uc.HintsMask == -1)
                return;

            List<string> allHints = new List<string>();

            if (HintsData.InDashboard)
            {
                allHints.Add("HINT_DASHBOARD_HELP");
                allHints.Add("HINT_DASHBOARD_ALIAS");
                allHints.Add("HINT_DASHBOARD_CL");
                allHints.Add("HINT_DASHBOARD_ANNOYED");
            }
            if (HintsData.InChangeView)
            {
                if (HintsData.HaveCommentsByUser)
                    allHints.Add("HINT_CHANGE_MUST_SUBMIT");
                if (HintsData.IsChangeAuthor && HintsData.HaveNeedsWorkVotes)
                    allHints.Add("HINT_CHANGE_NEEDS_WORK_NEEDS_WORK");
                if (HintsData.IsChangeAuthor)
                    allHints.Add("HINT_CHANGE_AUTHOR_SHOULD_RESPOND");
                if (!HintsData.IsChangeReviewer)
                    allHints.Add("HINT_CHANGE_MUST_BE_REVIEWER_TO_VOTE");
            }

            if (HintsData.InDiffView)
            {
                if (HintsData.HaveCommentsByOthers)
                    allHints.Add("HINT_FILEVIEW_CLICK_TO_RESPOND");
                if (HintsData.HaveMultipleVersions)
                    allHints.Add("HINT_FILEVIEW_SELECT_VERSION");

                allHints.Add("HINT_FILEVIEW_HOVER_TO_NAVIGATE");
                allHints.Add("HINT_FILEVIEW_CHANGE_TEXT_SIZE");
                allHints.Add("HINT_FILEVIEW_CLICK_A_LOT");
            }

            if (allHints.Count > 0)
            {
                Response.Write("<script type = \"text/javascript\">\n");
                Response.Write("HINT_MASK = " + (uc.HintsMask == null ? 0 : uc.HintsMask.Value) + ";\n\n");
                Response.Write("function displayHintsForThisPage()\n{\n    maybeDisplayHints(");
                int cnt = 0;
                foreach (string s in allHints)
                {
                    if (cnt != 0)
                        Response.Write(',');
                    Response.Write(s);
                    ++cnt;
                }
                Response.Write(");\n}\n\nsetTimeout(displayHintsForThisPage, 3000);\n");
                Response.Write("</script>\n");
            }
        }
    }
}