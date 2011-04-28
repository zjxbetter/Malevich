//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Eldar Musaev & Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace ReviewNotifier
{
    /// <summary>
    /// Implements support for notifier email customizations.
    /// 
    /// To customize email that notifier sends, create the following text files, and drop them in the same directory
    /// where ReviewNotifier.exe lives: Request.txt, Invite.txt, Iteration.txt, Response.txt, and Subjects.txt.
    /// 
    /// The files should contain the text which is sent, respectively, when a reviewer is asked to join code review,
    /// when the code invitation is sent, when a reviewer makes comments, and when reviewee responds to the comments.
    /// 
    /// The files should contain the following macros which are replaced whith real values (macro names are
    /// case-sensitive):
    /// Request.txt:
    ///     {Reviewer} - the alias of the reviewer to which email is sent.
    ///     {Webserver} - the server name where Malevich web site lives.
    ///     {WebRoot} - the application's path on the site (e.g. Malevich)
    ///     {MalevichId} - the id of the change in Malevich.
    ///     {Reviewee} - the person requesting code review.
    ///     {Details} - the description of the change list.
    ///     
    ///     The default template:
    ///     
    ///     Dear {Reviewer},
    ///     
    ///     I would like a code review. Please go to http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}
    ///     to comment.
    ///     
    ///     If you are super busy and cannot do the review soon, kindly notify me immediately so
    ///     I could plan for contingencies.
    ///     
    ///       --  {Reviewee}
    ///     
    ///     ---------
    ///     
    ///     {Details}
    /// 
    /// Invite.txt:
    ///     {Webserver} - the server name where Malevich web site lives.
    ///     {WebRoot} - the application's path on the site (e.g. Malevich)
    ///     {MalevichId} - the id of the change in Malevich.
    ///     {Reviewee} - the person requesting code review.
    ///     {Details} - the description of the change list.
    ///     
    ///     The default template:
    ///     
    ///     Dear potential reviewer,
    ///     
    ///     I am in need of your professional opinion. Could you do a code review for me?
    ///     
    ///     To accept the review request (thank you! thank you! thank you!), click on this link:
    ///     http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}&action=makemereviewer
    ///     
    ///     To learn more about this change, please go to http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}
    ///     
    ///     You can add yourself as a reviewer there as well.
    ///     
    ///     Thank you very, very much!
    ///     
    ///       --  {Reviewee}
    ///     
    ///     ---------
    ///     
    ///     {Details}
    /// 
    /// Iteration.txt:
    ///     {Reviewee} - the person requesting code review.
    ///     {Verdict} - What is the final verdict (needs work/LGTM/etc). This is a full sentence supplied by the
    ///         notifier. 
    ///     {Webserver} - the server name where Malevich web site lives.
    ///     {WebRoot} - the application's path on the site (e.g. Malevich)
    ///     {MalevichId} - the id of the change in Malevich.
    ///     {Reviewer} - the alias of the reviewer to which email is sent.
    ///     {Details} - the list of comments (formatted my notifier) made by the reviewer
    /// 
    ///     The default template:
    ///     
    ///     Dear {Reviewee},
    ///     
    ///     Thank you for giving me the opportunity to review your code.
    ///     
    ///     {Verdict}
    ///     
    ///     Please see the detailed comments at http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}
    ///     
    ///       --  {Reviewer}
    ///     
    ///     ---------
    ///     
    ///     {Details}
    /// 
    /// 
    /// Response.txt:
    ///     {Webserver} - the server name where Malevich web site lives.
    ///     {WebRoot} - the application's path on the site (e.g. Malevich)
    ///     {MalevichId} - the id of the change in Malevich.
    ///     {Reviewee} - the person requesting code review.
    ///     {Details} - the list of comments (formatted my notifier) made by the reviewer
    /// 
    ///     The default template:
    ///     
    ///     Dear reviewers,
    /// 
    ///     Thank you for your comments. Please see my responses at
    ///     http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}
    ///     
    ///       -- {Reviewee}
    ///     
    ///     ---------
    ///     
    ///     {Details}
    /// 
    /// Reminder.txt:
    ///     {Webserver} - the server name where Malevich web site lives.
    ///     {WebRoot} - the application's path on the site (e.g. Malevich)
    ///     {MalevichId} - the id of the change in Malevich.
    ///     {CL} - change list.
    ///     {Reviewee} - the person requesting code review.
    ///     {Details} - the description of the change list.
    ///     
    ///     The default template:
    ///     
    ///     Dear {Reviewee},
    ///
    ///     Your code review (http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId})
    ///     for the change list {CL} is now very, very old!
    ///
    ///     Chances are that the change has already been submitted and you just need to close the review:
    ///         review close {CL}
    ///
    ///     Or delete it if the change has been abandoned:
    ///         review delete {CL}
    ///
    ///     Otherwise consider reminding your reviewers about it!
    ///
    ///       --  Malevich
    ///
    ///     ---------
    ///
    ///     {Details}
    ///
    ///  Subjects.txt:
    ///     {CL} - change list.
    ///     {Reviewee} - the person requesting code review.
    ///     {Details} - abbreviated change list description.
    ///     {Verdict} - the vote (only in the iteration template).
    ///  
    ///     The default template:
    ///     
    ///     REQUEST: Code review for CL {CL} by {Reviewee}
    ///     INVITE: Looking for reviewers for CL {CL} by {Reviewee}
    ///     RESPONSE: Code review for CL {CL} by {Reviewee}
    ///     ITERATION: Code review for CL {CL} by {Reviewee}
    ///     REMINDER: Code review for CL {CL} by {Reviewee}
    ///
    /// </summary>
    public sealed class MailTemplates
    {
        /// <summary>
        /// Types of email that can be sent.
        /// </summary>
        public enum MailType
        {
            Request = 0,
            Invite,
            Iteration,
            Response,
            Reminder
        }

        /// <summary>
        /// Review request. Sent when reviewer is explicitly assigned. (review 5151 alice)
        /// </summary>
        private const string Request = "Dear {Reviewer},\n\nI would like a code review. " +
            "Please go to http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId} to comment.\n\n" +
            "If you are super busy and cannot do the review soon, kindly notify me immediately so " +
            "I could plan for contingencies.\n\n  --  {Reviewee}\n\n---------\n\n{Details}";

        /// <summary>
        /// Subject for review request. Sent when reviewer is explicitly assigned. (review 5151 alice)
        /// </summary>
        private const string RequestSubject = "Code review for CL {CL} by {Reviewee}";

        /// <summary>
        /// Invitation to join the review. Sent when review is requested with --invite flag. (review 5151 --invite bob)
        /// </summary>
        private const string Invitation = "Dear potential reviewer,\n\nI am in need of your professional opinion. " +
            "Could you do a code review for me?\n\nTo accept the review request (thank you! thank you! thank you!), " +
            "click on this link: http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}&action=makemereviewer\n\n" +
            "To learn more about this change, please go to http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}" +
            "\n\nYou can add yourself as a reviewer there as well.\n\nThank you very, very much!\n\n" +
            "  --  {Reviewee}\n\n---------\n\n{Details}";

        /// <summary>
        /// Subject for invitation to join the review. Sent when review is requested with --invite flag.
        /// (review 5151 --invite bob)
        /// </summary>
        private const string InvitationSubject = "Looking for reviewers for CL {CL} by {Reviewee}";

        /// <summary>
        /// The mail that is sent when a reviewer reviews the code.
        /// </summary>
        private const string Iteration = "Dear {Reviewee},\n\nThank you for giving me the opportunity to review " +
            "your code.\n\n{Verdict}\n\nPlease see the detailed comments at\n\n" +
            "http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}\n\n  --  {Reviewer}\n\n---------\n\n{Details}";

        /// <summary>
        /// Subject for the mail that is sent when a reviewer reviews the code.
        /// </summary>
        private const string IterationSubject = "Code review for CL {CL} by {Reviewee}";

        /// <summary>
        /// The mail that is sent when a reviewee responds to the comments.
        /// </summary>
        private const string Response = "Dear reviewers,\n\nThank you for your comments. Please see my responses at\n" +
            "\nhttp://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}\n\n  -- {Reviewee}\n\n---------\n\n{Details}";

        /// <summary>
        /// Subject for the mail that is sent when a reviewee responds to the comments.
        /// </summary>
        private const string ResponseSubject = "Code review for CL {CL} by {Reviewee}";

        /// <summary>
        /// The mail that is sent periodically to remind people about old reviews.
        /// </summary>
        private const string Reminder = "Dear {Reviewee},\n\nYour code review " +
            "(http://{Webserver}{WebRoot}/default.aspx?cid={MalevichId}) for the change list {CL} is now very, very " +
            "old!\n\nChances are that the change has already been submitted and you just need to close the review:\n" +
            "    review close {CL}\n\nOr delete it if the change has been abandoned:\n    review delete {CL}\n\n" +
            "Otherwise consider reminding your reviewers about it!\n\n  --  Malevich\n\n---------\n\n{Details}";

        /// <summary>
        /// Subject for the mail that is sent periodically to remind people about old reviews.
        /// </summary>
        private const string ReminderSubject = "Code review for CL {CL} by {Reviewee}";

        /// <summary>
        /// Hashtable that maps the template type to the template text.
        /// </summary>
        private Dictionary<MailType, String> mailBodies = new Dictionary<MailType, String>();

        /// <summary>
        /// Indicates if the respective mail body is HTML.
        /// </summary>
        private Dictionary<MailType, bool> mailBodiesIsHtml = new Dictionary<MailType, bool>();

        /// <summary>
        /// Hashtable that maps the template type to the template subject.
        /// </summary>
        private Dictionary<MailType, String> mailSubjects = new Dictionary<MailType, String>();

        /// <summary>
        /// Whether we have already attempted to read Subjects.txt.
        /// </summary>
        private bool subjectsParsed = false;

        /// <summary>
        /// Logger interface.
        /// </summary>
        private ILog logger;

        /// <summary>
        /// Initalizes an instance of MailTemplates class.
        /// </summary>
        /// <param name="logger"> The logger. </param>
        public MailTemplates(ILog logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Converts the template name to arguments that string formatting understands.
        /// Currently:
        ///     {MalevichId} => {0}
        ///     {Reviewer} => {1}
        ///     {Reviewee} => {2}
        ///     {Webserver} => {3}
        ///     {WebRoot} => {4}
        ///     {Verdict} => {5}
        ///     {Details} => {6}
        ///     {CL} => {7}
        /// Not every item must be present in the review template.
        /// </summary>
        /// <param name="template"></param>
        /// <returns></returns>
        private string PrepTemplate(string template)
        {
            // The template is treated as a format string, hence we have to first escape all opening and closing
            // braces (since they have a meaning in a format string). This will result in all our own escapes to be
            // double-escaped as well, so we must compensate.
            return template.Replace("{", "{{").Replace("}", "}}").Replace("{{MalevichId}}", "{0}")
                .Replace("{{Reviewer}}", "{1}").Replace("{{Reviewee}}", "{2}").Replace("{{Webserver}}", "{3}")
                .Replace("{{WebRoot}}", "{4}").Replace("{{Verdict}}", "{5}").Replace("{{Details}}", "{6}")
                .Replace("{{CL}}", "{7}");
        }

        /// <summary>
        /// Converts the subject template name to arguments that string formatting understands.
        /// Currently:
        ///     {CL} => {0}
        ///     {Reviewee} => {1}
        ///     {Details} => {2}
        ///     {Verdict} => {3}
        /// Not every item must be present in the review template.
        /// </summary>
        /// <param name="template"></param>
        /// <returns></returns>
        private string PrepSubjectTemplate(string template)
        {
            // The template is treated as a format string, hence we have to first escape all opening and closing
            // braces (since they have a meaning in a format string). This will result in all our own escapes to be
            // double-escaped as well, so we must compensate.
            return template.Replace("{", "{{").Replace("}", "}}").Replace("{{CL}}", "{0}")
                .Replace("{{Reviewee}}", "{1}").Replace("{{Details}}", "{2}").Replace("{{Verdict}}", "{3}");
        }

        /// <summary>
        /// Retrieves the template for the mail.
        /// </summary>
        /// <param name="mailType"> Which mail type we are sending. </param>
        /// <param name="isHtml"> Whether the returned body is HTML. </param>
        /// <returns> The template. </returns>
        private string GetTemplate(MailType mailType, out bool isHtml)
        {
            if (mailType != MailType.Invite && mailType != MailType.Iteration && mailType != MailType.Request &&
                mailType != MailType.Response && mailType != MailType.Reminder)
                throw new ArgumentException("mailType");

            if (!mailBodies.ContainsKey(mailType))
            {
                string res = null;
                string path = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
                bool templateIsHtml = false;
                try
                {
                    string fileName = Path.Combine(path, mailType.ToString() + ".html");
                    if (!File.Exists(fileName))
                        fileName = Path.Combine(path, mailType.ToString() + ".txt");
                    else
                        templateIsHtml = true;

                    if (File.Exists(fileName))
                    {
                        res = File.ReadAllText(fileName);
                        logger.Log("Using template @ {0}", fileName);
                    }
                }
                catch (IOException e)
                {
                    // Eat the exception.
                    logger.Log(e.ToString());
                }

                if (res == null)
                {
                    templateIsHtml = false; // We could have discovered an html file but then failed to read it.
                    switch (mailType)
                    {
                        case MailType.Request:
                            res = Request;
                            break;
                        case MailType.Invite:
                            res = Invitation;
                            break;
                        case MailType.Iteration:
                            res = Iteration;
                            break;
                        case MailType.Response:
                            res = Response;
                            break;
                        case MailType.Reminder:
                            res = Reminder;
                            break;
                    }
                }

                mailBodies[mailType] = PrepTemplate(res);
                mailBodiesIsHtml[mailType] = templateIsHtml;
            }

            isHtml = mailBodiesIsHtml[mailType];
            return mailBodies[mailType];
        }

        /// <summary>
        /// Retrieves the template for the mail subject.
        /// </summary>
        /// <param name="mailType"> Which mail type we are sending. </param>
        /// <returns> The template. </returns>
        private string GetSubjectTemplate(MailType mailType)
        {
            if (mailType != MailType.Invite && mailType != MailType.Iteration && mailType != MailType.Request &&
                mailType != MailType.Response && mailType != MailType.Reminder)
                throw new ArgumentException("mailType");

            if ((!mailSubjects.ContainsKey(mailType)) && (!subjectsParsed))
            {
                string path = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
                string fileName = Path.Combine(path, "Subjects.txt");
                try
                {
                    if (File.Exists(fileName))
                    {
                        Regex parseMailSubject = new Regex(@"^(REQUEST|INVITE|RESPONSE|ITERATION|REMINDER):\s*(.*)\s*$",
                            RegexOptions.IgnoreCase);
                        StreamReader r = new StreamReader(fileName);
                        string l;
                        while ((l = r.ReadLine()) != null)
                        {
                            Match m = parseMailSubject.Match(l);
                            if (!m.Success)
                                continue;

                            string mt = m.Groups[1].Value;
                            string val = m.Groups[2].Value;
                            MailType emt;
                            if ("REQUEST".Equals(mt, StringComparison.OrdinalIgnoreCase))
                                emt = MailType.Request;
                            else if ("INVITE".Equals(mt, StringComparison.OrdinalIgnoreCase))
                                emt = MailType.Invite;
                            else if ("RESPONSE".Equals(mt, StringComparison.OrdinalIgnoreCase))
                                emt = MailType.Response;
                            else if ("ITERATION".Equals(mt, StringComparison.OrdinalIgnoreCase))
                                emt = MailType.Iteration;
                            else if ("REMINDER".Equals(mt, StringComparison.OrdinalIgnoreCase))
                                emt = MailType.Reminder;
                            else
                                continue;
                            if (!string.IsNullOrEmpty(val))
                            {
                                logger.Log("Found custom subject template for {0}", emt);
                                mailSubjects[emt] = PrepSubjectTemplate(val);
                            }
                        }
                        r.Close();
                    }
                }
                catch (IOException e)
                {
                    // Eat the exception.
                    logger.Log(e.ToString());
                }

                subjectsParsed = true;
            }

            if (!mailSubjects.ContainsKey(mailType))
            {
                string res = null;

                switch (mailType)
                {
                    case MailType.Request:
                        res = RequestSubject;
                        break;
                    case MailType.Invite:
                        res = InvitationSubject;
                        break;
                    case MailType.Iteration:
                        res = IterationSubject;
                        break;
                    case MailType.Response:
                        res = ResponseSubject;
                        break;
                    case MailType.Reminder:
                        res = ReminderSubject;
                        break;
                }

                if (res != null)
                    mailSubjects[mailType] = PrepSubjectTemplate(res);
            }

            return mailSubjects[mailType];
        }

        /// <summary>
        /// Given parameters, formats the email. Not all parameters are used in all templates - pass only what you need,
        /// null everything else.
        /// </summary>
        /// <param name="mailType"> Which template to use. </param>
        /// <param name="malevichId"> CID of the review. </param>
        /// <param name="reviewer"> User name for the reviewer. </param>
        /// <param name="reviewee"> User name for the reviewee. </param>
        /// <param name="webserver"> The name of the web server where Malevich web site is hosted. </param>
        /// <param name="webRoot">  The application name on the server. </param>
        /// <param name="verdict"> The verdict. </param>
        /// <param name="description"> The description of the change. </param>
        /// <param name="details"> The details. Assumed to be text that would be HTML encoded if necessary. </param>
        /// <param name="CL"> The change list. </param>
        /// <param name="isHtml"> Output: whether the body is HTML. </param>
        /// <returns> The formatted email body. </returns>
        public string CreateMail(MailType mailType, int malevichId, string reviewer, string reviewee, string webserver,
            string webRoot, string verdict, string details, string CL, out bool isHtml)
        {
            string format = GetTemplate(mailType, out isHtml);

            if (reviewer == null)
                reviewer = String.Empty;

            if (reviewee == null)
                reviewee = String.Empty;

            if (webserver == null)
                webserver = String.Empty;

            if (webRoot == null)
                webRoot = String.Empty;

            if (verdict == null)
                verdict = String.Empty;

            if (details == null)
                details = String.Empty;

            if (isHtml)
                details = "<pre>" + HttpUtility.HtmlEncode(details) + "</pre>";

            if (CL == null)
                CL = String.Empty;

            return String.Format(format, malevichId, reviewer, reviewee, webserver, webRoot, verdict,
                details, CL);
        }

        /// <summary>
        /// Given parameters, formats the email. Not all parameters are used in all templates - pass only what you need,
        /// null everything else. This is an overload used if the caller knows what type (text or HTML) template
        /// is used, and 
        /// </summary>
        /// <param name="mailType"> Which template to use. </param>
        /// <param name="malevichId"> CID of the review. </param>
        /// <param name="reviewer"> User name for the reviewer. </param>
        /// <param name="reviewee"> User name for the reviewee. </param>
        /// <param name="webserver"> The name of the web server where Malevich web site is hosted. </param>
        /// <param name="webRoot">  The application name on the server. </param>
        /// <param name="verdict"> The verdict. </param>
        /// <param name="description"> The description of the change. </param>
        /// <param name="details"> The details. Assumed to be HTML encoded if the template is HTML. </param>
        /// <param name="CL"> The change list. </param>
        /// <returns> The formatted email body. </returns>
        public string CreateMail(MailType mailType, int malevichId, string reviewer, string reviewee, string webserver,
            string webRoot, string verdict, string details, string CL)
        {
            bool isHtml;
            string format = GetTemplate(mailType, out isHtml);

            if (reviewer == null)
                reviewer = String.Empty;

            if (reviewee == null)
                reviewee = String.Empty;

            if (webserver == null)
                webserver = String.Empty;

            if (webRoot == null)
                webRoot = String.Empty;

            if (verdict == null)
                verdict = String.Empty;

            if (details == null)
                details = String.Empty;

            if (CL == null)
                CL = String.Empty;

            return String.Format(format, malevichId, reviewer, reviewee, webserver, webRoot, verdict,
                details, CL);
        }

        /// <summary>
        /// Returns whether the template is HTML or not.
        /// </summary>
        /// <param name="mailType"> Which template to use. </param>
        /// <returns> True if the template is HTML. </returns>
        public bool IsTemplateHtml(MailType mailType)
        {
            bool isHtml;
            GetTemplate(mailType, out isHtml);
            return isHtml;
        }

        /// <summary>
        /// Given parameters, formats the email subject. Not all parameters are used in all templates - pass only what
        /// you need, null everything else.
        /// </summary>
        /// <param name="mailType"> Which template to use. </param>
        /// <param name="CL"> CL of the review. </param>
        /// <param name="reviewee"> User name for the reviewee. </param>
        /// <param name="details"> The details. </param>
        /// <param name="verdict"> The verdict. </param>
        /// <returns> The formatted email subject. </returns>
        public string CreateMailSubject(MailType mailType, string CL, string reviewee, string details, string verdict)
        {
            if (CL == null)
                CL = String.Empty;

            if (reviewee == null)
                reviewee = String.Empty;

            if (details == null)
                details = String.Empty;

            if (verdict == null)
                verdict = String.Empty;

            return String.Format(GetSubjectTemplate(mailType), CL, reviewee, details, verdict);
        }
    }
}
