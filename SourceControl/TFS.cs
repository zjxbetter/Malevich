//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 

#define USE_DIFF_TOOL

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;

// For Debug.Assert
using Debug = System.Diagnostics.Debug;

using Microsoft.TeamFoundation;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

using SourceControl;

using Malevich.Util;
using Malevich.Extensions;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SourceControl.Tfs
{
    /// <summary>
    /// The implementation of the TFS class.
    /// </summary>
    public sealed class Tfs : ISourceControlSystem
    {
        /// <summary>
        /// The endpoint of the TFS server.
        /// </summary>
        public string Server;

        /// <summary>
        /// The workspace.
        /// </summary>
        public string Workspace;

        /// <summary>
        /// The workspace owner. Can be prefixed by a domain (DOMAIN\user).
        /// If null, defaults to current user.
        /// </summary>
        public string WorkspaceOwner;

        /// <summary>
        /// TFS user name. Can be prefixed by a domain (DOMAIN\user). Can be null.
        /// </summary>
        public string User;

        /// <summary>
        /// TFS password. Can be null.
        /// </summary>
        public string Passwd;

        /// <summary>
        /// If true, the file data comes from the shelveset, otherwise it comes from a local hard drive.
        /// </summary>
        public bool GetFilesFromShelveSet;

        SourceControlType ISourceControlSystem.ServerType
        { get { return SourceControlType.TFS; } }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="port"> The endpoint of the source perforce server (servername:tcpport). </param>
        /// <param name="client"> The name of the client. </param>
        /// <param name="workspace">The name of the workspace to user.</param>
        /// <param name="workspaceOwner">Owner of the workspace to use.</param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="GetFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        public Tfs(
            string server,
            string workspace,
            string workspaceOwner,
            string user,
            string passwd,
            bool getFilesFromShelveSet)
        {
            Server = server;
            Workspace = workspace;
            WorkspaceOwner = workspaceOwner;
            User = user;
            Passwd = passwd;
            GetFilesFromShelveSet = getFilesFromShelveSet;
        }
    }

    /// <summary>
    /// Credential provider for TFS.
    /// </summary>
    sealed class TfsCredentialProvider : ICredentialsProvider
    {
        /// <summary>
        /// User name.
        /// </summary>
        private string User;

        /// <summary>
        /// Domain.
        /// </summary>
        private string Domain;

        /// <summary>
        /// Password.
        /// </summary>
        private string Password;

        /// <summary>
        /// Server URI.
        /// </summary>
        private Uri ServerUri;

        /// <summary>
        /// How many attempts have we made.
        /// </summary>
        private int Attempts;

        /// <summary>
        /// Supplies TFS with credentials.
        /// </summary>
        /// <param name="uri"> The uri of the TFS server. </param>
        /// <param name="failedCredentials"> The credentials that were provided, but did not work. </param>
        /// <returns></returns>
        ICredentials ICredentialsProvider.GetCredentials(Uri uri, ICredentials failedCredentials)
        {
            Console.WriteLine("Attempting to authenticate with " + uri.ToString());
            if (Attempts++ > 0)
            {
                // This is second attempt, but we don't have anything other than we already have given.
                Console.WriteLine("Could not authenticate... Giving up.");
                return null;
            }

            if (!ServerUri.Equals(uri))
            {
                Console.WriteLine("Authentication requested for unknown server.");
                Console.WriteLine("Request for " + uri.ToString());
                Console.WriteLine("Have credentials for " + ServerUri.ToString());
                return null;
            }

            return new NetworkCredential(User, Password, Domain);
        }

        /// <summary>
        /// Notifies the user that TFS connection was authenticated.
        /// </summary>
        /// <param name="uri"></param>
        void ICredentialsProvider.NotifyCredentialsAuthenticated(Uri uri)
        {
            Console.WriteLine("Successfully authenticated with " + uri.ToString());
        }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="userName"> User name, can have domain name in it. </param>
        /// <param name="password"> Password. </param>
        /// <param name="server"> Server URI. </param>
        public TfsCredentialProvider(string userName, string password, string server)
        {
            string[] creds = userName.Split('\\');
            if (creds.Length > 1)
            {
                Domain = creds[0];
                User = creds[1];
            }
            else
            {
                User = userName;
            }
            Password = password;
            ServerUri = new Uri(server);
        }
    }

    /// <summary>
    /// Provides base functionality for the source control and workitem TFS classes.
    /// </summary>
    class TfsServerBase
    {
        /// <summary>
        /// Source control system.
        /// </summary>
        public Tfs Tfs { get; set; }

        /// <summary>
        /// TFS interface. Initialized in Connect.
        /// </summary>
        public TeamFoundationServer TfsServer { get; set; }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="server"> The Tfs server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        public TfsServerBase(
            string server,
            string workspace,
            string workspaceOwner,
            string user,
            string passwd,
            bool getFilesFromShelveSet)
        {
            Tfs = new Tfs(server, workspace, workspaceOwner, user, passwd, getFilesFromShelveSet);
        }

        /// <summary>
        /// Connects to TFS.
        /// </summary>
        public bool Connect()
        {
            ICredentialsProvider creds = Tfs.User == null ? ((ICredentialsProvider)new UICredentialsProvider()) :
                ((ICredentialsProvider)new TfsCredentialProvider(Tfs.User, Tfs.Passwd, Tfs.Server));

            try
            {
                TfsServer = TeamFoundationServerFactory.GetServer(Tfs.Server, creds);
                TfsServer.EnsureAuthenticated();
            }
            catch (TeamFoundationServerUnauthorizedException)
            {
                Console.WriteLine("Could not connect to " + Tfs.Server + " using supplied credentials.");
                TfsServer = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Disconnects from TFS. Does nothing, really.
        /// </summary>
        public void Disconnect()
        {
        }
    }

    /// <summary>
    /// The TFS Source Control interface.
    /// </summary>
    sealed class TfsInterface : TfsServerBase, ISourceControl
    {
        /// <summary>
        /// Version control server. Initialized in Connect.
        /// </summary>
        private VersionControlServer VcsServer;

        /// <summary>
        /// Current workspace.
        /// </summary>
        private Workspace Workspace;

        /// <summary>
        /// Connects to TFS.
        /// </summary>
        bool ISourceControl.Connect()
        {
            if (base.Connect())
            {
                VcsServer = (VersionControlServer)TfsServer.GetService(typeof(VersionControlServer));
                if (!Tfs.GetFilesFromShelveSet)
                    Workspace = VcsServer.GetWorkspace(
                        Tfs.Workspace, Tfs.WorkspaceOwner == null ? TfsServer.AuthenticatedUserName
                                                                  : Tfs.WorkspaceOwner);
            }

            return VcsServer != null && (Tfs.GetFilesFromShelveSet || Workspace != null);
        }

        /// <summary>
        /// Disconnects from TFS.
        /// </summary>
        void ISourceControl.Disconnect()
        {
            base.Disconnect();
        }

        /// <summary>
        /// Returns if the encoding type is text.
        /// </summary>
        /// <param name="encoding"> Encoding type. </param>
        /// <returns> True if text. </returns>
        private bool IsTextEncoding(int encoding)
        {
            // TBD: Figure out how this really works.
            // So far we know that 65001 is utf-8, and -1 is binary.
            return encoding != -1;
        }

        /// <summary>
        /// Gets the change from the shelveset.
        /// Returns null if any error occurs, or the change is not pending.
        /// </summary>
        /// <param name="changeId"> shelveset identifier. </param>
        /// <param name="includeBranchedFiles"> Include full text for branched and integrated files. </param>
        /// <returns> The change. </returns>
        Change ISourceControl.GetChange(string changeId, bool includeBranchedFiles)
        {
            bool getFilesFromShelveSet = Tfs.GetFilesFromShelveSet;
            Debug.Assert(Tfs.GetFilesFromShelveSet || Workspace != null);

            string shelvesetName = changeId;
            string shelvesetOwner = TfsServer.AuthenticatedUserName;

            Shelveset shelveset = null;
            PendingSet[] sets = null;

            {
                var nameAndOwner = changeId.Split(new char[] { ';' });
                if (nameAndOwner.Length == 2)
                {
                    shelvesetName = nameAndOwner[0];
                    shelvesetOwner = nameAndOwner[1];
                }

                Shelveset[] shelvesets = VcsServer.QueryShelvesets(shelvesetName, shelvesetOwner);
                if (shelvesets.Length != 1)
                {
                    if (shelvesets.Count() == 0)
                        Console.WriteLine("Change not found.");
                    else
                        Console.WriteLine("Ambiguous change name.");

                    return null;
                }

                shelveset = shelvesets.First();

                if (getFilesFromShelveSet)
                    sets = VcsServer.QueryShelvedChanges(shelvesetName, shelvesetOwner, null, true);
                else
                    sets = VcsServer.QueryShelvedChanges(shelveset);
            }

            List<ChangeFile> files = new List<ChangeFile>();
            foreach (PendingSet set in sets)
            {
                PendingChange[] changes = set.PendingChanges;
                foreach (PendingChange change in changes)
                {
                    ChangeFile.SourceControlAction action;
                    string originalFileName = null;
                    if (change.ChangeTypeName.Equals("edit"))
                    {
                        action = ChangeFile.SourceControlAction.EDIT;
                    }
                    else if (change.ChangeTypeName.Equals("add"))
                    {
                        action = ChangeFile.SourceControlAction.ADD;
                    }
                    else if (change.ChangeTypeName.Equals("delete") || change.ChangeTypeName.Equals("merge, delete"))
                    {
                        action = ChangeFile.SourceControlAction.DELETE;
                    }
                    else if (change.ChangeTypeName.Equals("branch") || change.ChangeTypeName.Equals("merge, branch"))
                    {
                        action = ChangeFile.SourceControlAction.BRANCH;
                    }
                    else if (change.ChangeTypeName.Equals("merge, edit"))
                    {
                        action = ChangeFile.SourceControlAction.INTEGRATE;
                    }
                    else if (change.ChangeTypeName.Equals("rename"))
                    {
                        action = ChangeFile.SourceControlAction.RENAME;
                    }
                    else if (change.ChangeTypeName.Equals("rename, edit"))
                    {
                        action = ChangeFile.SourceControlAction.EDIT;

                        originalFileName = change.SourceServerItem;
                    }
                    else
                    {
                        Console.WriteLine("Unsupported action for file " + change.LocalItem + " : " +
                            change.ChangeTypeName);

                        return null;
                    }

                    ChangeFile file = new ChangeFile(change.ServerItem, action, change.Version,
                        (change.ItemType == ItemType.File) && IsTextEncoding(change.Encoding));

                    files.Add(file);

                    file.LocalFileName = Workspace.GetLocalItemForServerItem(change.ServerItem);

                    file.OriginalServerFileName = originalFileName;

                    if (getFilesFromShelveSet)
                    {
                        if (action != ChangeFile.SourceControlAction.DELETE)
                        {
                            file.LastModifiedTime = shelveset.CreationDate.ToUniversalTime();
                        }
                    }
                    else if (File.Exists(file.LocalFileName))
                    {
                        file.LastModifiedTime = File.GetLastWriteTimeUtc(file.LocalFileName);
                    }

                    if (!file.IsText)
                        continue;

                    // Store the entire file.
                    if (action == ChangeFile.SourceControlAction.ADD ||
                        (action == ChangeFile.SourceControlAction.BRANCH && includeBranchedFiles))
                    {
                        if (getFilesFromShelveSet)
                        {
                            using (Malevich.Util.TempFile tempFile = new Malevich.Util.TempFile())
                            {
                                change.DownloadShelvedFile(tempFile.FullName);
                                file.Data = File.ReadAllText(tempFile.FullName);
                            }
                        }
                        else
                        {
                            file.Data = File.ReadAllText(file.LocalFileName);
                        }
                    }

                    // Store the diff.
                    else if (action == ChangeFile.SourceControlAction.EDIT ||
                             (action == ChangeFile.SourceControlAction.INTEGRATE && includeBranchedFiles))
                    {
#if USE_DIFF_TOOL
                        using (var baseFile = new TempFile())
                        using (var changedFile = new TempFile())
                        {
                            change.DownloadBaseFile(baseFile.FullName);
                            change.DownloadShelvedFile(changedFile.FullName);

                            string args = baseFile.FullName + " " + changedFile.FullName;

                            using (Process diff = new Process())
                            {
                                diff.StartInfo.UseShellExecute = false;
                                diff.StartInfo.RedirectStandardError = true;
                                diff.StartInfo.RedirectStandardOutput = true;
                                diff.StartInfo.CreateNoWindow = true;
                                diff.StartInfo.FileName = @"bin\diff.exe";
                                diff.StartInfo.Arguments = args;
                                diff.Start();

                                string stderr;
                                file.Data = Malevich.Util.CommonUtils.ReadProcessOutput(diff, false, out stderr);
                            }
                        }
#else
                        IDiffItem changedFile = getFilesFromShelveSet ?
                            (IDiffItem)new DiffItemShelvedChange(shelveset.Name, change) :
                            (IDiffItem)new DiffItemLocalFile(file.LocalFileName, change.Encoding,
                                file.LastModifiedTime.Value, false);
                        DiffItemPendingChangeBase baseFile = new DiffItemPendingChangeBase(change);

                        // Generate the diffs
                        DiffOptions options = new DiffOptions();
                        options.OutputType = DiffOutputType.UnixNormal;
                        options.UseThirdPartyTool = false;


                        using (var memStream = new MemoryStream())
                        {
                            options.StreamWriter = new StreamWriter(memStream);
                            {
                                // DiffFiles closes options.StreamWriter.
                                Difference.DiffFiles(VcsServer, baseFile, changedFile, options, null, true);
                                memStream.Seek(0, SeekOrigin.Begin);

                                // Remove TFS-specific delimiters.
                                using (StreamReader reader = new StreamReader(memStream))
                                {
                                    StringBuilder sb = new StringBuilder();
                                    for (string line = reader.ReadLine(); line != null; line = reader.ReadLine())
                                    {
                                        if (line.Equals(""))
                                            continue;

                                        if (line.StartsWith("="))
                                            continue;

                                        sb.Append(line);
                                        sb.Append('\n');
                                    }
                                    file.Data = sb.ToString();
                                }
                            }
                        }
#endif
                    }
                }
            }

            if (files.Count() == 0)
            {
                Console.WriteLine("The shelveset does not contain any files!");
                return null;
            }

            // Iterate the workitems associated with the bug and store the IDs.
            var bugIds = new List<string>();
            foreach (WorkItemCheckinInfo workItemInfo in shelveset.WorkItemInfo)
            {
                var workItem = workItemInfo.WorkItem;
                bugIds.Add(workItem.Id.ToString());
            }

            return new Change(
                Tfs,
                Tfs.Workspace,
                shelveset.Name,
                shelveset.OwnerName,
                shelveset.CreationDate.ToUniversalTime(),
                shelveset.Comment,
                bugIds,
                files) { ChangeListFriendlyName = shelveset.DisplayName.Split(new char[] { ';' })[0] };
        }

        /// <summary>
        /// Reads a file from the source control system.
        /// </summary>
        /// <param name="depotFileName"> The server name of the file. </param>
        /// <param name="revision"> The revision of the file to get. </param>
        /// <returns> The string that constitutes the body of the file. </returns>
        string ISourceControl.GetFile(string name, int revision, out DateTime? timeStamp)
        {
            Item item = VcsServer.GetItem(name, VersionSpec.ParseSingleSpec(revision.ToString(),
                TfsServer.AuthenticatedUserName));
            timeStamp = item.CheckinDate.ToUniversalTime();

            string tempFile = Path.GetTempFileName();
            item.DownloadFile(tempFile);

            StreamReader reader = new StreamReader(tempFile);
            string fileContents = reader.ReadToEnd();
            reader.Close();

            File.Delete(tempFile);

            return fileContents;
        }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="server"> The TFS server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        private TfsInterface(
            string server,
            string workspace,
            string workspaceOwner,
            string user,
            string passwd,
            bool getFilesFromShelveSet)
            : base(server, workspace, workspaceOwner, user, passwd, getFilesFromShelveSet)
        {
        }

        /// <summary>
        /// Factory for the TFS connector instances.
        /// </summary>
        /// <param name="server"> The TFS server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        /// otherwise - from local disk. </param>
        /// <returns> The source control instance. </returns>
        public static TfsInterface GetInstance(
            string server, string workspace, string workspaceOwner,
            string user, string passwd, bool getFilesFromShelveSet)
        {
            return new TfsInterface(server, workspace, workspaceOwner, user, passwd, getFilesFromShelveSet);
        }
    }

    /// <summary>
    /// Implements IBug for a TFS workitem.
    /// </summary>
    class TfsBug : IBug
    {
        #region IBug Members

        /// <summary>
        /// The string value of the bug ID.
        /// </summary>
        string IBug.Id
        {
            get
            {
                return _workItem.Id.ToString();
            }
        }

        /// <summary>
        /// Adds a link to the bug. Will not add if already there.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="comment"></param>
        /// <returns></returns>
        bool IBug.AddLink(Uri target, string comment)
        {
            var link = new Microsoft.TeamFoundation.WorkItemTracking.Client.Hyperlink(target.ToString());
            link.Comment = comment == null ? "Review page" : comment;

            if (_workItem.Links.Contains(link))
                return false;

            // BUG in TFS: LinkCollection.Contains returns false even when the link exists, and then
            // on Add it throws a ValidationException. So keep checking but also put in a try/catch.
            try
            {
                _workItem.Links.Add(link);
            }
            catch (ValidationException)
            {
                return false;
            }

            var invalidFields = _workItem.Validate();
            if (invalidFields != null && invalidFields.Count != 0)
            {
                Console.WriteLine("Bug validation failed for bug ID {0}:", ((IBug)this).Id);
                foreach (var obj in invalidFields)
                {
                    var field = (Field)obj;
                    Console.WriteLine("Invalid field: {0}", field.Name);
                }
            }
            Debug.Assert(_workItem.IsValid());
            _workItem.Save();
            return true;
        }

        #endregion

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="workItem">The TFS workitem to wrap.</param>
        public TfsBug(Microsoft.TeamFoundation.WorkItemTracking.Client.WorkItem workItem)
        {
            _workItem = workItem;
        }

        /// <summary>
        /// The TFS workitem wrapped.
        /// </summary>
        private Microsoft.TeamFoundation.WorkItemTracking.Client.WorkItem _workItem;
    }

    /// <summary>
    /// The TFS bug tracking interface.
    /// </summary>
    class BugServer : TfsServerBase, IBugServer
    {
        /// <summary>
        /// The TFS interface used to access workitems.
        /// </summary>
        private WorkItemStore WorkItemStore { get; set; }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="server"> The Tfs server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        private BugServer(string server, string workspace, string workspaceOwner, string user, string passwd)
            : base(server, workspace, workspaceOwner, user, passwd, true)
        {
        }

        /// <summary>
        /// Factory for WorkItemStore connections.
        /// </summary>
        /// <param name="server"> The Tfs server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        /// otherwise - from local disk. </param>
        /// <returns> The source control instance. </returns>
        public static BugServer GetInstance(
            string server, string workspace, string workspaceOwner, string user, string passwd)
        {
            return new BugServer(server, workspace, workspaceOwner, user, passwd);
        }

        #region IBugServer Members

        /// <summary>
        /// Connects to the TFS workitem server.
        /// </summary>
        /// <returns></returns>
        bool IBugServer.Connect()
        {
            if (base.Connect())
                WorkItemStore = new WorkItemStore(TfsServer);

            if (WorkItemStore == null)
                Console.WriteLine("Request for TFS WorkItemStore service failed.");

            return WorkItemStore != null;
        }

        /// <summary>
        /// Disconnects from the TFS workitem server.
        /// </summary>
        void IBugServer.Disconnect()
        {
            base.Disconnect();
        }

        /// <summary>
        /// Gets an IBug interface for the given ID.
        /// </summary>
        /// <param name="id">The bug id.</param>
        /// <returns>An IBug interface, if the bug is found; null otherwise.</returns>
        IBug IBugServer.GetBug(string id)
        {
            int workItemId;
            if (!int.TryParse(id, out workItemId))
                return null;

            var workItem = WorkItemStore.GetWorkItem(workItemId);
            if (workItem == null)
                return null;

            return new TfsBug(workItem);
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Calls Disconnect.
        /// </summary>
        void IDisposable.Dispose()
        {
            Disconnect();
        }

        #endregion
    }

    public static class Factory
    {
        /// <summary>
        /// Factory for the TFS connector instances.
        /// </summary>
        /// <param name="server"> The Tfs server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        /// otherwise - from local disk. </param>
        /// <returns> The source control instance. </returns>
        public static ISourceControl GetISourceControl(
            string server, string workspace, string workspaceOwner,
            string user, string passwd, bool getFilesFromShelveSet)
        {
            return TfsInterface.GetInstance(server, workspace, workspaceOwner, user, passwd, getFilesFromShelveSet);
        }

        /// <summary>
        /// Factory for WorkItemStore connections.
        /// </summary>
        /// <param name="server"> The Tfs server. </param>
        /// <param name="workspace"> The workspace. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <param name="getFilesFromShelveSet"> If true, the file versions are coming from shelvesets,
        /// otherwise - from local disk. </param>
        /// <returns> The source control instance. </returns>
        public static IBugServer GetIBugServer(
            string server, string workspace, string workspaceOwner, string user, string passwd)
        {
            return BugServer.GetInstance(server, workspace, workspaceOwner, user, passwd);
        }

        /// <summary>
        /// Gets the Tfs client settings.
        /// </summary>
        /// <returns></returns>
        public static SourceControlSettings GetSettings()
        {
            SourceControlSettings settings = new SourceControlSettings();

            settings.Port = Environment.GetEnvironmentVariable("TFSSERVER");
            settings.Client = Environment.GetEnvironmentVariable("TFSWORKSPACE");
            settings.ClientOwner = Environment.GetEnvironmentVariable("TFSWORKSPACEOWNER");
            settings.User = Environment.GetEnvironmentVariable("TFSUSER");
            settings.Password = Environment.GetEnvironmentVariable("TFSPASSWORD");
            settings.Diff = SourceControlSettings.DiffSource.Unspecified;

            string tfsDiffSource = Environment.GetEnvironmentVariable("TFSDIFFSOURCE");
            if ("shelf".Equals(tfsDiffSource, StringComparison.InvariantCultureIgnoreCase))
                settings.Diff = SourceControlSettings.DiffSource.Server;
            if ("local".Equals(tfsDiffSource, StringComparison.InvariantCultureIgnoreCase))
                settings.Diff = SourceControlSettings.DiffSource.Local;

            string path = Environment.GetEnvironmentVariable("path").Replace("\"", "");
            string[] pathArray = path.Split(';');
            for (int i = 0; i < pathArray.Length; ++i)
            {
                string tf = Path.Combine(pathArray[i], "tf.exe");
                if (File.Exists(tf))
                    settings.ClientExe = tf;
            }

            if (!AutoDiscoverTfsSettings(settings))
                Console.WriteLine("WARNING: Unable to detect ambient TFS settings.");

            return settings;
        }

        /// <summary>
        /// Autodiscovers TFS client based on the current directory.
        /// </summary>
        /// <param name="settings"></param>
        private static bool AutoDiscoverTfsSettings(SourceControlSettings settings)
        {
            try
            {
                string vcsConfig = Path.Combine(TeamFoundationServer.ClientCacheDirectory, "VersionControl.config");
                if (!File.Exists(vcsConfig))
                    return false;

                XmlDocument config = new XmlDocument();
                config.Load(vcsConfig);

                string dir = Directory.GetCurrentDirectory();
                if (!dir.EndsWithIgnoreCase("\\"))
                    dir += "\\";

                XmlNodeList servers = config.GetElementsByTagName("ServerInfo");
                foreach (XmlNode server in servers)
                {
                    if (server.ChildNodes == null)
                        continue;
                    foreach (XmlNode workspace in server.ChildNodes)
                    {
                        if (workspace.ChildNodes == null)
                            continue;

                        XmlNode paths = workspace["MappedPaths"];
                        if (paths == null)
                            continue;

                        foreach (XmlNode path in paths.ChildNodes)
                        {
                            XmlAttribute pathAttribute = path.Attributes["path"];
                            if (pathAttribute == null)
                                continue;

                            string value = pathAttribute.Value;
                            if (value == null)
                                continue;

                            if (!value.EndsWithIgnoreCase("\\"))
                                value += "\\";

                            if (dir.StartsWithIgnoreCase(value))
                            {
                                if (settings.Port == null)
                                {
                                    XmlAttribute uriAttribute = server.Attributes["uri"];
                                    if (uriAttribute != null)
                                        settings.Port = uriAttribute.Value;
                                }

                                if (settings.Client == null && settings.Diff == SourceControlSettings.DiffSource.Local)
                                {
                                    XmlAttribute workspaceAttribute = workspace.Attributes["name"];
                                    if (workspaceAttribute != null)
                                        settings.Client = workspaceAttribute.Value;

                                    XmlAttribute workspaceOwnerAttr = workspace.Attributes["ownerName"];
                                    if (workspaceOwnerAttr != null)
                                        settings.ClientOwner = workspaceOwnerAttr.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }

            return true;
        }
    }
}
