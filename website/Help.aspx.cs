using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;

using Malevich.Util;
using Malevich.Extensions;

public partial class Help : System.Web.UI.Page
{
    /// <summary>
    /// Creates and adds a new label to the active page.
    /// </summary>
    /// <param name="contents">The label contents.</param>
    /// <returns>The new label.</returns>
    private Label AddLabel(string contents)
    {
        return Master.Main.AddLabel(contents);
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
            CssClass = "button",
        };
    }

    private ContentPlaceHolder ActivePage
    { get { return Master.Main; } }

    /// <summary>
    /// Displays context-sensitive help.
    /// </summary>
    /// <param name="sourceUrl"> The URL from which the request came. </param>
    public void DisplayHelp(string sourceUrl)
    {
        DisplayPageHeader("Malevich help");

        string url = Server.HtmlDecode(sourceUrl);

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

        AddLabel("<br>");

        ActivePage.Controls.Add(
            CreateLinkButton("Back...", Server.UrlDecode(sourceUrl)));

    }
    protected void Page_Load(object sender, EventArgs e)
    {
        DisplayHelp(Request["sourceUrl"]);
    }
}
