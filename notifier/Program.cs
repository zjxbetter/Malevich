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
using System.Configuration;
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Web;

using Microsoft.Win32;

using Malevich.Util;
using DataModel;
using ReviewNotifier.com.microsoft.mail.wcf;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace ReviewNotifier
{
    /// <summary>
    /// Sends an email every time a review is requested or performed.
    /// Must be executed from a real user context.
    /// </summary>
    class Program
    {
        /// <summary>
        /// A class that logs to the console, and potentially to a file.
        /// </summary>
        private class Logger : ILog
        {
            /// <summary>
            /// Maximum log size after which it gets archived and a new log is started.
            /// </summary>
            private const int MaxFileSize = 100 * 1024;

            /// <summary>
            /// Log file writer.
            /// </summary>
            private StreamWriter writer;

            /// <summary>
            /// Creates an instance of the Logger class.
            /// </summary>
            /// <param name="fileName"></param>
            public Logger(string fileName)
            {
                if (fileName == null)
                    return;

                if (File.Exists(fileName))
                {
                    FileInfo info = new FileInfo(fileName);
                    if (info.Length > MaxFileSize)
                    {
                        string backup = fileName + ".prev.log";
                        File.Delete(backup);
                        File.Move(fileName, backup);
                    }
                }

                writer = new StreamWriter(fileName, true);
            }

            /// <summary>
            /// Writes an output to a console, and if configured, to a file.
            /// </summary>
            /// <param name="format"> Format string. </param>
            /// <param name="args"> Argumenst. </param>
            public void Log(string format, params object[] args)
            {
                if (writer != null)
                    writer.WriteLine(format, args);

                Console.WriteLine(format, args);
            }

            /// <summary>
            /// Closes the writer.
            /// </summary>
            public void Close()
            {
                if (writer != null)
                    writer.Close();
                writer = null;
            }
        }

        /// <summary>
        /// Name of the scheduled task.
        /// </summary>
        private const string SchedulTaskName = "MalevichReviewNotifier";

        /// <summary>
        /// Maximum length of change description that could go into email subject.
        /// </summary>
        private const int MaxDescriptionLength = 50;

        /// <summary>
        /// For ldap queries, a dictionary that hashes the user name to an email address.
        /// </summary>
        private static Dictionary<string, string> emailDictionary = new Dictionary<string, string>();

        /// <summary>
        /// For ldap queries, stores the 'givenname' property for user names.
        /// </summary>
        private static Dictionary<string, string> givennameDictionary = new Dictionary<string, string>();

        /// <summary>
        /// For ldap queries, a directory searcher object.
        /// </summary>
        //private static DirectorySearcher directorySearcher;

        /// <summary>
        /// Logger.
        /// </summary>
        private static ILog logger;

        /// <summary>
        /// Displays the help string.
        /// </summary>
        private static void DisplayUsage()
        {
            Console.WriteLine("To configure for Exchange:");
            Console.WriteLine("    ReviewNotifier exchange webserviceurl companydomain [useldap]");
            Console.WriteLine("To configure for smtp:");
            Console.WriteLine("    ReviewNotifier smtp smtpserver companydomain [usessl] [useldap]");
            Console.WriteLine("Common configuration:");
            Console.WriteLine("    ReviewNotifier credentials username [password domain] [aliastosendfrom]");
            Console.WriteLine("    ReviewNotifier webserver webservername");
            Console.WriteLine("    ReviewNotifier database SQL_server_instance");
            Console.WriteLine("    ReviewNotifier schedule repetition_interval_in_minutes");
            Console.WriteLine("To save the log of operation in a file:");
            Console.WriteLine("    ReviewNotifier log fully_qualified_file_name");
            Console.WriteLine("To reset configuration:");
            Console.WriteLine("    ReviewNotifier reset");
            Console.WriteLine("To use (only after configuration!):");
            Console.WriteLine("    ReviewNotifier");
            Console.WriteLine("Or, more likely, create a scheduled task that runs ReviewNotifier every");
            Console.WriteLine("10 minutes by running \"ReviewNotifier schedule 10\"");
            Console.WriteLine();
            Console.WriteLine("To send a reminder to attend to older code reviews (no activity for number_of_days):");
            Console.WriteLine("    ReviewNotifier --remind number_of_days");
            Console.WriteLine();
            Console.WriteLine("All configuration steps must be completed before the program is started.");
            Console.WriteLine();
            Console.WriteLine("Use either SMTP of Exchange configuration, not both. Always reset the");
            Console.WriteLine("configuration when switching between SMTP and Exchange.");
            Console.WriteLine();
            Console.WriteLine("If Exchange mode is used, the web service url should point to Exchange");
            Console.WriteLine("Web Server 2007. The format of the string should look like this:");
            Console.WriteLine("        https://owa.microsoft.com/EWS/Exchange.asmx");
            Console.WriteLine();
            Console.WriteLine("Note: a 'send of behalf' address can be specified. If it is, the mail will");
            Console.WriteLine("come from that account. Note that the logon account must have rights to send");
            Console.WriteLine("on behalf of this (optional) email alias. The domain of this alias is the");
            Console.WriteLine("same as the user account.");
            Console.WriteLine();
            Console.WriteLine("To test the setup, run 'ReviewNotifier testsetup email' where email");
            Console.WriteLine("is the alias (without the domain name to which the test email will be sent.");
        }

        /// <summary>
        /// Stores credentials in registry.
        /// </summary>
        /// <param name="args"> The argument array, presumed to be one of:
        ///     {"credentials", username, password, domain}
        ///     {"credentials", username, password, domain, user name to send on behalf of}
        ///     {"credentials", username, user name to send on behalf of}
        ///     
        /// optionally email on behalf of which to send} </param>
        private static void SetCredentials(string[] args)
        {
            if (args.Length < 2 || args.Length > 5)
            {
                Console.Error.WriteLine("Expected a different set of parameters. Use 'help' for help.");
                return;
            }

            var userAndDomain = args[1].Split('\\');
            if (userAndDomain.Length == 2)
            {
                Config.Domain = userAndDomain[0];
                Config.User = userAndDomain[1];
            }
            else
            {
                Config.User = args[1];
            }

            if (args.Length == 3)
            {
                Config.FromEmail = args[2];
            }
            else if (args.Length > 3)
            {
                Config.Password = args[2];
                Config.Domain = args[3];
                if (args.Length == 5)
                    Config.FromEmail = args[4];
            }
            Config.Save();
        }

        /// <summary>
        /// Stores exchange information in registry.
        /// </summary>
        /// <param name="args"> The argument array, presumed to be {"exchange", EWS 2007 url, domain [, useldap]}
        /// </param>
        private static void SetExchangeServer(string[] args)
        {
            if (args.Length != 3 && args.Length != 4)
            {
                Console.Error.WriteLine("Need URL and email domain");
                return;
            }

            if (args.Length == 4 && !"useldap".Equals(args[3], StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("The last argument can only be useldap.");
                return;
            }

            Config.EmailService = args[1];
            Config.EmailDomain = args[2];
            if (args.Length == 4)
                Config.UseLdap = true;

            Config.Save();
        }

        /// <summary>
        /// Stores SMTP information in registry.
        /// </summary>
        /// <param name="args"> The argument array, presumed to be {"smtp", smtp server, domain [, usessl] [, useldap]}
        /// </param>
        private static void SetSmtpServer(string[] args)
        {
            if (args.Length < 3 || args.Length > 5)
            {
                Console.Error.WriteLine("Need hostname and email domain");
                return;
            }

            Config.SmtpServer = args[1];
            Config.EmailDomain = args[2];

            for (int i = 3; i < args.Length; ++i)
            {
                if ("useLdap".Equals(args[i], StringComparison.OrdinalIgnoreCase))
                {
                    Config.UseLdap = true;
                }
                else if ("useSsl".Equals(args[i], StringComparison.OrdinalIgnoreCase))
                {
                    Config.UseSsl = true;
                }
                else
                {
                    Console.Error.WriteLine("{0} is not recognized!", args[i]);
                    return;
                }
            }
            Config.Save();
        }

        /// <summary>
        /// Stores database connection string in registry.
        /// </summary>
        /// <param name="args"> The argument array, presumed to be {"database", connectionString} </param>
        private static void SetDatabase(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Need database connection string");
                return;
            }

            Config.Database = args[1];
            Config.Save();
        }

        /// <summary>
        /// Stores web server name in registry.
        /// </summary>
        /// <param name="args"> The argument array, presumed to be {"webserver", connectionString} </param>
        private static void SetWebserver(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Need web server name");
                return;
            }

            Config.WebServer = args[1];
            Config.Save();
        }

        /// <summary>
        /// Add, update or delete notifier as repetition task in Windows Task Scheduler.
        /// </summary>
        /// <param name="args"> The argument array, presumed to be {"schedule", interval_in_minutes} </param>
        private static void SetScheduleTask(string[] args)
        {
            int interval;
            if (args.Length != 2 || !Int32.TryParse(args[1], out interval))
            {
                Console.Error.WriteLine("ReviewNotifier schedule repetition_interval_in_minutes");
                Console.Error.WriteLine(
                    "Set the repetition interval to a positive integar to add/update a scheduled task.");
                Console.Error.WriteLine("Set the repetition interval to 0 to delete the existing task.");
                return;
            }

            TaskScheduler taskScheduler = new TaskScheduler();
            taskScheduler.Interval = interval;
            taskScheduler.TaskName = SchedulTaskName;
            taskScheduler.TaskPath = Process.GetCurrentProcess().MainModule.FileName;

            if (!ReviewNotifierConfiguration.Load())
            {
                Console.Error.WriteLine("Please configure service parameters before echeduling the task!");
                return;
            }

            bool success = taskScheduler.SetTask(Config.User, Config.Password);
            if (success)
                Console.Error.WriteLine(
                    "Created/updated/deleted the scheduled task successfully. (repetition = {0} min)",
                    taskScheduler.Interval);
            else
                Console.Error.WriteLine("Failed to set schedule task, please create the task manually.");
        }

        /// <summary>
        /// Configure the log file name.
        /// </summary>
        /// <param name="args"> Arguments, presumed to me {"log", filename} </param>
        private static void SetLogFile(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Need log file name");
                return;
            }

            if ((!Path.IsPathRooted(args[1])) || args[1].StartsWith(@"\\"))
            {
                Console.Error.WriteLine("Path must be local and fully qualified");
                return;
            }

            Config.LogFile = args[1];
            Config.Save();
        }

        /// <summary>
        /// Processes configuration parameters.
        /// </summary>
        /// <param name="args"></param>
        private static void SetConfiguration(string[] args)
        {
            if (args[0].Equals("reset"))
            {
                Config.Clear();
                return;
            }

            if (args[0].Equals("credentials", StringComparison.InvariantCultureIgnoreCase))
            {
                SetCredentials(args);
                return;
            }

            if (args[0].Equals("exchange", StringComparison.InvariantCultureIgnoreCase))
            {
                SetExchangeServer(args);
                return;
            }

            if (args[0].Equals("smtp", StringComparison.InvariantCultureIgnoreCase))
            {
                SetSmtpServer(args);
                return;
            }

            if (args[0].Equals("webserver", StringComparison.InvariantCultureIgnoreCase))
            {
                SetWebserver(args);
                return;
            }

            if (args[0].Equals("schedule", StringComparison.InvariantCultureIgnoreCase))
            {
                SetScheduleTask(args);
                return;
            }

            if (args[0].Equals("database", StringComparison.InvariantCultureIgnoreCase))
            {
                SetDatabase(args);
                return;
            }

            if (args[0].Equals("log", StringComparison.InvariantCultureIgnoreCase))
            {
                SetLogFile(args);
                return;
            }

            DisplayUsage();
        }

        /// <summary>
        /// Converts the review status to a verdict sentence.
        /// </summary>
        /// <param name="status"> The numeric code for the verdict. </param>
        /// <returns></returns>
        private static string ReviewStatusToSentence(int verdict)
        {
            switch (verdict)
            {
                case 0: return "I think this change needs more work before it is submitted.";
                case 1: return "This looks good, but I do recommend a few minor tweaks.";
                case 2: return "LGTM.";
            }

            return "I've made a few comments, but they are non-scoring :-).";
        }

        /// <summary>
        /// Computes the displayed name of the file version. Similar to ComputeMoniker, but without the action.
        /// </summary>
        /// <param name="name"> The base name of a file (excluding the path). </param>
        /// <param name="version"> The version. </param>
        /// <returns> The string to display. </returns>
        private static string FileDisplayName(string name, FileVersion version)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(name);
            sb.Append('#');
            sb.Append(version.Revision.ToString());
            if (!version.IsRevisionBase && version.TimeStamp != null)
                sb.Append(" " + version.TimeStamp);

            return sb.ToString();
        }

        /// <summary>
        /// Lists the review comments in the string builder.
        /// </summary>
        /// <param name="context"> The data context. </param>
        /// <param name="reviewId"> The review object. </param>
        /// <param name="isBodyHtml"> If true, generate HTML comments. </param>
        /// <param name="malevichPath"> The URL of Malevich server. </param>
        private static void ListCommentsForReview(CodeReviewDataContext context, Review rw, StringBuilder sb,
            bool isBodyHtml, string malevichUrl)
        {
            if (rw.CommentText != null)
            {
                if (isBodyHtml)
                {
                    sb.Append("<div class=\"CssMalevichOverallComment\"><pre>");
                    sb.Append(HttpUtility.HtmlEncode(rw.CommentText));
                    sb.Append("</pre></div>");
                }
                else
                {
                    sb.Append(rw.CommentText);
                    sb.Append("\n\n\n");
                }
            }

            var commentsQuery = from cm in context.Comments
                                where cm.ReviewId == rw.Id
                                join fv in context.FileVersions on cm.FileVersionId equals fv.Id
                                orderby fv.FileId, cm.FileVersionId, cm.Line, cm.LineStamp
                                select cm;

            Comment[] comments = commentsQuery.ToArray();
            if (comments.Length == 0)
                return;

            if (isBodyHtml)
                sb.Append("<table class=\"CssMalevichCommentTable\">");

            int currentFileVersionId = 0;
            int currentLine = 0;
            foreach (Comment comment in comments)
            {
                if (currentFileVersionId != comment.FileVersionId)
                {
                    FileVersion version = comment.FileVersion;
                    string name = version.ChangeFile.ServerFileName;
                    int lastForwardSlash = name.LastIndexOf('/');
                    if (lastForwardSlash >= 0)
                        name = name.Substring(lastForwardSlash + 1);

                    FileVersion ver = comment.FileVersion;

                    if (isBodyHtml)
                        sb.Append("<tr class=\"CssMalevichEmailCommentTableRow\">" +
                            "<td colspan=\"2\" class=\"CssMalevichEmailFileColumn\">" +
                            "<a href=\"" + malevichUrl + "?fid=" + ver.FileId + "&vid1=" + ver.Id + "&vid2=" + ver.Id +
                            "\" class=\"CssMalevichFileLink\">File " + FileDisplayName(name, ver) + "</a>:</td></tr>");
                    else
                        sb.Append("\n\nFile " + FileDisplayName(name, ver) + ":\n");

                    currentFileVersionId = version.Id;
                    currentLine = 0;
                }

                if (isBodyHtml)
                    sb.Append(
                        "<tr class=\"CssMalevichEmailCommentTableRow\"><td class=\"CssMalevichEmailLineColumn\">");

                if (currentLine != comment.Line)
                {
                    currentLine = comment.Line;
                    sb.Append("Line " + currentLine + ":\n");
                }

                if (isBodyHtml)
                {
                    sb.Append("</td><td class=\"CssMalevichEmailCommentColumn\"><pre>");
                    sb.Append(HttpUtility.HtmlEncode(comment.CommentText));
                    sb.Append("</pre></td></tr>");
                }
                else
                {
                    sb.Append(comment.CommentText);
                    sb.Append("\n");
                }
            }

            if (isBodyHtml)
                sb.Append("</table>");
        }

        /// <summary>
        /// Creates and populated Exchange message type.
        /// </summary>
        /// <param name="to"> Email address to send to. </param>
        /// <param name="from"> Email address of send on behalf, or null. </param>
        /// <param name="replyTo"> An alias of the person on behalf of which the mail is sent. </param>
        /// <param name="subject"> Subject. </param>
        /// <param name="body"> Body of the message. </param>
        /// <param name="isBodyHtml"> Whether the body of the message is HTML. </param>
        /// <param name="threadId"> The id of the thread, if message is a part of the thread. 0 otherwise. </param>
        /// <param name="isThreadStart"> Whether this is the first message in the thread. </param>
        /// <returns> Created message structure. </returns>
        private static MessageType MakeExchangeMessage(string to, string from, string replyTo, string subject,
            string body, bool isBodyHtml, int threadId, bool isThreadStart)
        {
            return MakeExchangeMessage(new List<string>() { to }, null, from, replyTo, subject, body,
                isBodyHtml, threadId, isThreadStart);
        }

        /// <summary>
        /// Creates and populated Exchange message type.
        /// 
        /// Note: just like SMTP, this function takes the threading parameters, but unlike SMTP it does nothing
        /// with them. Exchange web services were designed for implementing mail readers, rather than sending mail,
        /// configuring threading without having access to the mail box is incredibly difficult. Given the limited
        /// amount of benefit, I gave up. If in the future EWS improves, maybe I will be able to implement this.
        /// Meanwhile - using SMTP transport is the recommended way.
        /// </summary>
        /// <param name="to"> List of 'to' addresses. </param>
        /// <param name="cc"> List of 'cc' addresses. </param>
        /// <param name="from"> On-behalf-of account, or null. </param>
        /// <param name="replyTo"> An alias of the person on behalf of which the mail is sent. </param>
        /// <param name="subject"> Subject. </param>
        /// <param name="body"> Body. </param>
        /// <param name="isBodyHtml"> Whether the body of the message is HTML. </param>
        /// <param name="threadId"> The id of the thread, if message is a part of the thread. 0 otherwise.
        /// This is currently unused (see above). </param>
        /// <param name="isThreadStart"> Whether this is the first message in the thread. This is currently unused
        /// (see above). </param>
        /// <returns> Created message structure. </returns>
        private static MessageType MakeExchangeMessage(List<string> to, List<string> cc, string from, string replyTo,
            string subject, string body, bool isBodyHtml, int threadId, bool isThreadStart)
        {
            MessageType message = new MessageType();

            List<EmailAddressType> recipients = new List<EmailAddressType>();
            foreach (string email in to)
            {
                EmailAddressType address = new EmailAddressType();
                address.EmailAddress = email;
                recipients.Add(address);
            }
            message.ToRecipients = recipients.ToArray();

            if (cc != null)
            {
                recipients = new List<EmailAddressType>();
                foreach (string email in cc)
                {
                    EmailAddressType address = new EmailAddressType();
                    address.EmailAddress = email;
                    recipients.Add(address);
                }
                message.CcRecipients = recipients.ToArray();
            }

            if (from != null)
            {
                message.From = new SingleRecipientType();
                message.From.Item = new EmailAddressType();
                message.From.Item.EmailAddress = from;
            }

            if (replyTo != null)
            {
                EmailAddressType reply = new EmailAddressType();
                reply.EmailAddress = replyTo;

                message.ReplyTo = new EmailAddressType[1];
                message.ReplyTo[0] = reply;
            }

            message.Subject = subject;
            message.Sensitivity = SensitivityChoicesType.Normal;

            message.Body = new BodyType();
            message.Body.BodyType1 = isBodyHtml ? BodyTypeType.HTML : BodyTypeType.Text;

            message.Body.Value = body;

            return message;
        }

        /// <summary>
        /// Creates System.Net.Mail.MailMessage.
        /// </summary>
        /// <param name="to"> Email to send to. </param>
        /// <param name="from"> On behalf email, or null. </param>
        /// <param name="replyTo"> An alias of the person on behalf of which the mail is sent. </param>
        /// <param name="sender"> Email of a sender. </param>
        /// <param name="subject"> Subject. </param>
        /// <param name="body"> Body. </param>
        /// <param name="isBodyHtml"> Whether the body of the message is HTML. </param>
        /// <param name="threadId"> The id of the thread, if message is a part of the thread. 0 otherwise. </param>
        /// <param name="isThreadStart"> Whether this is the first message in the thread. </param>
        /// <returns> Created message structure. </returns>
        private static MailMessage MakeSmtpMessage(string to, string from, string replyTo, string sender,
            string subject, string body, bool isBodyHtml, int threadId, bool isThreadStart)
        {
            return MakeSmtpMessage(new List<string>() { to }, null, from, replyTo, sender, subject, body,
                isBodyHtml, threadId, isThreadStart);
        }

        /// <summary>
        /// Creates System.Net.Mail.MailMessage.
        /// </summary>
        /// <param name="to"> List of emails for the 'to' line. </param>
        /// <param name="cc"> List of emails for 'cc' line. </param>
        /// <param name="from"> On behalf email, or null. </param>
        /// <param name="replyTo"> An alias of the person on behalf of which the mail is sent. </param>
        /// <param name="sender"> Email of a sender. </param>
        /// <param name="subject"> Subject. </param>
        /// <param name="body"> Body. </param>
        /// <param name="isBodyHtml"> Whether the body of the message is HTML. </param>
        /// <param name="threadId"> The id of the thread, if message is a part of the thread. 0 otherwise. </param>
        /// <param name="isThreadStart"> Whether this is the first message in the thread. </param>
        /// <returns> Created message structure. </returns>
        private static MailMessage MakeSmtpMessage(List<string> to, List<string> cc, string from, string replyTo,
            string sender, string subject, string body, bool isBodyHtml, int threadId, bool isThreadStart)
        {
            if (from == null)
                from = sender;

            MailMessage message = new MailMessage();
            foreach (string address in to)
                message.To.Add(address);

            if (cc != null)
            {
                foreach (string address in cc)
                    message.CC.Add(address);
            }
            
            if (replyTo != null)
                message.ReplyToList.Add(new MailAddress(replyTo));

            message.Subject = subject;
            message.From = new MailAddress(from);
            message.Sender = new MailAddress(from);
            message.Body = body;
            message.IsBodyHtml = isBodyHtml;

            if (threadId != 0)
            {
                if (isThreadStart)
                {
                    message.Headers["Message-ID"] = String.Format("<{0}:{1}>", threadId, Environment.MachineName);
                }
                else
                {
                    message.Headers["In-Reply-To"] = String.Format("<{0}:{1}>", threadId, Environment.MachineName);
                    message.Headers["Message-ID"] = String.Format("<{0}-{1}:{2}>", threadId, Environment.TickCount,
                        Environment.MachineName);
                }
            }

            return message;
        }

        /// <summary>
        /// Sends mail through the exchange server.
        /// </summary>
        /// <param name="Config"> ReviewNotifierConfiguration. </param>
        /// <param name="exchangeItems"> Mail to send. </param>
        /// <returns> true if successful. </returns>
        private static bool SendExchangeMail(ReviewNotifierConfiguration Config, List<MessageType> exchangeItems)
        {
            int maxQuotaNum = int.MaxValue;
            var binding = (ExchangeServicePortType)new ExchangeServicePortTypeClient(
                new BasicHttpBinding("ExchangeServiceBinding")
                {
                    MaxReceivedMessageSize = maxQuotaNum,
                    MaxBufferSize = maxQuotaNum,
                    ReaderQuotas = new System.Xml.XmlDictionaryReaderQuotas()
                    {
                        MaxArrayLength = maxQuotaNum,
                        MaxStringContentLength = maxQuotaNum,
                        MaxNameTableCharCount = maxQuotaNum
                    }
                },
                new EndpointAddress(Config.EmailService));

            DistinguishedFolderIdType folder = new DistinguishedFolderIdType();
            folder.Id = DistinguishedFolderIdNameType.sentitems;

            TargetFolderIdType targetFolder = new TargetFolderIdType();
            targetFolder.Item = folder;

            
            CreateItemType createItem = new CreateItemType();
            createItem.MessageDisposition = MessageDispositionType.SendAndSaveCopy;
            createItem.MessageDispositionSpecified = true;
            createItem.SavedItemFolderId = targetFolder;

            createItem.Items = new NonEmptyArrayOfAllItemsType();
            createItem.Items.Items = exchangeItems.ToArray();

            var createReq = new CreateItemRequest() { CreateItem = createItem };

            var response = binding.CreateItem(createReq);

            bool result = true;
            foreach (ResponseMessageType r in response.CreateItemResponse1.ResponseMessages.Items)
            {
                if (r.ResponseClass != ResponseClassType.Success)
                {
                    logger.Log("Failed to send the message. ");
                    logger.Log(r.MessageText);

                    result = false;
                }
            }

            return result;
        }

        /// <summary>
        /// Sends mail through SMTP server.
        /// </summary>
        /// <param name="Config"> ReviewNotifierConfiguration. </param>
        /// <param name="smtpItems"> Mail to send. </param>
        /// <returns> true if successful. </returns>
        private static bool SendSmtpMail(ReviewNotifierConfiguration Config, List<MailMessage> smtpItems)
        {
            SmtpClient client = new SmtpClient(Config.SmtpServer);
            if (Config.Password == null)
                client.UseDefaultCredentials = true;
            else
                client.Credentials = new NetworkCredential(Config.User, Config.Password, Config.Domain);

            if (Config.UseSsl)
                client.EnableSsl = true;

            foreach (MailMessage email in smtpItems)
                client.Send(email);

            return true;
        }

        /// <summary>
        /// Retrieves the specified property values from LDAP.
        /// </summary>
        /// <param name="props">Property names to get the values of.</param>
        /// <param name="userName">User name against which to bind the property names.</param>
        /// <returns>Dictionary of name+value pairs.</returns>
        private static IDictionary<string, string> RetrieveLdapProperties(ICollection<string> props, string userName)
        {
            var directorySearcher = new DirectorySearcher();
            directorySearcher.Filter = String.Format("(SAMAccountName={0})", userName);
            foreach (var prop in props)
            {
                directorySearcher.PropertiesToLoad.Add(prop);
            }
            SearchResult result = directorySearcher.FindOne();
            Dictionary<string, string> dict = null;
            if (result != null)
            {
                dict = new Dictionary<string, string>(result.Properties.Count);
                foreach (var name in result.Properties.PropertyNames)
                {
                    // Note: assumes 1 to 1 name/value mapping.
                    dict.Add(name.ToString(), result.Properties[name.ToString()][0].ToString());
                }
            }
            return dict;
        }

        /// <summary>
        /// Resolves the property propName against the LDAP.
        /// </summary>
        /// <param name="propName">Property name to get the value of.</param>
        /// <param name="userName">User name against which to bind the property name.</param>
        /// <returns>Property value.</returns>
        private static string RetrieveLdapProperty(string propName, string userName)
        {
            var dict = RetrieveLdapProperties(new string[] { propName }, userName);
            string value = null;
            if (dict != null)
                dict.TryGetValue(propName, out value);
            return value;
        }

        /// <summary>
        /// Resolves the email address from the user name, performing LDAP query if necessary.
        /// </summary>
        /// <param name="Config"></param>
        /// <param name="userName"></param>
        /// <returns>User's email address.</returns>
        private static string ResolveUser(ReviewNotifierConfiguration Config, string userName)
        {
            if (!Config.UseLdap)
                return userName + "@" + Config.EmailDomain;

            string email;
            if (emailDictionary.TryGetValue(userName, out email))
                return email;

            email = RetrieveLdapProperty("mail", userName);
            if (email != null)
            {
                emailDictionary[userName] = email;
            }
            else
            {
                email = userName + "@" + Config.EmailDomain;
                Console.Error.WriteLine("Failed ldap lookup for {0}. Using {1}.", userName, email);
            }

            return email;
        }

        /// <summary>
        /// Returns the user's friendly name, if LDAP is enabled; otherwise returns userName.
        /// </summary>
        /// <param name="Config"></param>
        /// <param name="userName"></param>
        /// <returns>User's friendly (given) name.</returns>
        private static string ResolveFriendlyName(ReviewNotifierConfiguration Config, string userName)
        {
            if (!Config.UseLdap)
                return userName;

            string givenname;
            if (givennameDictionary.TryGetValue(userName, out givenname))
                return givenname;

            givenname = RetrieveLdapProperty("givenname", userName);
            if (givenname != null)
            {
                givennameDictionary[userName] = givenname;
            }
            else
            {
                givenname = userName;
                Console.Error.WriteLine("Failed ldap lookup for {0}. Using {1}.", userName, givenname);
            }

            return givenname;
        }

        /// <summary>
        /// Tests if the current configuration works by sending email.
        /// </summary>
        /// <param name="email"> Who to send email to. </param>
        private static void TestSetup(string email)
        {
            Console.WriteLine("Testing notifier setup.");
            Console.Write("1. Attempting to load configuration... ");
            if (!ReviewNotifierConfiguration.Load())
            {
                Console.WriteLine("Failure!");
                Console.WriteLine("Run 'ReviewNotifier help' for help with configuring this program.");
                return;
            }
            Console.WriteLine("Success!");
            Console.Write("2. Attempting to connect to the database... ");

            CodeReviewDataContext context = new CodeReviewDataContext(Config.Database);
            int openReviews = (from rr in context.ChangeLists where rr.Stage == 0 select rr).Count();
            int totalReviews = (from rr in context.ChangeLists select rr).Count();
            int files = (from ff in context.ChangeFiles select ff).Count();
            int comments = (from cc in context.Comments select cc).Count();

            Console.WriteLine("Success!");

            string mailbody = "If you are reading this message, the notifier configuration should be correct.\n\n" +
                "Check that the mail has come from the right account, and that the following stats are reasonable:\n" +
                "    Open reviews: " + openReviews + "\n    Total reviews: " + totalReviews +
                "\n    Total files in all reviews: " + files + "\n    Total comments in all reviews: " +
                comments + "\n\nRespectfully,\n    Your friendly review notifier.\n";

            bool result = false;
            if (!string.IsNullOrEmpty(Config.EmailService))
            {
                Console.Write("3. Sending mail using Exchange protocol... ");
                List<MessageType> mail = new List<MessageType>();
                mail.Add(MakeExchangeMessage(email + "@" + Config.EmailDomain,
                    Config.FromEmail == null ? null : Config.FromEmail + "@" + Config.EmailDomain,
                    email + "@" + Config.EmailDomain,
                    "A test email from Malevich notifier - sent via Exchange transport", mailbody, false, 0, false));
                result = SendExchangeMail(Config, mail);
            }
            else if (!string.IsNullOrEmpty(Config.SmtpServer))
            {
                Console.Write("3. Sending mail using SMTP protocol... ");
                List<MailMessage> mail = new List<MailMessage>();
                mail.Add(MakeSmtpMessage(email + "@" + Config.EmailDomain,
                    Config.FromEmail == null ? null : Config.FromEmail + "@" + Config.EmailDomain,
                    ResolveUser(Config, Config.User), ResolveUser(Config, Config.User),
                    "A test email from Malevich notifier - sent via SMTP transport", mailbody, false, 0, false));
                result = SendSmtpMail(Config, mail);
            }
            else
            {
                // This should really never happen.
                Console.WriteLine("Failure: mail transport is not configured!");
            }

            if (result)
            {
                Console.WriteLine("Success! (or so we think: check your inbox!");
                Console.WriteLine("Email was sent to {0} @ {1}", email, Config.EmailDomain);
            }
        }

        /// <summary>
        /// Abbreviates a string to a given number of characters.
        /// null is an acceptable input, and null is returned back
        /// in that case.
        /// 
        /// Note: the resulting abbreviation is approximate -
        /// simplicity was chosen over correctness :-). It is
        /// possible for a string that would fit to be abbreviated.
        /// </summary>
        /// <param name="str"> A string. </param>
        /// <param name="maxlen"> Maximum length of the result. </param>
        /// <returns> An abbreviated string. </returns>
        private static string Abbreviate(string str, int maxlen)
        {
            if (maxlen < 4)
                throw new ArgumentOutOfRangeException("maxlen should be greater than 3");

            if (str == null)
                return null;

            StringBuilder result = new StringBuilder(maxlen);
            bool haveWhiteSpace = false;
            foreach (char c in str)
            {
                if (char.IsWhiteSpace(c))
                {
                    haveWhiteSpace = true;
                    continue;
                }

                if (haveWhiteSpace && result.Length > 0)
                {
                    result.Append(' ');
                    haveWhiteSpace = false;
                }

                result.Append(c);
                if (result.Length > maxlen)
                    break;
            }

            if (result.Length > maxlen)
            {
                int index = maxlen - 3;
                while (index > 0 && !char.IsWhiteSpace(result[index]))
                    --index;
                result.Length = index;
                result.Append("...");
            }

            return result.ToString();
        }

        /// <summary>
        /// Does all the work.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            if (args.Contains<string>("help") || args.Contains("-h") || args.Contains("-?"))
            {
                DisplayUsage();
                return;
            }

            if (args.Length == 2 && args[0].Equals("testsetup", StringComparison.InvariantCultureIgnoreCase))
            {
                TestSetup(args[1]);
                return;
            }

            int remindDaysOld = -1;
            if (args.Length == 2 && args[0].Equals("--remind", StringComparison.InvariantCultureIgnoreCase))
            {
                if ((!Int32.TryParse(args[1], out remindDaysOld)) || (remindDaysOld <= 0))
                {
                    DisplayUsage();
                    return;
                }
            }

            if (args.Length > 0 && remindDaysOld <= 0)
            {
                SetConfiguration(args);
                return;
            }

            if (!ReviewNotifierConfiguration.Load())
                return;

            logger = new Logger(Config.LogFile);

            logger.Log("Started processing review notifications @ {0}", DateTime.Now);

            MailTemplates templates = new MailTemplates(logger);

            CodeReviewDataContext context = new CodeReviewDataContext(Config.Database);
            Dictionary<int, string> sourceControlRoots = new Dictionary<int, string>();

            var sourceControlsQuery = from sc in context.SourceControls select sc;
            foreach (SourceControl sc in sourceControlsQuery)
            {
                string site = string.Empty;
                if (!string.IsNullOrEmpty(sc.WebsiteName))
                {
                    if (!sc.WebsiteName.StartsWith("/"))
                        site = "/" + sc.WebsiteName.Substring(1);
                    else
                        site = sc.WebsiteName;
                }
                sourceControlRoots[sc.Id] = site;
            }

            List<MessageType> exchangeItems = null;
            if (!string.IsNullOrEmpty(Config.EmailService))
                exchangeItems = new List<MessageType>();

            List<MailMessage> smtpItems = null;
            if (!string.IsNullOrEmpty(Config.SmtpServer))
                smtpItems = new List<MailMessage>();

            var mailChangeListQuery = from cl in context.MailChangeLists
                                      join rv in context.Reviewers on cl.ReviewerId equals rv.Id
                                      join ch in context.ChangeLists on cl.ChangeListId equals ch.Id
                                      select new { cl, rv.ReviewerAlias, ch.UserName, ch.SourceControlId, ch.CL,
                                          ch.Description };

            foreach (var request in mailChangeListQuery)
            {
                logger.Log("Sending new review request for {0} to {1}", request.CL, request.ReviewerAlias);

                string subject = templates.CreateMailSubject(MailTemplates.MailType.Request, request.CL,
                    request.UserName, Abbreviate(request.Description, MaxDescriptionLength), null);
                bool isBodyHtml;
                string body = templates.CreateMail(MailTemplates.MailType.Request, request.cl.ChangeListId,
                    ResolveFriendlyName(Config, request.ReviewerAlias), ResolveFriendlyName(Config, request.UserName),
                    Config.WebServer, sourceControlRoots[request.SourceControlId],
                    null, request.Description, request.CL, out isBodyHtml);

                try
                {
                    string email = ResolveUser(Config, request.ReviewerAlias);
                    string replyToAlias = ResolveUser(Config, request.UserName);
                    string sender = ResolveUser(Config, Config.User);
                    string from = Config.FromEmail == null ? null : Config.FromEmail + "@" + Config.EmailDomain;
                    if (exchangeItems != null)
                    {
                        exchangeItems.Add(MakeExchangeMessage(email, from, replyToAlias, subject, body, isBodyHtml,
                            request.cl.ChangeListId, true));
                    }

                    if (smtpItems != null)
                    {
                        smtpItems.Add(MakeSmtpMessage(email, from, replyToAlias, sender, subject, body, isBodyHtml,
                            request.cl.ChangeListId, true));
                    }
                }
                catch (FormatException)
                {
                    logger.Log("Could not send email - invalid email format!");
                }

                context.MailChangeLists.DeleteOnSubmit(request.cl);
            }

            var reviewInviteQuery = (from ri in context.MailReviewRequests
                                     join ch in context.ChangeLists on ri.ChangeListId equals ch.Id
                                     select new { ri, ch }).ToArray();

            foreach (var invite in reviewInviteQuery)
            {
                logger.Log("Sending new review invitation for {0} to {1}", invite.ch.CL,
                    invite.ri.ReviewerAlias);

                string subject = templates.CreateMailSubject(MailTemplates.MailType.Invite, invite.ch.CL,
                    invite.ch.UserName, Abbreviate(invite.ch.Description, MaxDescriptionLength), null);
                bool isBodyHtml;
                string body = templates.CreateMail(MailTemplates.MailType.Invite, invite.ch.Id, null,
                    ResolveFriendlyName(Config, invite.ch.UserName), Config.WebServer,
                    sourceControlRoots[invite.ch.SourceControlId], null,
                    invite.ch.Description, invite.ch.CL, out isBodyHtml);

                try
                {
                    string email = ResolveUser(Config, invite.ri.ReviewerAlias);
                    string replyToAlias = ResolveUser(Config, invite.ch.UserName);
                    string sender = ResolveUser(Config, Config.User);
                    string from = Config.FromEmail == null ? null : Config.FromEmail + "@" + Config.EmailDomain;
                    if (exchangeItems != null)
                        exchangeItems.Add(MakeExchangeMessage(email, from, replyToAlias, subject, body, isBodyHtml,
                            invite.ch.Id, true));

                    if (smtpItems != null)
                        smtpItems.Add(MakeSmtpMessage(email, from, replyToAlias, sender, subject, body, isBodyHtml,
                            invite.ch.Id, true));
                }
                catch (FormatException)
                {
                    logger.Log("Could not send email - invalid email format!");
                }

                context.MailReviewRequests.DeleteOnSubmit(invite.ri);
            }

            var mailReviewQuery = (from mr in context.MailReviews
                                   join rw in context.Reviews on mr.ReviewId equals rw.Id
                                   join cc in context.ChangeLists on rw.ChangeListId equals cc.Id
                                   select new { mr, rw, cc }).ToArray();
            foreach (var request in mailReviewQuery)
            {
                logger.Log("Sending review notification for {0}", request.cc.CL);

                string from = Config.FromEmail == null ? null : Config.FromEmail + "@" + Config.EmailDomain;
                string sender = ResolveUser(Config, Config.User);

                List<string> to = new List<string>();
                List<string> cc = new List<string>();

                if (request.cc.UserName.Equals(request.rw.UserName, StringComparison.InvariantCultureIgnoreCase))
                {
                    // This is a response to review comments. The reviewers are on the 'to' line, reviewee is on 'cc'.
                    cc.Add(ResolveUser(Config, request.cc.UserName));

                    foreach (Reviewer r in request.cc.Reviewers)
                        to.Add(ResolveUser(Config, r.ReviewerAlias));
                }
                else
                {
                    // This is a review. The reviewee is on the 'to' line, the reviewers are on 'cc'.
                    to.Add(ResolveUser(Config, request.cc.UserName));

                    foreach (Reviewer r in request.cc.Reviewers)
                        cc.Add(ResolveUser(Config, r.ReviewerAlias));
                }

                string replyToAlias = ResolveUser(Config, request.rw.UserName);

                string malevichUrl = "http://" + Config.WebServer + sourceControlRoots[request.cc.SourceControlId] +
                    "/default.aspx";

                StringBuilder comments = new StringBuilder();
                string body;
                bool isBodyHtml;
                string subject;
                if (request.cc.UserName.Equals(request.rw.UserName, StringComparison.InvariantCultureIgnoreCase))
                {
                    subject = templates.CreateMailSubject(MailTemplates.MailType.Response, request.cc.CL,
                        request.cc.UserName, Abbreviate(request.cc.Description, MaxDescriptionLength), null);

                    isBodyHtml = templates.IsTemplateHtml(MailTemplates.MailType.Response);

                    ListCommentsForReview(context, request.rw, comments, isBodyHtml, malevichUrl);

                    body = templates.CreateMail(MailTemplates.MailType.Response, request.cc.Id, null,
                        ResolveFriendlyName(Config, request.rw.UserName), Config.WebServer,
                        sourceControlRoots[request.cc.SourceControlId], null,
                        comments.ToString(), request.cc.CL);
                }
                else
                {
                    string verdict = ReviewStatusToSentence(request.rw.OverallStatus);

                    subject = templates.CreateMailSubject(MailTemplates.MailType.Iteration, request.cc.CL,
                        request.cc.UserName, Abbreviate(request.cc.Description, MaxDescriptionLength), verdict);

                    isBodyHtml = templates.IsTemplateHtml(MailTemplates.MailType.Iteration);

                    ListCommentsForReview(context, request.rw, comments, isBodyHtml, malevichUrl);

                    body = templates.CreateMail(MailTemplates.MailType.Iteration, request.cc.Id,
                        ResolveFriendlyName(Config, request.rw.UserName), ResolveFriendlyName(Config, request.cc.UserName),
                        Config.WebServer, sourceControlRoots[request.cc.SourceControlId],
                        verdict, comments.ToString(), request.cc.CL);
                }

                try
                {
                    if (exchangeItems != null)
                        exchangeItems.Add(MakeExchangeMessage(to, cc, from, replyToAlias, subject, body.ToString(),
                            isBodyHtml, request.cc.Id, false));

                    if (smtpItems != null)
                        smtpItems.Add(MakeSmtpMessage(to, cc, from, replyToAlias, sender, subject, body.ToString(),
                            isBodyHtml, request.cc.Id, false));
                }
                catch (FormatException)
                {
                    logger.Log("Could not send email - invalid email format!");
                }

                context.MailReviews.DeleteOnSubmit(request.mr);
            }

            if (remindDaysOld > 0)
            {
                DateTime threshold = DateTime.Now.AddDays(-remindDaysOld);
                var oldChangeListQuery = from rr in context.ChangeLists
                                         where rr.Stage == 0 && rr.TimeStamp < threshold
                                         select rr;

                foreach (ChangeList cl in oldChangeListQuery)
                {
                    logger.Log("Sending review reminder for {0}", cl.CL);

                    string subject = templates.CreateMailSubject(MailTemplates.MailType.Reminder, cl.CL, cl.UserName,
                        Abbreviate(cl.Description, MaxDescriptionLength), null);
                    string email = ResolveUser(Config, cl.UserName);
                    string sender = ResolveUser(Config, Config.User);
                    string from = Config.FromEmail == null ? null : Config.FromEmail + "@" + Config.EmailDomain;
                    bool isBodyHtml;
                    string body = templates.CreateMail(MailTemplates.MailType.Reminder, cl.Id,
                        null, ResolveFriendlyName(Config, cl.UserName), Config.WebServer,
                        sourceControlRoots[cl.SourceControlId],
                        null, cl.Description, cl.CL, out isBodyHtml);

                    try
                    {
                        if (exchangeItems != null)
                            exchangeItems.Add(MakeExchangeMessage(email, from, null, subject, body, isBodyHtml,
                                cl.Id, false));

                        if (smtpItems != null)
                            smtpItems.Add(MakeSmtpMessage(email, from, null, sender, subject, body, isBodyHtml,
                                cl.Id, false));
                    }
                    catch (FormatException)
                    {
                        logger.Log("Could not send email - invalid email format!");
                    }
                }
            }

            if (exchangeItems != null && exchangeItems.Count() > 0)
                SendExchangeMail(Config, exchangeItems);
            if (smtpItems != null && smtpItems.Count() > 0)
                SendSmtpMail(Config, smtpItems);

            context.SubmitChanges();

            logger.Log("Finished processing review mail @ {0}", DateTime.Now);

            logger.Close();
        }

        private static ReviewNotifierConfiguration Config
        {
            get { return ReviewNotifierConfiguration.Get(); }
        }
    }

    class ReviewNotifierConfiguration : ConfigurationSection
    {
        private static string NullIfEmpty(string str)
        {
            return string.IsNullOrEmpty(str) ? null : str;
        }

        public ReviewNotifierConfiguration()
        {
        }

        /// <summary>
        /// User name.
        /// E.g. alice
        /// </summary>
        [ConfigurationProperty("user")]
        public string User
        {
            get { return NullIfEmpty((string)this["user"]); }
            set { this["user"] = value; }
        }

        /// <summary>
        /// Password.
        /// </summary>
        [ConfigurationProperty("password")]
        public string Password
        {
            get { return NullIfEmpty((string)this["password"]); }
            set { this["password"] = value; }
        }

        /// <summary>
        /// User domain (as in DOMAIN\user).
        /// E.g. REDMOND
        /// </summary>
        [ConfigurationProperty("domain")]
        public string Domain
        {
            get { return NullIfEmpty((string)this["domain"]); }
            set { this["domain"] = value; }
        }

        /// <summary>
        /// User account (without email domain) from which to send.
        /// E.g. bob
        /// </summary>
        [ConfigurationProperty("fromEmail")]
        public string FromEmail
        {
            get { return NullIfEmpty((string)this["fromEmail"]); }
            set { this["fromEmail"] = value; }
        }

        /// <summary>
        /// The database instance.
        /// E.g. localhost\mysqlinstance
        /// </summary>
        [ConfigurationProperty("database")]
        public string Database
        {
            get { return NullIfEmpty((string)this["database"]); }
            set { this["database"] = value; }
        }

        /// <summary>
        /// Web server where Malevich is hosted.
        /// E.g. sergeydev1
        /// </summary>
        [ConfigurationProperty("webServer")]
        public string WebServer
        {
            get { return NullIfEmpty((string)this["webServer"]); }
            set { this["webServer"] = value; }
        }

        /// <summary>
        /// If using Exchange, the URL of email service. Otherwise null.
        /// E.g. https://mail.microsoft.com/EWS/Exchange.asmx
        /// </summary>
        [ConfigurationProperty("emailService")]
        public string EmailService
        {
            get { return NullIfEmpty((string)this["emailService"]); }
            set { this["emailService"] = value; }
        }

        /// <summary>
        /// If using SMTP server, its hostname.
        /// E.g. smtp.redmond.microsoft.com
        /// </summary>
        [ConfigurationProperty("smtpServer")]
        public string SmtpServer
        {
            get { return NullIfEmpty((string)this["smtpServer"]); }
            set { this["smtpServer"] = value; }
        }

        /// <summary>
        /// Whether to use SSL with the smtp service. Only used for SMTP transport.
        /// </summary>
        [ConfigurationProperty("useSsl")]
        public bool UseSsl
        {
            get { return (bool)this["useSsl"]; }
            set { this["useSsl"] = value; }
        }

        /// <summary>
        /// Whether to use ActiveDirectory to resolve email addresses.
        /// </summary>
        [ConfigurationProperty("useLdap")]
        public bool UseLdap
        {
            get { return (bool)this["useLdap"]; }
            set { this["useLdap"] = value; }
        }

        /// <summary>
        /// The email domain.
        /// </summary>
        [ConfigurationProperty("emailDomain")]
        public string EmailDomain
        {
            get { return NullIfEmpty((string)this["emailDomain"]); }
            set { this["emailDomain"] = value; }
        }

        /// <summary>
        /// The log file.
        /// </summary>
        [ConfigurationProperty("logFile")]
        public string LogFile
        {
            get { return NullIfEmpty((string)this["logFile"]); }
            set { this["logFile"] = value; }
        }

        /// <summary>
        /// Verifies that various pieces are either missing, or correctly formatted.
        /// </summary>
        /// <returns></returns>
        internal bool VerifyParts()
        {
            bool result = true;
            if ((User != null) && (User.Contains('@') || User.Contains('\\')))
            {
                Console.Error.WriteLine("User name should not contain the domain information. E.g.: bob");
                result = false;
            }
            if ((Domain != null) && (Domain.Contains('.') || Domain.Contains('@') || Domain.Contains('\\')))
            {
                Console.Error.WriteLine("Domain name should be unqualified netbios domain. E.g.: REDMOND");
                result = false;
            }
            if ((FromEmail != null) && (FromEmail.Contains('@') || FromEmail.Contains('\\')))
            {
                Console.Error.WriteLine("'From' user name should not contain the domain information. E.g.: alice");
                result = false;
            }
            if ((EmailService != null) &&
                !(EmailService.StartsWith("http", StringComparison.InvariantCultureIgnoreCase) &&
                EmailService.EndsWith("asmx", StringComparison.InvariantCultureIgnoreCase)))
            {
                Console.Error.WriteLine("Exchange service does not seem to be configured correctly.");
                Console.Error.WriteLine("Expecting something like: https://mail.microsoft.com/EWS/Exchange.asmx");
                result = false;
            }
            if ((SmtpServer != null) && ((SmtpServer.Contains('@') || SmtpServer.Contains('\\') ||
                SmtpServer.Contains('/'))))
            {
                Console.Error.WriteLine("SMTP server hostname contains incorrect characters.");
                Console.Error.WriteLine("Expecting something like: smtp.redmond.microsoft.com");
                result = false;
            }
            if ((EmailDomain != null) && (EmailDomain.Contains('@') || EmailDomain.Contains('\\') ||
                EmailDomain.Contains('/')))
            {
                Console.Error.WriteLine("Email domain contains incorrect characters.");
                Console.Error.WriteLine("Expecting something like: microsoft.com");
                result = false;
            }

            return result;
        }

        /// <summary>
        /// Verifies that the configuration is in a ready to run state.
        /// </summary>
        internal bool VerifyWhole()
        {
            if (!VerifyParts())
                return false;

            bool result = true;
            if (SmtpServer != null && EmailService != null)
            {
                Console.Error.WriteLine("Can only have either SMTP or Exchange configured.");
                Console.Error.WriteLine("Please reset the configuration and try again.");
                result = false;
            }

            if ((SmtpServer == null && EmailService == null) || User == null || Database == null ||
                WebServer == null || EmailDomain == null)
            {
                Console.Error.WriteLine("You need to configure user credentials, mail server, web server, " +
                    "and database connection string first.");
                result = false;
            }

            return result;
        }

        private volatile static Configuration _config = null;
        private const string _sectionName = "reviewNotifier";
        private volatile static ReviewNotifierConfiguration _section = null;
        private static object _lock = new object();

        public static bool Load()
        {
            if (_section == null)
            {
                lock (_lock)
                {
                    if (_section == null)
                    {
                        //Configuration exeConfig = ConfigurationManager.OpenExeConfiguration(Environment.GetCommandLineArgs()[0]);
                        Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                        var section = (ReviewNotifierConfiguration)config.GetSection(_sectionName);
                        if (section == null)
                        {
                            section = new ReviewNotifierConfiguration();
                            section.SectionInformation.AllowExeDefinition = ConfigurationAllowExeDefinition.MachineToApplication;
                            section.SectionInformation.AllowLocation = false;
                        }
                        _config = config;
                        _section = section;
                    }
                }
            }
            return _section != null;
        }

        public static ReviewNotifierConfiguration Get()
        {
            Load();
            return _section;
        }

        public void Save()
        {
            if (_config == null || _section == null)
                throw new InvalidOperationException("Configuration has not yet been loaded, cannot save.");

            if (_config.Sections.Get(_sectionName) == null)
                _config.Sections.Add(_sectionName, _section);

            _config.Save(ConfigurationSaveMode.Minimal);
        }

        public void Clear()
        {
            Load();
            if (_config.Sections.Get(_sectionName) != null)
                _config.Sections.Remove(_sectionName);
        }
    }
}

