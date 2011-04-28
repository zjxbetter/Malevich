//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Jay Ongg for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Diagnostics;
using System.Text.RegularExpressions;


namespace SourceControl
{
    /// <summary>
    /// The implementation of the Subversion class.
    /// </summary>
    public sealed class Subversion : ISourceControlSystem
    {
        /// <summary>
        /// The Url of the Subversion server.
        /// </summary>
        public string Url;

        /// <summary>
        SourceControlType ISourceControlSystem.ServerType
        { get { return SourceControlType.SUBVERSION; } }

        /// Trivial constructor.
        /// </summary>
        /// <param name="url"> The url of the repository. </param>
        /// <param name="user"> Subversion user name, can be null. </param>
        /// <param name="passwd"> Subversion password, can be null. </param>
        public Subversion(string url)
        {
            Url = url;
        }
    }

    /// <summary>
    /// The Subversion Source Control interface.
    /// </summary>
    public sealed class SubversionInterface : ISourceControl, ILogControl
    {
        /// <summary>
        /// path to diffFromSvn.bat
        /// </summary>
        private string diffFromSvnBatFilename;

        /// <summary>
        /// the client name
        /// </summary>
        private string Client;

        /// <summary>
        /// The location of the Subversion client .exe.
        /// </summary>
        private string ClientExe;

        /// <summary>
        /// The local root of the repository
        /// </summary>
        private string localRoot;

        /// <summary>
        /// Source control system.
        /// </summary>
        private ISourceControlSystem SourceControl;

        /// <summary>
        /// Log zones that are turned on.
        /// </summary>
        private LogOptions LogLevel;

        /// <summary>
        /// Connects to Subversion. Does nothing, really.
        /// </summary>
        public bool Connect()
        {
            // Don't need to do anything.
            return true;
        }

        /// <summary>
        /// Controls the log output. Implemented here primarily to allow dumping the IO from the client utility.
        /// </summary>
        /// <param name="level"> The level of logging. </param>
        public void SetLogLevel(LogOptions level)
        {
            LogLevel = level;
        }

        /// <summary>
        /// Disconnects from Subversion. Does nothing, really.
        /// </summary>
        public void Disconnect()
        {
            // Don't need to do anything.
        }

        /// <summary>
        /// Gets the change from the repository
        /// Returns null if any error occurs, or the change is not pending.
        /// </summary>
        /// <param name="changeId"> shelveset identifier. </param>
        /// <param name="includeBranchedFiles"> Include full text for branched and integrated files. </param>
        /// <returns> The change. </returns>
        public Change GetChange(string changeId, bool includeBranchedFiles)
        {
            if (!File.Exists(diffFromSvnBatFilename))
            {
                Console.WriteLine("difffromsvn.bat does not exist in the same directory as review.exe.");
                Console.WriteLine(" This batch file must run 'diff.exe %6 %7'");
                return null;
            }

            // first get the list of all files (including paths):
            // svn status --changelist xxx 
            string result = RunClient(@"status --changelist """ + changeId + "\" \"" + localRoot + "\" ", false);

            StringReader sr = new StringReader(result);
            string line = null;

            bool seenDash = false;

            Regex fileAfterSpaceRegEx = new Regex(@"^(?<changetype>.).......(?<filename>(\S)+)$");

            List<ChangeFile> files = new List<ChangeFile>();

            // Results look like this:
            //--- Changelist 'mine':
            //        util\VisitorPrefsGetter.java
            //M       util\VisitorPrefsEidMigrator.java

            // Rules:
            // skip past first line that begins with "-"
            // then for each line, get the filename by finding the first non-whitespace after a white-space
            while ((line = sr.ReadLine()) != null)
            {
                if (!seenDash)
                {
                    if ((line.Length > 0) && (line[0] == '-'))
                    {
                        seenDash = true;
                    }
                    continue;
                }

                Match match = fileAfterSpaceRegEx.Match(line);
                if (!match.Success)
                {
                    Console.WriteLine("Could not interpret svn status output: " + line);
                    return null;
                }

                // what type of change is it?
                string typeOfChange = match.Groups[2].Value;
                if (typeOfChange.Length == 0)
                {
                    // no change, skip this
                    continue;
                }
                ChangeFile.SourceControlAction action = ChangeFile.SourceControlAction.EDIT;

                switch (typeOfChange)
                {
                    case "C":   // conflicted
                    case "M":   // modified
                    case "R":   // replaced
                    case " ":   // no change but added to the changelist
                        action = ChangeFile.SourceControlAction.EDIT;
                        break;
                    case "D":   // deleted
                    case "!":
                        action = ChangeFile.SourceControlAction.DELETE;
                        break;
                    case "I":   // ignored
                    case "A":   // added
                        action = ChangeFile.SourceControlAction.ADD;
                        break;
                    case "?":   // not under version control
                        break;
                    default:
                        // unexpected
                        Console.WriteLine("Unexpected change type: " + typeOfChange);
                        return null;
                }
                
                // this is the filename
                string filename = match.Groups[3].Value;

                bool isText = false;
                // get the mime-type to see if it's text
                string mimetype = RunClient("propget svn:mime-type " + "\"" + filename + "\"", false);
                if (mimetype == null || mimetype.StartsWith("text") || mimetype.Length == 0)
                {
                    isText = true;
                }


                // get the current revision
                string serverFilename = null;
                int revisionId = GetRevisionFromFile(filename, ref serverFilename);

                ChangeFile cf = new ChangeFile(serverFilename, action, revisionId, isText);
                cf.LocalFileName = filename;
                files.Add(cf);
            }

            if (!seenDash)
                return null;

            if (files.Count > 0)
            {
                // TODO: figure out name of client and description
                Change change = new Change(SourceControl, Client, changeId, 
                    DateTime.Now.ToUniversalTime(), changeId, files.ToArray());

                FillInFileData(change);
                return change;
            }

            return null;
        }

