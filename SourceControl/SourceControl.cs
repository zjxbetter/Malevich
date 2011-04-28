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
using System.Linq;
using System.Text;

using Malevich.Extensions;

namespace SourceControl
{
    /// <summary>
    /// An unrecoverable exception thrown when source control
    /// encounters a runtime error and cannot cope.
    /// 
    /// The error output has already been printed - this
    /// should be caught just to terminate the program.
    /// </summary>
    public class SourceControlRuntimeError : Exception
    {
    }

    /// <summary>
    /// The description of the changed text file.
    /// </summary>
    //@TODO: better to create an IChangeFile and expose that as the public iterface.
    public sealed class ChangeFile
    {
        /// <summary>
        /// What type of the change it is.
        /// 
        /// Ugly as hell, but this is also duplicated in website\Default.aspx.cs
        /// to remove the dependency on SourceControl - which pulls in a dependency
        /// on TFS which we definitely do not want on a web server.
        /// 
        /// Keep these two in sync!
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

        /// <summary>
        /// The name of the file, in source control semantics.
        /// </summary>
        public string ServerFileName;

        /// <summary>
        /// The name of the file, local.
        /// </summary>
        //@TODO: This is non-nullable in the database, so for now the
        //       empty string is used for the same purpose.
        public string LocalFileName;

        /// <summary>
        /// A convenience accessor to get a display-friendly file name. Prefers
        /// the local file name if available, and falls back to the server name.
        /// </summary>
        public string LocalOrServerFileName
        {
            get
            {
                return LocalFileName.IsNullOrEmpty() ? ServerFileName : LocalFileName;
            }
        }

        /// <summary>
        /// The original name of the file, if changed.
        /// If not null, should be used to get the base line.
        /// </summary>
        public string OriginalServerFileName;

        /// <summary>
        /// The action that is taken (ADD, EDIT, DELETE).
        /// </summary>
        public SourceControlAction Action;

        /// <summary>
        /// When the file was last modified, if this is not a deletion, in UTC.
        /// </summary>
        public DateTime? LastModifiedTime;

        /// <summary>
        /// The checked-out revision.
        /// </summary>
        public int Revision;

        /// <summary>
        /// Whether the file is text.
        /// </summary>
        public bool IsText;

        /// <summary>
        /// If ADD, the entire file; if EDIT - the diff; if DELETE - null.
        /// If !IsText, also null.
        /// </summary>
        public string Data;

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="serverFileName"> The file name inside the source control. </param>
        /// <param name="action"> The action (ADD, EDIT, DELETE). </param>
        /// <param name="revision"> The revision of the checked out file (0 if add). </param>
        /// <param name="isText"> True if this is a text file. </param>
        public ChangeFile(string serverFileName, SourceControlAction action, int revision, bool isText)
        {
            ServerFileName = serverFileName;
            Action = action;
            Revision = revision;
            IsText = isText;
            Data = null;
            LastModifiedTime = null;
            LocalFileName = string.Empty;
            serverFileName = null;
        }
    }

    /// <summary>
    /// The class that encapsulates a description of a change.
    /// </summary>
    //@TODO: better to create an IChange and expose that as the public iterface.
    public sealed class Change
    {
        /// <summary>
        /// The source control system where the change belongs.
        /// </summary>
        public ISourceControlSystem Server;

        /// <summary>
        /// The name of the client.
        /// </summary>
        public string SdClientName;

        /// <summary>
        /// The CL identifier, unique within the server.
        /// </summary>
        public string ChangeListId;

        /// <summary>
        /// The CL friendly name, for display purposes.
        /// </summary>
        public string ChangeListFriendlyName;

        /// <summary>
        /// When working with TFS shelvesets, the shelveset owner may be different
        /// from the current user.
        /// </summary>
        public string ChangeListOwner;

        /// <summary>
        /// Timestamp of the change, in UTC.
        /// </summary>
        public DateTime TimeStamp;

        /// <summary>
        /// The description of the change.
        /// </summary>
        public string Description;

        /// <summary>
        /// The text files in the change.
        /// </summary>
        public IList<ChangeFile> Files;

        /// <summary>
        /// The associated bug/workitem IDs.
        /// </summary>
        public IList<string> BugIds
        { get; set; }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="server"> The source control server where the change belongs. </param>
        /// <param name="sdClientName">In TFS, this is the name of the workspace.</param>
        /// <param name="changeListId"> The CL number of the change. </param>
        /// <param name="changeListOwner">The CL owner.</param>
        /// <param name="timeStamp"> UTC time when the change was created. </param>
        /// <param name="description"> The description of the change. </param>
        /// <param name="bugIds">The bug IDs associated with this change.</param>
        /// <param name="files"> The list of (only) text files that constitute the change. </param>
        public Change(
            ISourceControlSystem server,
            string sdClientName,
            string changeListId,
            string changeListOwner,
            DateTime timeStamp,
            string description,
            IList<string> bugIds,
            IList<ChangeFile> files)
        {
            Server = server;
            SdClientName = sdClientName == null ? string.Empty : sdClientName;
            ChangeListId = changeListId;
            ChangeListOwner = changeListOwner;
            TimeStamp = timeStamp;
            Description = description != null ? description : "";
            BugIds = bugIds;
            Files = files;
        }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="server"> The source control server where the change belongs. </param>
        /// <param name="sdClientName">In TFS, this is the name of the workspace.</param>
        /// <param name="changeListId"> The CL number of the change. </param>
        /// <param name="timeStamp"> UTC time when the change was created. </param>
        /// <param name="description"> The description of the change. </param>
        /// <param name="files"> The list of (only) text files that constitute the change. </param>
        public Change(
            ISourceControlSystem server, string sdClientName, string changeListId,
            DateTime timeStamp, string description, IList<ChangeFile> files)
            : this(server, sdClientName, changeListId, null, timeStamp, description, new string[0], files)
        {
        }
    }