        /// <summary>
        /// Gets the current revision of a file.
        /// </summary>
        /// <param name="filename"> The local filename. </param>
        /// <param name="serverFilename"> The server filename</param>
        /// <returns> The revision number. </returns>
        int GetRevisionFromFile(string filename, ref string serverFilename)
        {
            Regex revisionRegEx = new Regex(@"^(\s)*Revision(\s)*:(\s)*(?<revision>(\S)*)(\s)*$",
                RegexOptions.ExplicitCapture);
            Regex urlRegEx = new Regex(@"^(\s)*URL(\s)*:(\s)*(?<url>(\S)*)(\s)*$",
                RegexOptions.ExplicitCapture);
            string svnInfo = RunClient("info " + "\"" + filename + "\"", false);

            StringReader sr = new StringReader(svnInfo);
            string currentRevision = null;

            while (true)
            {
                string line = sr.ReadLine();
                if (line == null)
                    break;

                Match match = revisionRegEx.Match(line);
                if (match.Success)
                {
                    currentRevision = match.Groups[1].Value;
                }

                Match urlMatch = urlRegEx.Match(line);
                if (urlMatch.Success)
                {
                    serverFilename = urlMatch.Groups[1].Value;
                }
            }

            if (currentRevision == null)
            {
                currentRevision = "0";
            }

            return int.Parse(currentRevision);
        }

        /// <summary>
        /// Iterates through every file in the change list, and:
        ///     1. fills in its local path and 
        ///     2. diff if it is an edit, or the add file itself if it is an add.
        /// </summary>
        /// <param name="change"> The change list, instantiated. </param>
        private void FillInFileData(Change change)
        {
            foreach (ChangeFile file in change.Files)
            {
                if (File.Exists(file.LocalFileName))
                    file.LastModifiedTime = File.GetLastWriteTimeUtc(file.LocalFileName);

                if (!file.IsText)
                    continue;

                if (file.Action == ChangeFile.SourceControlAction.EDIT ||
                    (file.Action == ChangeFile.SourceControlAction.INTEGRATE))
                {
                    // The following depends on a diff command that will diff %6 and %7.
                    // Contents of the file is one line:
                    // @diff %6 %7
                    // This is something that the svn developers recommend (they choose not to fix this)
                    string result = RunClient("diff --diff-cmd \"" + diffFromSvnBatFilename + "\" \"" + file.LocalFileName + "\"",
                                                true);
                    // skip past first line
                    StringReader sr = new StringReader(result);
                    StringBuilder sb = new StringBuilder();
                    for (; ; )
                    {
                        string line = sr.ReadLine();
                        if (line == null)
                            break;

                        if (line.Equals(""))
                            continue;

                        if (line.StartsWith("="))
                            continue;

                        sb.Append(line);
                        sb.Append('\n');
                    }

                    file.Data = sb.ToString();
                    sr.Close();
                }
                else if (file.Action == ChangeFile.SourceControlAction.ADD ||
                    (file.Action == ChangeFile.SourceControlAction.BRANCH))
                {
                    try
                    {
                        StreamReader reader = new StreamReader(file.LocalFileName);
                        file.Data = reader.ReadToEnd();
                        reader.Close();
                    }
                    catch (FileNotFoundException)
                    {
                        Console.WriteLine("File not found: " + file.LocalFileName);
                        throw new SourceControlRuntimeError();
                    }
                }
            }
        }