    /// <summary>
    /// The type of the server.
    /// </summary>
    public enum SourceControlType
    {
        TFS = 0,
        SD = 1,
        PERFORCE = 2,
        SUBVERSION = 3,
    }

    /// <summary>
    /// Base interface for source control descriptors.
    /// </summary>
    public interface ISourceControlSystem
    {
        /// <summary>
        /// The source control system type.
        /// </summary>
        SourceControlType ServerType
        { get; }
    }

    /// <summary>
    /// Settings for the source control system.
    /// </summary>
    //@TODO: Turn this into an interface.
    public sealed class SourceControlSettings
    {
        /// <summary>
        /// Whether the changed file is coming from the local hard drive, or the server (which can only happen
        /// in the case of TFS shelf set).
        /// </summary>
        public enum DiffSource
        {
            /// <summary>
            /// Unspecified.
            /// </summary>
            Unspecified,

            /// <summary>
            /// Changed file is local.
            /// </summary>
            Local,

            /// <summary>
            /// Changed file is on the server (in TFS shelf set).
            /// </summary>
            Server
        };

        /// <summary>
        /// Source control server endpoint - whatever it is that uniquely identifies the server.
        /// This string is source control-type specific. This cannot be null.
        /// </summary>
        public string Port;

        /// <summary>
        /// Proxy server for the source control, if any. Null if none.
        /// </summary>
        public string Proxy;

        /// <summary>
        /// Client/workspace - source control specific string that identifies the local client.
        /// This cannot be null.
        /// </summary>
        public string Client;

        /// <summary>
        /// Client/workspace owner. If the workspace is owned by another account (shared workspaces)
        /// this should be specified.
        /// </summary>
        public string ClientOwner;

        /// <summary>
        /// User name for the source control system. Can be null if Windows auth is used for source control.
        /// </summary>
        public string User;

        /// <summary>
        /// The password. Can be null if Windows auth is used for source control.
        /// </summary>
        public string Password;

        /// <summary>
        /// Client executable. This can be null if executable is not used to connect to source control (e.g. TFS).
        /// </summary>
        public string ClientExe;

        /// <summary>
        /// Where is the changed file coming from?
        /// </summary>
        public DiffSource Diff = DiffSource.Unspecified;
    }

    /// <summary>
    /// Abstracts the source control interface.
    /// </summary>
    //@TODO: Should probably implement IDisposable to allow use of C#'s 'using' for disconnection.
    public interface ISourceControl
    {
        /// <summary>
        /// Connects to the server.
        /// </summary>
        bool Connect();

        /// <summary>
        /// Closes the connection to the server.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Gets the change from the source control system. The change must be pending.
        /// Returns null if any error occurs, or the change is not pending.
        /// </summary>
        /// <param name="changeList"> The CL identifier. </param>
        /// <param name="includeBranchedFiles"> Include the text of branched and integrated files. </param>
        /// <returns> The change. </returns>
        Change GetChange(string changeList, bool includeBranchedFiles);

        /// <summary>
        /// Gets the file from the source control as one text string. The file must be text.
        /// If error occurs (or the file is not text) the function returns null.
        /// </summary>
        /// <param name="depotFileName"> The name, in depot semantics. </param>
        /// <param name="revision"> The revision. </param>
        /// <returns> File text, as a string. </returns>
        string GetFile(string depotFileName, int revision, out DateTime? fileTime);
    }

    /// <summary>
    /// Simple interface to a bug owned by a bug tracking server. This is simple right
    /// now but can be expanded as required.
    /// </summary>
    public interface IBug
    {
        /// <summary>
        /// The bug's unique identifier within the server.
        /// </summary>
        string Id
        { get; }

        /// <summary>
        /// Adds a link to the bug. Will not add if it is a duplicate.
        /// </summary>
        /// <param name="target">Where to link.</param>
        /// <param name="comment">Information about the link.</param>
        /// <returns></returns>
        bool AddLink(Uri target, string comment);
    }

    /// <summary>
    /// Interface to a bug tracking server.
    /// </summary>
    public interface IBugServer : IDisposable
    {
        /// <summary>
        /// Establish a connection to the server.
        /// </summary>
        /// <returns>true if successful, false otherwise.</returns>
        bool Connect();

        /// <summary>
        /// Terminate connection with server.
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Look up the bug identified by id.
        /// </summary>
        /// <param name="id">The bug's unique identifier.</param>
        /// <returns>The bug interface on success; null on failure.</returns>
        IBug GetBug(string id);
    }

    /// <summary>
    /// What type of logging we want turned on. Note that errors are always logged.
    /// </summary>
    [Flags]
    public enum LogOptions
    {
        None = 0,
        ClientUtility = 0x01
    }

    /// <summary>
    /// If the source control system implements this interface, its logging can be controlled
    /// to a relatively fine granularity.
    /// </summary>
    public interface ILogControl
    {
        /// <summary>
        /// Set log levels.
        /// </summary>
        /// <param name="level"> The log level, see LogOptions. </param>
        void SetLogLevel(LogOptions level);
    }
}