        /// <summary>
        /// Reads a file from the source control system.
        /// </summary>
        /// <param name="depotFileName"> The server name of the file. </param>
        /// <param name="revision"> The revision of the file to get. </param>
        /// <returns> The string that constitutes the body of the file. </returns>
        public string GetFile(string name, int revision, out DateTime? timeStamp)
        {
            string body = RunClient("cat " + "\"" + name + "\"", false);

            // get the timestamp
            string infoAtRevision = RunClient("info " + "\"" + name + "\"" + "@" + revision.ToString(), false);
            string lastChangedDate = null;

            Regex lastChangedRegEx = new Regex(@"^(\s)*Last Changed Date(\s)*:(\s)*(?<lastchangeddate>.*)$",
                RegexOptions.ExplicitCapture);

            StringReader sr = new StringReader(infoAtRevision);
            while (true)
            {
                string line = sr.ReadLine();
                if (line == null)
                    break;

                Match match = lastChangedRegEx.Match(line);
                if (match.Success)
                {
                    lastChangedDate = match.Groups[1].Value;
                    break;
                }
            }

            // “2010-03-17 15:54:07 -0700 (Wed, 17 Mar 2010)”
            string lastChangeDateFirstPart = lastChangedDate.Substring(0, 19);
            timeStamp = (DateTime.ParseExact(lastChangeDateFirstPart, "yyyy-MM-dd HH:mm:ss", 
                System.Globalization.CultureInfo.InvariantCulture)).ToUniversalTime();

            return body;
        }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        private SubversionInterface(string clientExe, string url, 
                                    string client)
        {
            Client = client;
            ClientExe = clientExe;
            SourceControl = new Subversion(url);
            localRoot = GetLocalRootDirectory();
            FileInfo fileInfo = new FileInfo(Process.GetCurrentProcess().MainModule.FileName);

            diffFromSvnBatFilename = fileInfo.DirectoryName + "\\difffromsvn.bat";
        }

        /// <summary>
        /// Factory for the Subversion instances.
        /// </summary>
        /// <returns> The source control instance. </returns>
        public static ISourceControl GetInstance(string clientExe, string url, 
                                                 string client)
        {
            return new SubversionInterface(clientExe, url, client);
        }

        private string RunClient(string commandLine, bool eatFirstLine)
        {
            if (SourceControl.ServerType != SourceControlType.SUBVERSION)
                return null;

            ProcessReader procReader = new ProcessReader(LogLevel);
            return procReader.RunExecutable(ClientExe, eatFirstLine, commandLine);
        }


        /// <summary>
        /// Gets the root directory of the repository, derived from the current directory and svn info
        /// </summary>
        /// <returns></returns>
        public string GetLocalRootDirectory()
        {
            Regex urlRegEx = new Regex(@"^(\s)*URL(\s)*:(\s)*(?<url>(\S)*)(\s)*$",
                RegexOptions.ExplicitCapture);

            ProcessReader procReader = new ProcessReader(LogOptions.None);
            string svnInfo = procReader.RunExecutable(ClientExe, false, "info");
            string url = null;

            if (svnInfo != null)
            {
                StringReader sr = new StringReader(svnInfo);
                while (url == null)
                {
                    string l = sr.ReadLine();
                    if (l == null)
                        break;

                    Match urlMatch = urlRegEx.Match(l);
                    if (urlMatch.Success)
                    {
                        url = urlMatch.Groups[1].Value;
                    }
                }
            }

            // the client is unused in Subversion, so generate our from the computer name
            return GetLocalRootDirectory(url);
        }

        /// <summary>
        /// Gets the root directory of the repository, given the SVN URL, SVN Repository Root, and the current directory
        /// </summary>
        /// <returns></returns>
        public static string GetLocalRootDirectory(string url)
        {
            if (url == null)
            {
                // not a subversion directory
                return null;
            }

            string localPath = null;

            // from the current directory, pop up and find the topmost directory with a ".svn" directory
            string directoryInQuestion = Directory.GetCurrentDirectory();
            while (true)
            {
                if (Directory.Exists(directoryInQuestion + "\\.svn"))
                {
                    localPath = directoryInQuestion;

                    DirectoryInfo dirInfo = Directory.GetParent(directoryInQuestion);
                    directoryInQuestion = dirInfo.ToString();
                }
                else
                {
                    break;
                }
            }

            return localPath;
        }


        /// <summary>
        /// Gets the client settings.
        /// </summary>
        /// <returns></returns>
        public static SourceControlSettings GetSettings()
        {
            SourceControlSettings settings = new SourceControlSettings();

            settings.Port = Environment.GetEnvironmentVariable("SVNURL");

            string path = Environment.GetEnvironmentVariable("path").Replace("\"", "");
            string[] pathArray = path.Split(';');
            for (int i = 0; i < pathArray.Length; ++i)
            {
                string svn = Path.Combine(pathArray[i], "svn.exe");
                if (File.Exists(svn))
                {
                    settings.ClientExe = svn;
                    break;
                }
            }

            // get override settings from svn info output
            // Example:
            // D:\projects\Malevich\Malevich>svn info
            // Path: .
            // URL: https://malevich.svn.codeplex.com/svn/Malevich
            // Repository Root: https://malevich.svn.codeplex.com/svn
            // Repository UUID: 8ead0314-7f71-49e1-95c8-3147638646d4
            // Revision: 40756
            // Node Kind: directory
            // Schedule: normal
            // Last Changed Author: unknown
            // Last Changed Rev: 35869
            // Last Changed Date: 2010-01-10 23:37:13 -0800 (Sun, 10 Jan 2010)
            if (settings.ClientExe != null)
            {
                Regex portRegex = new Regex(@"^(\s)*Repository Root(\s)*:(\s)*(?<url>(\S)*)(\s)*$",
                    RegexOptions.ExplicitCapture);
                Regex urlRegEx = new Regex(@"^(\s)*URL(\s)*:(\s)*(?<url>(\S)*)(\s)*$",
                    RegexOptions.ExplicitCapture);

                ProcessReader procReader = new ProcessReader(LogOptions.None);
                string svnInfo = procReader.RunExecutable(settings.ClientExe, false, "info");
                string url = null;

                if (svnInfo != null)
                {
                    StringReader sr = new StringReader(svnInfo);
                    while (settings.Port == null || url == null)
                    {
                        string l = sr.ReadLine();
                        if (l == null)
                            break;

                        if (settings.Port == null)
                        {
                            Match portMatch = portRegex.Match(l);
                            if (portMatch.Success)
                            {
                                settings.Port = portMatch.Groups[1].Value;
                            }
                        }

                        Match urlMatch = urlRegEx.Match(l);
                        if (urlMatch.Success)
                        {
                            url = urlMatch.Groups[1].Value;
                        }
                    }
                }

                // the client is unused in Subversion, so generate our from the computer name
                string localRoot = GetLocalRootDirectory(url);
                if (localRoot != null)
                {
                    settings.Client = Environment.GetEnvironmentVariable("COMPUTERNAME") + "-" + localRoot;
                }
            }

            return settings;
        }
    }

    class ProcessReader
    {
        private StringBuilder outputStringBuilder = new StringBuilder();
        private StringBuilder errorStringBuilder = new StringBuilder();
        private string commandLine = "";
        private string executable = "";
        private LogOptions logLevel;
        private bool eatNextLine = false;

        public ProcessReader(LogOptions logLevel) 
        {
            this.logLevel = logLevel;
        }

        private void OutputHandler(object sendingProcess,
            DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (dataReceivedEventArgs.Data != null)
            {
                if ((logLevel & LogOptions.ClientUtility) == LogOptions.ClientUtility)
                {
                    System.Console.WriteLine(dataReceivedEventArgs.Data);
                }

                // should we eat this line?
                if (eatNextLine)
                {
                    // skip this line, set it so we don't skip future lines
                    eatNextLine = false;
                }
                else
                {
                    outputStringBuilder.AppendLine(dataReceivedEventArgs.Data);
                }
            }
        }

        private void ErrorHandler(object sendingProcess,
            DataReceivedEventArgs dataReceivedEventArgs)
        {
            if (dataReceivedEventArgs.Data != null)
            {
                if ((logLevel & LogOptions.ClientUtility) == LogOptions.ClientUtility)
                {
                    System.Console.Error.WriteLine(dataReceivedEventArgs.Data);
                }
                errorStringBuilder.AppendLine(dataReceivedEventArgs.Data);
            }
        }
        
        /// <summary>
        /// Runs an exe, returns the output in a string. If error happens, prints stderr and returns null.
        /// </summary>
        /// <param name="commandLine"> Command line to execute. </param>
        /// <returns> The output as one string. </returns>
        public string RunExecutable(string exeToRun, bool eatFirstLine, string commandLine)
        {
            this.eatNextLine = eatFirstLine;
            this.executable = exeToRun;
            this.commandLine = commandLine;
            if ((logLevel & LogOptions.ClientUtility) == LogOptions.ClientUtility)
            {
                System.Console.WriteLine("Commandline: " + this.executable + " " + this.commandLine);
            }

            using (Process client = new Process())
            {
                client.StartInfo.UseShellExecute = false;
                client.StartInfo.RedirectStandardError = true;
                client.StartInfo.RedirectStandardOutput = true;
                client.StartInfo.CreateNoWindow = true;
                client.StartInfo.FileName = exeToRun;

                client.StartInfo.Arguments = commandLine;

                client.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
                client.ErrorDataReceived += new DataReceivedEventHandler(ErrorHandler);

                client.Start();

                client.BeginOutputReadLine();
                client.BeginErrorReadLine();

                client.WaitForExit();

                string stderr = errorStringBuilder.ToString();
            }

            return outputStringBuilder.ToString();
        }
    }
}
