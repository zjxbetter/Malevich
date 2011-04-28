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
using System.Diagnostics;
using System.DirectoryServices;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Mail;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.XPath;

using Microsoft.Web.Administration;
using Microsoft.Win32;

using ICSharpCode.SharpZipLib.Zip;

using DataModel;
using Malevich.Util;

namespace Installer
{
    /// <summary>
    /// Interop with Win32. See MSDN for documentation.
    /// </summary>
    class Win32
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct OSVERSIONINFOEX
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
            public short wServicePackMajor;
            public short wServicePackMinor;
            public short wSuiteMask;
            public byte wProductType;
            public byte wReserved;
        }

        public const int VER_NT_WORKSTATION = 1;
        public const int VER_NT_DOMAIN_CONTROLLER = 2;
        public const int VER_NT_SERVER = 3;
        public const int VER_SUITE_SMALLBUSINESS = 1;
        public const int VER_SUITE_ENTERPRISE = 2;
        public const int VER_SUITE_TERMINAL = 16;
        public const int VER_SUITE_DATACENTER = 128;
        public const int VER_SUITE_SINGLEUSERTS = 256;
        public const int VER_SUITE_PERSONAL = 512;
        public const int VER_SUITE_BLADE = 1024;

        [DllImport("kernel32.dll")]
        public static extern bool GetVersionEx(ref OSVERSIONINFOEX osVersionInfo);

        public const int NO_ERROR = 0;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_WRONG_LEVEL = 124;
        public const int ERROR_MORE_DATA = 234;

        public struct SHARE_INFO_2
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string NetName;
            public int ShareType;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Remark;
            public int Permissions;
            public int MaxUsers;
            public int CurrentUsers;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Path;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Password;
        }

        [DllImport("netapi32", CharSet = CharSet.Unicode)]
        public static extern int NetShareEnum(string lpServerName, int dwLevel,
            out IntPtr lpBuffer, int dwPrefMaxLen, out int entriesRead,
            out int totalEntries, ref int hResume);

        [DllImport("netapi32")]
        public static extern int NetApiBufferFree(IntPtr lpBuffer);

    }

    /// <summary>
    /// The state of the product.
    /// </summary>
    enum CurrentState
    {
        NoInstall,
        DatabaseOnly,
        UnsupportedInstall,
        PartialInstall,
        CoherentInstall
    };

    /// <summary>
    /// Exception that gets thrown when the platform does not match prerequisites.
    /// </summary>
    class PlatformException : Exception
    {
        public PlatformException(string exception)
            : base(exception)
        {
        }
    }

    /// <summary>
    /// Exception that gets thrown when installation fails.
    /// </summary>
    class InstallationException : Exception
    {
        public InstallationException(string exception)
            : base(exception)
        {
        }
    }

    /// <summary>
    /// Exception that gets thrown when configuration step has a problem.
    /// </summary>
    class ConfigurationException : Exception
    {
        public ConfigurationException(string exception)
            : base(exception)
        {
        }
    }

    /// <summary>
    /// Detected platform. Contains data from the environment which is later used throughout the installation process.
    /// </summary>
    class DetectedPlatform
    {
        /// <summary>
        /// Operating system type.
        /// </summary>
        public enum OSType
        {
            Server2008,
            Server2008R2,
            Win7,
            Vista
        }

        /// <summary>
        /// SQL Server type.
        /// </summary>
        public enum SqlType
        {
            SqlServer2008
        }

        /// <summary>
        /// Detected OS.
        /// </summary>
        public OSType OS;

        /// <summary>
        /// Detected  version of SQL Server.
        /// </summary>
        public SqlType SQL;

        /// <summary>
        /// List of acceptable SQL Server instances.
        /// </summary>
        public List<string> DatabaseInstances;
    }

    /// <summary>
    /// The parameters of the installation.
    /// They are either discovered, or configured.
    /// </summary>
    class InstallParameters
    {
        /// <summary>
        /// This is our source installation directory.
        /// </summary>
        public string InstallSource;

        /// <summary>
        /// This is our target installation directory.
        /// </summary>
        public string InstallTarget;

        /// <summary>
        /// The state of current installation.
        /// </summary>
        public CurrentState InstallState
        {
            get
            {
                if (Database != null && DatabaseDirectory != null && NotifierDirectory != null &&
                    Website != null && App != null && Root != null && WebsitePath != null &&
                    ClientShare != null && ClientDirectory != null && UnixUtilsDiff != null)
                    return CurrentState.CoherentInstall;

                // Note we don't test for differ intentionally here.
                if (Database != null && DatabaseDirectory != null && NotifierDirectory == null &&
                    Website == null && App == null && Root == null && WebsitePath == null &&
                    ClientDirectory == null && ClientShare == null)
                    return CurrentState.DatabaseOnly;

                if (Database == null && (Website != null || NotifierDirectory != null || ClientDirectory != null))
                    return CurrentState.UnsupportedInstall;

                if (Database != null || DatabaseDirectory != null || NotifierDirectory != null ||
                    Website != null || App != null || Root != null || WebsitePath != null ||
                    ClientDirectory != null || ClientShare != null)
                    return CurrentState.PartialInstall;

                return CurrentState.NoInstall;
            }
        }

        /// <summary>
        /// The database instance name.
        /// </summary>
        public string Database;

        /// <summary>
        /// The directory where database is located.
        /// </summary>
        public string DatabaseDirectory;

        /// <summary>
        /// The directory where the Notifier lives.
        /// </summary>
        public string NotifierDirectory;

        /// <summary>
        /// The instance of IIS that owns the Website, App, and Root below
        /// </summary>
        public ServerManager IIS;

        /// <summary>
        /// The web site where Malevich is installed.
        /// </summary>
        public Site Website;

        /// <summary>
        /// Web application under the web site where Malevich is installed.
        /// </summary>
        public Application App;

        /// <summary>
        /// Virtual root where web site is installed.
        /// </summary>
        public VirtualDirectory Root;

        /// <summary>
        /// The directory where the web site lives.
        /// </summary>
        public string WebsitePath;

        /// <summary>
        /// The name of the web app.
        /// </summary>
        public string WebApplicationName;

        /// <summary>
        /// The name of the share where review client lives.
        /// </summary>
        public string ClientShare;

        /// <summary>
        /// The name of the directory where review client lives.
        /// </summary>
        public string ClientDirectory;

        /// <summary>
        /// Path to unix utilities (the differ).
        /// </summary>
        public string UnixUtilsDiff;

        // The following data is not discoverable. It is collected from the user if necessary.
        /// <summary>
        /// Smtp server to send from, e.g. smtphost.redmond.corp.microsoft.com
        /// </summary>
        public string SmtpServer;

        /// <summary>
        /// Company domain (e.g. microsoft.com)
        /// </summary>
        public string CompanyDomain;

        /// <summary>
        /// Alias to send from.
        /// </summary>
        public string AliasToSendFrom;

        /// <summary>
        /// Whether SSL will be used to send email.
        /// </summary>
        public bool UseSsl;

        /// <summary>
        /// Whether LDAP will be used to send email.
        /// </summary>
        public bool UseLdap;

        /// <summary>
        /// Schedule notifier every this number of minutes.
        /// </summary>
        public int NotifierInterval;

        /// <summary>
        /// Whether the installer should convert time stamps in ChangeList table to UTC.
        /// </summary>
        public bool FixChangeListTimeStamps;

        /// <summary>
        /// What platform we're installing on.
        /// </summary>
        public DetectedPlatform Platform;
    }

    /// <summary>
    /// Configurable parameters in web.config that need to be transferred over to the new one.
    /// </summary>
    class WebConfigParameters
    {
        /// <summary>
        /// Website-wide max line length default.
        /// </summary>
        public string MaxLineLength;

        /// <summary>
        /// Website-wide max line number length default.
        /// </summary>
        public string MaxLineNumberLength;

        /// <summary>
        /// Website-wide max description length.
        /// </summary>
        public string MaxDescriptionLength;

        /// <summary>
        /// Website-wide max review comment length.
        /// </summary>
        public string MaxReviewCommentLength;

        /// <summary>
        /// Supported fonts.
        /// </summary>
        public string Fonts;

        /// <summary>
        /// How many spaces per tab do we display. Negative values = tab is displayed as \t.
        /// </summary>
        public string SpacesPerTab;

        /// <summary>
        /// Whether users are allowed to override the tab setting. An admin can prohibit this as
        /// a style point, if tabs are disallowed.
        /// </summary>
        public string AllowTabOverride;

        /// <summary>
        /// Optional values in web.config - they are simply being transferred as is.
        /// </summary>
        public Dictionary<string, string> OptionalValues = new Dictionary<string, string>();
    }

    /// <summary>
    /// Custom action for the MSI: Installs Malevich after all the files have been copied.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Shows the message in the UI and logs it in the install log file.
        /// </summary>
        /// <param name="message"> A string to log. </param>
        private static void Log(string message)
        {
            Console.WriteLine(message);
        }

        /// <summary>
        /// Checks if file exists in a directory or any of its parent directories. If so, returns that directory.
        /// </summary>
        /// <param name="directory"> Directory where to look. </param>
        /// <param name="file"> File for which to look. </param>
        /// <returns> null if not found, otherwise the file. </returns>
        private static string FindFileInPath(string directory, string file)
        {
            while (!String.IsNullOrEmpty(directory))
            {
                string candidate = Path.Combine(directory, file);
                if (File.Exists(candidate))
                    return candidate;
                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        /// <summary>
        /// Enumerates local instances of SQL server.
        /// </summary>
        /// <returns>List of SQL server instances, relative to localhost. </returns>
        /// <remarks> This code no longer uses SQL Server Management assembly, because it fails to detect
        /// the first instance of SQL Server 2008 installed after SQL Server 2005. This method is expected
        /// to be more robust. </remarks>
        private static ICollection<string> GetLocalSqlServerInstances()
        {
            List<string> servers = new List<string>();

            RegistryKey sqlServerRegKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server");
            if (sqlServerRegKey != null)
            {
                String[] instances = (String[])sqlServerRegKey.GetValue("InstalledInstances");
                if (instances != null)
                {
                    foreach (string instance in instances)
                    {
                        if (instance.Equals("mssqlserver", StringComparison.OrdinalIgnoreCase))
                            servers.Add("localhost");
                        else
                            servers.Add("localhost\\" + instance);
                    }
                }
                sqlServerRegKey.Close();
            }

            return servers;
        }

        /// <summary>
        /// Detects current installation state.
        /// </summary>
        /// <returns> Install parameters. </returns>
        private static InstallParameters DetectInstallState()
        {
            Log("Detecting existing installation state.");
            InstallParameters result = new InstallParameters();

            result.InstallSource = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);

            // Database.
            Log("  Looking for Malevich database.");
            ICollection<string> localSqlServers = GetLocalSqlServerInstances();
            foreach (string sqlServer in localSqlServers)
            {
                Log("    Checking SQL server instance " + sqlServer);

                SqlConnection sqlConnection = new SqlConnection("Data Source=" + sqlServer +
                    ";Initial Catalog=CodeReview;Integrated Security=True");
                try
                {
                    sqlConnection.Open();
                    Log("      Found Malevich in " + sqlServer);
                    using (SqlCommand command = new SqlCommand("sp_helpfile", sqlConnection))
                    {
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if ("CodeReview".Equals((string)reader["name"], StringComparison.OrdinalIgnoreCase))
                                {
                                    result.DatabaseDirectory = Path.GetDirectoryName((string)reader["filename"]);
                                    result.Database = sqlServer;
                                    Log("      Found Malevich database files here: " + result.DatabaseDirectory);
                                    break;
                                }
                            }
                        }
                    }
                    sqlConnection.Close();
                    break;
                }
                catch (SqlException)
                {
                }
            }

            // Web site.
            Log("  Looking for Malevich website.");
            ServerManager iis = new ServerManager();
            foreach (Site site in iis.Sites)
            {
                Log("    Checking " + site.Name);
                foreach (Application app in site.Applications)
                {
                    Log("      Checking application " + app.Path);
                    foreach (VirtualDirectory dir in app.VirtualDirectories)
                    {
                        Log("        Checking directory " + dir.PhysicalPath);
                        if (File.Exists(Path.Combine(dir.PhysicalPath, "ReviewStyle.css")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"comments.js")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"hints.js")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"navigator.js")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"bin\CodeReviewDataModel.dll")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"bin\CommonUtils.dll")))
                        {
                            Log("    Found Malevich on " + site.Name + " (" + app.Path + ") in " + dir.PhysicalPath);
                            result.WebsitePath = dir.PhysicalPath;
                            result.Website = site;
                            result.App = app;
                            result.Root = dir;
                            result.IIS = iis;
                            goto done;
                        }
                    }
                }
            }
        done:

            //  Notifier
            Log("  Looking for the review notifier.");

            Process client = new Process();
            client.StartInfo.UseShellExecute = false;
            client.StartInfo.RedirectStandardError = true;
            client.StartInfo.RedirectStandardOutput = true;
            client.StartInfo.CreateNoWindow = true;
            client.StartInfo.FileName = "schtasks.exe";
            client.StartInfo.Arguments = "/query /v /fo list";

            client.Start();

            string stderr;
            string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
            client.Dispose();

            if (String.IsNullOrEmpty(stderr) && !String.IsNullOrEmpty(stdout))
            {
                using (StringReader reader = new StringReader(stdout))
                {
                    Regex exeMatcher = new Regex("^Task To Run:\\s*\"?(.*)\\\\ReviewNotifier.exe\"?\\s*$",
                        RegexOptions.IgnoreCase);
                    string input;
                    while ((input = reader.ReadLine()) != null)
                    {
                        Match match = exeMatcher.Match(input);
                        if (match.Success)
                        {
                            result.NotifierDirectory = match.Groups[1].Value;
                            Log("    Found notifier in " + result.NotifierDirectory);
                            break;
                        }
                    }
                }
            }
  
            // review.exe
            Log("  Looking for shared client tool.");

            IntPtr buffer;
            int entriesRead, entriesTotal;
            int resume = 0;
            int error = Win32.NetShareEnum(String.Empty, 2, out buffer, -1, out entriesRead,
                out entriesTotal, ref resume);
            if (error == Win32.NO_ERROR && entriesRead > 0)
            {
                int offset = Marshal.SizeOf(typeof(Win32.SHARE_INFO_2));
                int item = buffer.ToInt32();
                for (int i = 0; i < entriesRead; i++, item += offset)
                {
                    IntPtr pItem = new IntPtr(item);
                    Win32.SHARE_INFO_2 si = (Win32.SHARE_INFO_2)Marshal.PtrToStructure(pItem,
                        typeof(Win32.SHARE_INFO_2));

                    Log("    Checking " + si.NetName);
                    try
                    {
                        if ((!String.IsNullOrEmpty(si.Path)) && File.Exists(Path.Combine(si.Path, "review.exe")))
                        {
                            result.ClientDirectory = si.Path;
                            result.ClientShare = si.NetName;
                            Log("    Found client share " + result.ClientShare + " in " + result.ClientDirectory);
                            break;
                        }
                    }
                    catch (UnauthorizedAccessException uae)
                    {
                        Log("      Could not access the share: access denied.");
                        Log(uae.ToString());
                    }
                    catch (IOException ioe)
                    {
                        Log("      Could not access the share: I/O error.");
                        Log(ioe.ToString());
                    }
                    catch (ArgumentException ae)
                    {
                        Log("      Could not access the share: not a valid path.");
                        Log(ae.ToString());
                    }
                }
            }
            if (IntPtr.Zero != buffer)
                Win32.NetApiBufferFree(buffer);

            // Unix Utils
            Log("  Looking for the differ tool.");
            if (result.WebsitePath != null)
            {
                Log("    Searching web.config.");
                try
                {
                    using (StreamReader reader = new StreamReader(Path.Combine(result.WebsitePath, "web.config")))
                    {
                        Regex diffKeyRegex = new Regex("key\\s*=\\s*\"diffExe\"", RegexOptions.IgnoreCase);
                        Regex valueRegex = new Regex("value\\s*=\\s*\"(.*?)\"", RegexOptions.IgnoreCase);

                        string input;
                        while ((input = reader.ReadLine()) != null)
                        {
                            if (diffKeyRegex.IsMatch(input))
                            {
                                Match valueMatch = valueRegex.Match(input);
                                if (valueMatch.Success)
                                {
                                    result.UnixUtilsDiff = valueMatch.Groups[1].Value;
                                    Log("    Found differ in " + result.UnixUtilsDiff);
                                    break;
                                }
                                else
                                {
                                    Log("    Note: could not parse " + input);
                                }
                            }
                        }
                    }
                }
                catch(IOException ex)
                {
                    Log("    Of note: Failed to read " + Path.Combine(result.WebsitePath, "web.config"));
                    Log(ex.ToString());
                }
            }

            if (result.UnixUtilsDiff == null)
            {
                Log("    Searching the file system.");
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo drive in drives)
                {
                    Log("      Searching " + drive.RootDirectory.FullName);
                    string candidate = Path.Combine(drive.RootDirectory.FullName, @"usr\local\wbin\diff.exe");
                    try
                    {
                        if (File.Exists(candidate))
                        {
                            result.UnixUtilsDiff = candidate;
                            Log("    Found differ: " + result.UnixUtilsDiff);
                            break;
                        }
                        string[] rootDirs = Directory.GetDirectories(drive.RootDirectory.FullName);
                        foreach (string dir in rootDirs)
                        {
                            candidate = Path.Combine(dir, @"usr\local\wbin\diff.exe");
                            if (File.Exists(candidate))
                            {
                                result.UnixUtilsDiff = candidate;
                                Log("    Found differ: " + result.UnixUtilsDiff);
                                break;
                            }
                        }
                        if (result.UnixUtilsDiff != null)
                            break;
                    }
                    catch (UnauthorizedAccessException uae)
                    {
                        Log("      Could not access the drive: access denied.");
                        Log(uae.ToString());
                    }
                    catch (IOException ioe)
                    {
                        Log("      Could not access the drive: I/O error.");
                        Log(ioe.ToString());
                    }                    
                }

                if (result.UnixUtilsDiff == null)
                {
                    string baseFile = null;
                    if (result.WebsitePath != null)
                        baseFile = FindFileInPath(result.WebsitePath, @"unxutils\usr\local\wbin\diff.exe");
                    if (baseFile == null && result.ClientDirectory != null)
                        baseFile = FindFileInPath(result.ClientDirectory, @"unxutils\usr\local\wbin\diff.exe");
                    if (baseFile == null && result.NotifierDirectory != null)
                        baseFile = FindFileInPath(result.NotifierDirectory, @"unxutils\usr\local\wbin\diff.exe");
                    if (baseFile == null && result.DatabaseDirectory != null)
                        baseFile = FindFileInPath(result.DatabaseDirectory, @"unxutils\usr\local\wbin\diff.exe");
                    if (baseFile != null)
                    {
                        result.UnixUtilsDiff = baseFile;
                        Log("    Found differ: " + result.UnixUtilsDiff);
                    }
                }
            }

            // This tried to figure out if there is a common root.
            string[] dirs = new string[4];
            int dirNumber = 0;
            if (result.NotifierDirectory != null)
            {
                string s = Path.GetDirectoryName(result.NotifierDirectory);
                if (s != null)
                    dirs[dirNumber++] = s.ToLower();
            }
            if (result.DatabaseDirectory != null)
            {
                dirs[dirNumber++] = result.DatabaseDirectory.ToLower();
            }
            if (result.ClientDirectory != null)
            {
                string s = Path.GetDirectoryName(result.ClientDirectory);
                if (s != null)
                    dirs[dirNumber++] = s.ToLower();
            }
            if (result.WebsitePath != null)
            {
                string s = Path.GetDirectoryName(result.WebsitePath);
                if (s != null)
                    dirs[dirNumber++] = s.ToLower();
            }

            if (dirNumber == 1)
            {
                if (Directory.Exists(Path.Combine(dirs[0], "backup")) &&
                    Directory.Exists(Path.Combine(dirs[0], "unxutils")))
                {
                    result.InstallTarget = dirs[0];
                }
            }
            else
            {
                Array.Sort(dirs, delegate(string s1, string s2)
                {
                    if (s1 == null && s2 == null)
                        return 0;

                    if (s1 == null)
                        return 1;

                    if (s2 == null)
                        return -1;

                    return s1.CompareTo(s2);
                });
                string candidate = dirs[0];
                int curNum = 1;
                int maxNum = 1;
                for (int i = 1; i < dirNumber; ++i)
                {
                    if (dirs[i - 1].Equals(dirs[i]))
                    {
                        ++curNum;
                        if (curNum > maxNum)
                        {
                            candidate = dirs[i];
                            maxNum = curNum;
                        }
                    }
                    else
                    {
                        curNum = 1;
                    }
                }

                if (maxNum > 1)
                    result.InstallTarget = candidate;
            }

            return result;
        }

        /// <summary>
        /// Gets the list of acceptable database instances.
        /// </summary>
        /// <returns> List of instances that can be used for Malevich installation. </returns>
        private static List<string> GetDatabaseInstances()
        {
            List<string> databaseInstances = new List<string>();
            ICollection<string> localServerInstances = GetLocalSqlServerInstances();
            foreach (string sqlServer in localServerInstances)
            {
                Log("        --- " + sqlServer);

                SqlConnection sqlConnection = new SqlConnection("Data Source=" + sqlServer +
                    ";Integrated Security=True");
                try
                {
                    sqlConnection.Open();
                    try
                    {
                        using (SqlCommand command = new SqlCommand("SELECT @@VERSION", sqlConnection))
                        {
                            string versionString = command.ExecuteScalar() as string;
                            if (versionString != null && versionString.StartsWith("Microsoft SQL Server 2008"))
                            {
                                bool added = false;

                                string[] names = sqlServer.Split('\\');
                                string localConnection = (names.Length < 2) ? "127.0.0.1" : ("127.0.0.1\\" + names[1]);
                                SqlConnection tcpConnection = new SqlConnection("Data Source=" + localConnection +
                                    ";Integrated Security=True");
                                try
                                {
                                    tcpConnection.Open();
                                    try
                                    {
                                        using (SqlCommand command2 = new SqlCommand("SELECT @@VERSION", tcpConnection))
                                        {
                                            versionString = command2.ExecuteScalar() as string;
                                            if (versionString != null && versionString.StartsWith(
                                                "Microsoft SQL Server 2008"))
                                            {
                                                databaseInstances.Add(sqlServer);
                                                added = true;
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        tcpConnection.Close();
                                    }
                                }
                                catch (SqlException)
                                {
                                }

                                if (!added)
                                {
                                    Log("Note: detected server instance " + sqlServer + " which appears");
                                    Log("to be an instance of SQL Server 2008, but has no enabled TCP");
                                    Log("interfaces. If you want to use this instance of SQL Server, please");
                                    Log("enable TCP/IP protocol in SQL Server Configuration Manager and restart");
                                    Log("Malevich configuration.");
                                }
                            }
                        }
                    }
                    finally
                    {
                        sqlConnection.Close();
                    }
                }
                catch (SqlException)
                {
                }
            }

            return databaseInstances;
        }

        /// <summary>
        /// Verifies that the default web site exists. This is the first load of web administration assembly,
        /// it is in a separate function to prevent crash if IIS is not installed.
        /// </summary>
        private static void VerifyDefaultWebSite()
        {
            ServerManager iis = new ServerManager();
            Site defaultWebSite = iis.Sites["Default Web Site"];
            if (defaultWebSite == null)
                throw new PlatformException("Default web site is not present!");

            if (defaultWebSite.State != ObjectState.Started)
                throw new PlatformException("Default web site is not started!");
        }

        /// <summary>
        /// Verify that it is Server 2008, it has SQL 2008, etc.
        /// </summary>
        private static DetectedPlatform VerifyPlatform()
        {
            DetectedPlatform platform = new DetectedPlatform();

            Log("Verifying the OS and dependencies.");
            OperatingSystem os = Environment.OSVersion;
            if (os.Platform != PlatformID.Win32NT)
                throw new PlatformException("Wrong platform: Malevich only runs on Win32.");
            Log("  Win32NT: Check!");

            Win32.OSVERSIONINFOEX versionEx = new Win32.OSVERSIONINFOEX();
            versionEx.dwOSVersionInfoSize = Marshal.SizeOf(versionEx);
            if (!Win32.GetVersionEx(ref versionEx))
                throw new PlatformException("Wrong platform: could not get version.");
            Log("  Got OS version: Check!");

            if (((os.Version.Major == 6) && (os.Version.Minor == 0) &&
                ((versionEx.wProductType == Win32.VER_NT_SERVER) ||
                (versionEx.wProductType == Win32.VER_NT_DOMAIN_CONTROLLER))))
            {
                Log("  Server 2008: Check!");
                platform.OS = DetectedPlatform.OSType.Server2008;
            }
            else if (((os.Version.Major == 6) && (os.Version.Minor == 1) &&
                ((versionEx.wProductType == Win32.VER_NT_SERVER) ||
                (versionEx.wProductType == Win32.VER_NT_DOMAIN_CONTROLLER))))
            {
                Log("  Server 2008 R2: Check!");
                platform.OS = DetectedPlatform.OSType.Server2008R2;
            }
            else if (((os.Version.Major == 6) && (os.Version.Minor == 1) &&
                ((versionEx.wProductType == Win32.VER_NT_WORKSTATION))))
            {
                Log("  Windows 7: Check!");
                platform.OS = DetectedPlatform.OSType.Win7;
            }
            else if (((os.Version.Major == 6) && (os.Version.Minor == 0) &&
                ((versionEx.wProductType == Win32.VER_NT_WORKSTATION))))
            {
                Log("  Windows Vista: Check!");
                platform.OS = DetectedPlatform.OSType.Vista;
            }
            else
            {
                Log(String.Format("Unsupported operating system ({0} - {1}:{2})", versionEx.wProductType,
                    os.Version.Major, os.Version.Minor));
                throw new PlatformException("Wrong OS: must be Windows Server 2008 or Windows 7.");
            }

            // Alright, we're running a supported OS at this point. Good! Next, discover SQL Server 2008.
            Log("  Discovering SQL Server instances.");
            Log("    (This may take a while, please be patient...)");
            platform.DatabaseInstances = GetDatabaseInstances();
            if (platform.DatabaseInstances.Count == 0)
            {
                Log("Installation program could not detect an acceptable running instance of");
                Log("SQL Server 2008. If you do have SQL Server 2008 installed on this computer,");
                Log("use SQL Server Configuration Manager to verify that SQL Browser service");
                Log("is enabled and started.");
                Log("You should make sure it is configured to start automatically.");

                throw new PlatformException("No SQL Server 2008 instances detected!");
            }
            Log("  SQL Server has local database instances: Check!");

            platform.SQL = DetectedPlatform.SqlType.SqlServer2008;

            Log("  Verifying IIS configuration...");

            RegistryKey iisConfig = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp\Components", false);
            if (iisConfig == null)
                throw new PlatformException("IIS is not installed.");
            try
            {
                if ((int)iisConfig.GetValue("StaticContent", 0) != 1)
                    throw new PlatformException("Static content support is not installed in IIS.");
                if ((int)iisConfig.GetValue("ASPNET", 0) != 1)
                    throw new PlatformException("ASP.NET option is not installed in IIS.");
                if ((int)iisConfig.GetValue("HttpProtocol", 0) != 1)
                    throw new PlatformException("Http binding is not installed in IIS.");
                if ((int)iisConfig.GetValue("WindowsAuthentication", 0) != 1)
                    throw new PlatformException("Windows authentication option is not installed in IIS.");
            }
            finally
            {
                iisConfig.Close();
            }

            VerifyDefaultWebSite();

            Log("  IIS fully installed with all the required options: Check!");

            return platform;
        }

        /// <summary>
        /// Backs up the existing database.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void BackupDatabase(InstallParameters installParams)
        {
            string dir = Path.Combine(installParams.InstallTarget, "backup");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            DateTime now = DateTime.Now;
            string fileName = Path.Combine(dir,
                String.Format("malevich_backup_{0}_{1}_{2}_{3:00}{4:00}{5:00}_{6:000}.bak",
                now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond));

            Log("  Backing up database to " + fileName);

            try
            {
                SqlConnection sqlConnection = new SqlConnection("Data Source=" + installParams.Database +
                    ";Initial Catalog=CodeReview;Integrated Security=True");
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand(@"BACKUP DATABASE [CodeReview] TO DISK = @File " +
                    "WITH NOFORMAT, NOINIT, NAME = N'Malevich Backup (Full)', SKIP, NOREWIND, NOUNLOAD, STATS = 10",
                    sqlConnection))
                {
                    command.CommandTimeout = 20 * 60; // 20 minutes should be enough.
                    command.Parameters.Add(new SqlParameter("@File", fileName));
                    command.ExecuteNonQuery();
                }
                sqlConnection.Close();
            }
            catch (SqlException sex)
            {
                Log(sex.ToString());
                throw new InstallationException("Could not backup database.");
            }
        }

        /// <summary>
        /// Create the database.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void CreateDatabase(InstallParameters installParams)
        {
            Log("  Creating the database.");
            try
            {
                SqlConnection sqlConnection = new SqlConnection("Data Source=" + installParams.Database +
                    ";Integrated Security=True");
                sqlConnection.Open();
                string[] sqlCommands = {
                    "CREATE DATABASE [CodeReview] ON PRIMARY " +
                    "(NAME = N'CodeReview', FILENAME = '" + Path.Combine(installParams.InstallTarget, "CodeReview.mdf")
                    + "', SIZE = 393216KB, FILEGROWTH = 131072KB) LOG ON (NAME = N'CodeReview_log', FILENAME = '" +
                    Path.Combine(installParams.InstallTarget, "CodeReview_log.ldf") +
                    "', SIZE = 131072KB, FILEGROWTH = 16384KB)",
                    "ALTER DATABASE [CodeReview] SET COMPATIBILITY_LEVEL = 100",
                    "ALTER DATABASE [CodeReview] SET ANSI_NULL_DEFAULT OFF",
                    "ALTER DATABASE [CodeReview] SET ANSI_NULLS OFF",
                    "ALTER DATABASE [CodeReview] SET ANSI_PADDING OFF",
                    "ALTER DATABASE [CodeReview] SET ANSI_WARNINGS OFF",
                    "ALTER DATABASE [CodeReview] SET ARITHABORT OFF",
                    "ALTER DATABASE [CodeReview] SET AUTO_CLOSE OFF",
                    "ALTER DATABASE [CodeReview] SET AUTO_CREATE_STATISTICS ON",
                    "ALTER DATABASE [CodeReview] SET AUTO_SHRINK OFF",
                    "ALTER DATABASE [CodeReview] SET AUTO_UPDATE_STATISTICS ON",
                    "ALTER DATABASE [CodeReview] SET CURSOR_CLOSE_ON_COMMIT OFF",
                    "ALTER DATABASE [CodeReview] SET CURSOR_DEFAULT GLOBAL",
                    "ALTER DATABASE [CodeReview] SET CONCAT_NULL_YIELDS_NULL OFF",
                    "ALTER DATABASE [CodeReview] SET NUMERIC_ROUNDABORT OFF",
                    "ALTER DATABASE [CodeReview] SET QUOTED_IDENTIFIER OFF",
                    "ALTER DATABASE [CodeReview] SET RECURSIVE_TRIGGERS OFF",
                    "ALTER DATABASE [CodeReview] SET DISABLE_BROKER",
                    "ALTER DATABASE [CodeReview] SET AUTO_UPDATE_STATISTICS_ASYNC OFF",
                    "ALTER DATABASE [CodeReview] SET DATE_CORRELATION_OPTIMIZATION OFF",
                    "ALTER DATABASE [CodeReview] SET PARAMETERIZATION SIMPLE",
                    "ALTER DATABASE [CodeReview] SET READ_WRITE",
                    "ALTER DATABASE [CodeReview] SET RECOVERY FULL",
                    "ALTER DATABASE [CodeReview] SET MULTI_USER",
                    "ALTER DATABASE [CodeReview] SET PAGE_VERIFY CHECKSUM",
                    "USE [CodeReview];" +
                    "IF NOT EXISTS (SELECT name FROM sys.filegroups WHERE is_default=1 AND name = N'PRIMARY') " +
                    "ALTER DATABASE [CodeReview] MODIFY FILEGROUP [PRIMARY] DEFAULT" };

                foreach (string commandString in sqlCommands)
                {
                    Log("Running " + commandString);
                    SqlCommand command = new SqlCommand(commandString, sqlConnection);
                    command.CommandTimeout = 120;
                    command.ExecuteNonQuery();
                }

                sqlConnection.Close();
                sqlConnection = new SqlConnection("Data Source=" + installParams.Database +
                    ";Initial Catalog=CodeReview;Integrated Security=True");
                sqlConnection.Open();
                sqlConnection.Close();
            }
            catch (SqlException sex)
            {
                Log(sex.ToString());
                throw new InstallationException("Could not create the database.");
            }
        }

        /// <summary>
        /// Runs the script to convert the CL timestamps to UTC.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void FixChangeListTimeStamps(InstallParameters installParams)
        {
            Log("Converting changelist timestamps to UTC");
            string fixupcommands = null;

            string fixupscript = Path.Combine(installParams.InstallSource, @"FixChangeListTimeStamps.sql");

            using (StreamReader pds = new StreamReader(fixupscript))
                fixupcommands = pds.ReadToEnd();

            if (String.IsNullOrEmpty(fixupcommands))
                throw new InstallationException("Could not get fixup script.");

            try
            {
                SqlConnection sqlConnection = new SqlConnection("Data Source=" + installParams.Database +
                    ";Initial Catalog=CodeReview;Integrated Security=True");
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand(fixupcommands, sqlConnection))
                    command.ExecuteNonQuery();
                sqlConnection.Close();
            }
            catch (SqlException sex)
            {
                Log(sex.ToString());
                throw new InstallationException("Could not convert change list timestamps to UTC.");
            }

        }
        /// <summary>
        /// Installs or upgrades the database.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void InstallDatabase(InstallParameters installParams)
        {
            Log("Installing the database.");
            int changeListCount = 0;
            if (installParams.DatabaseDirectory == null)
            {
                CreateDatabase(installParams);
            }
            else
            {
                BackupDatabase(installParams);
                if (installParams.FixChangeListTimeStamps)
                    FixChangeListTimeStamps(installParams);

                SqlConnection sqlConnection = new SqlConnection("Data Source=" + installParams.Database +
                    ";Initial Catalog=CodeReview;Integrated Security=True");
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand("SELECT COUNT(Id) FROM ChangeList", sqlConnection))
                    changeListCount = (int)command.ExecuteScalar();
                sqlConnection.Close();
            }

            Log("  Deploying the metadata:");

            // Bug in vsdbcmd: if HKEY_CURRENT_USER\Software\Microsoft\VisualStudio\9.0 does not exist, the tool
            // does not work.
            Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\VisualStudio\9.0").Close();

            string deployer = Path.Combine(installParams.InstallSource, @"redistr\deploy\vsdbcmd.exe");
            string metadata = Path.Combine(installParams.InstallSource, @"$(TargetFileName)");
            string postscript = Path.Combine(installParams.InstallSource, @"Script.PostDeployment.sql");

            Process client = new Process();
            client.StartInfo.UseShellExecute = false;
            client.StartInfo.RedirectStandardError = true;
            client.StartInfo.RedirectStandardOutput = true;
            client.StartInfo.CreateNoWindow = true;
            client.StartInfo.FileName = deployer;
            client.StartInfo.Arguments = "/a:Deploy /cs:\"Data Source=" + installParams.Database +
                ";Integrated Security=true\" /model:\"" + metadata + "\" /dd:+ /p:TargetDatabase=CodeReview";

            client.Start();

            string stderr;
            string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
            client.Dispose();

            if (!String.IsNullOrEmpty(stderr))
            {
                Log("Deployment has returned an error status:");
                Log(stderr);
                Log("The rest of the output:");
                Log(stdout);
                throw new InstallationException("Failed to deploy the metadata.");
            }

            Log(stdout);

            Log("  Running the post-deployment script.");
            string postdeploycommands = null;
            using (StreamReader pds = new StreamReader(postscript))
                postdeploycommands = pds.ReadToEnd();

            if (String.IsNullOrEmpty(postdeploycommands))
                throw new InstallationException("Could not get postdeployment script.");

            try
            {
                SqlConnection sqlConnection = new SqlConnection("Data Source=" + installParams.Database +
                    ";Initial Catalog=CodeReview;Integrated Security=True");
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand(postdeploycommands, sqlConnection))
                    command.ExecuteNonQuery();
                sqlConnection.Close();
            }
            catch (SqlException sex)
            {
                Log(sex.ToString());
                throw new InstallationException("Could not deploy the database.");
            }

            Log("  Verifying the database.");

            try
            {
                using (CodeReviewDataContext context = new CodeReviewDataContext("Data Source=" +
                    installParams.Database + ";Initial Catalog=CodeReview;Integrated Security=True"))
                {
                    int? cid = null;
                    context.AddChangeList(1, "Test client", "Test change list", "Test description",
                        DateTime.Now, ref cid);
                    ChangeList[] cl = (from cc in context.ChangeLists where cc.Id == cid.Value select cc).ToArray();
                    if (cl.Length != 1)
                        throw new InstallationException(
                            "Failed to create or query a change list in the verification step.");
                    context.ChangeLists.DeleteOnSubmit(cl[0]);
                    context.SubmitChanges();

                    if ((from cc in context.ChangeLists select cc).Count() != changeListCount)
                        throw new InstallationException(
                            "Verification failed - upgrade changed number of change lists in the database.");
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                throw new InstallationException("Failed to verify the database.");
            }
        }

        /// <summary>
        /// Recursively copies files from one directory to another.
        /// </summary>
        /// <param name="dirFrom"> Source directory. </param>
        /// <param name="dirTo"> Target directory. </param>
        private static void CopyFiles(string dirFrom, string dirTo)
        {
            Log("      " + dirTo);
            if (!Directory.Exists(dirTo))
                Directory.CreateDirectory(dirTo);

            string[] dirs = Directory.GetDirectories(dirFrom);
            foreach (string d in dirs)
                CopyFiles(d, Path.Combine(dirTo, Path.GetFileName(d)));

            string[] files = Directory.GetFiles(dirFrom);
            foreach (string f in files)
                File.Copy(f, Path.Combine(dirTo, Path.GetFileName(f)));
        }

        /// <summary>
        /// Sets things in the new web.config file.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void FixupWebConfig(InstallParameters installParams, WebConfigParameters wcp)
        {
            Log("  Fixing up web.config.");

            string file = Path.Combine(installParams.WebsitePath != null ? installParams.WebsitePath :
                Path.Combine(installParams.InstallTarget, "web"), "web.config");

            XmlDocument webConfig = new XmlDocument();
            webConfig.Load(file);
            XPathNavigator cursor = webConfig.CreateNavigator();
            XPathNodeIterator connections = cursor.Select("/configuration/connectionStrings/add");
            foreach (XPathNavigator connection in connections)
            {
                if ("DataConnectionString".Equals(connection.GetAttribute("name", ""),
                    StringComparison.OrdinalIgnoreCase))
                {
                    connection.MoveToAttribute("connectionString", "");
                    connection.SetValue("Data Source=" + installParams.Database +
                        ";Initial Catalog=CodeReview;Integrated Security=True");
                }
            }

            XPathNodeIterator appSettings = cursor.Select("/configuration/appSettings/add");
            foreach (XPathNavigator appSetting in appSettings)
            {
                string name = appSetting.GetAttribute("key", "");
                if ("diffExe".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    appSetting.MoveToAttribute("value", "");
                    appSetting.SetValue(installParams.UnixUtilsDiff != null ? installParams.UnixUtilsDiff :
                        Path.Combine(installParams.InstallTarget, @"unxutils\usr\local\wbin\diff.exe"));
                    continue;
                }

                if (wcp == null)
                    continue;

                string targetValue = null;
                if ("maxLineLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.MaxLineLength;
                else if ("maxLineNumberLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.MaxLineNumberLength;
                else if ("maxDescriptionLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.MaxDescriptionLength;
                else if ("maxReviewCommentLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.MaxReviewCommentLength;
                else if ("fonts".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.Fonts;
                else if ("spacesPerTab".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.SpacesPerTab;
                else if ("allowTabOverride".Equals(name, StringComparison.OrdinalIgnoreCase))
                    targetValue = wcp.AllowTabOverride;

                if (targetValue == null)
                {
                    // This particular setting was not found; it could be in
                    // the dictionary of optional parameters.
                    if (wcp.OptionalValues.Keys.Contains(name))
                    {
                        targetValue = wcp.OptionalValues[name];
                        wcp.OptionalValues.Remove(name);
                    }
                    else
                    {
                        continue;
                    }
                }

                string currentValue = appSetting.GetAttribute("value", "");
                if (targetValue.Equals(currentValue))
                    continue;

                appSetting.MoveToAttribute("value", "");
                appSetting.SetValue(targetValue);
            }

            if (wcp != null)
            {
                XPathNavigator appSettingsKey = cursor.SelectSingleNode("/configuration/appSettings");

                foreach (string key in wcp.OptionalValues.Keys)
                    appSettingsKey.AppendChild("<add key=\"" + key + "\" value=\"" + wcp.OptionalValues[key] + "\" />");
            }

            webConfig.Save(file);
        }

        /// <summary>
        /// Reads user-configurable parameters out of web.config.
        /// </summary>
        /// <param name="file"> Path to web.config. </param>
        /// <returns> Parameter set that needs to be preserved. </returns>
        private static WebConfigParameters GetWebConfig(string file)
        {
            Log("  Saving original web.config parameters.");
            WebConfigParameters wcp = new WebConfigParameters();
            XmlDocument webConfig = new XmlDocument();
            webConfig.Load(file);
            XPathNavigator cursor = webConfig.CreateNavigator();
            XPathNodeIterator appSettings = cursor.Select("/configuration/appSettings/add");
            foreach (XPathNavigator appSetting in appSettings)
            {
                string name = appSetting.GetAttribute("key", "");
                if ("maxLineLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.MaxLineLength = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("maxLineNumberLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.MaxLineNumberLength = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("maxDescriptionLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.MaxDescriptionLength = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("maxReviewCommentLength".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.MaxReviewCommentLength = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("fonts".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.Fonts = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("spacesPerTab".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.SpacesPerTab = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("allowTabOverride".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    wcp.AllowTabOverride = appSetting.GetAttribute("value", "");
                    continue;
                }
                if ("diffExe".Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    // diffExe is handled elsewhere
                    continue;
                }

                wcp.OptionalValues[name] = appSetting.GetAttribute("value", "");
            }
            return wcp;
        }

        /// <summary>
        /// Unpacks Unix Utils.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void UnpackDiffer(InstallParameters installParams)
        {
            Log("Installing Unix Utilities (UnxUtils).");

            FastZip zipper = new FastZip();
            zipper.ExtractZip(Path.Combine(installParams.InstallSource, @"redistr\UnxUtils.zip"),
                Path.Combine(installParams.InstallTarget, "unxutils"), FastZip.Overwrite.Always,
                null, null, null, false);
        }

        /// <summary>
        /// Sets the source control to point to the selected web site.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void ConfigureSourceControlInDatabase(InstallParameters installParams)
        {
            try
            {
                using (CodeReviewDataContext context = new CodeReviewDataContext("Data Source=" +
                    installParams.Database + ";Initial Catalog=CodeReview;Integrated Security=True"))
                {
                    SourceControl[] sourceControls = (from sc in context.SourceControls select sc).ToArray();
                    if (sourceControls.Length == 1)
                    {
                        sourceControls[0].WebsiteName = "/" + (installParams.WebApplicationName == null ? "Malevich" :
                            installParams.WebApplicationName);
                        context.SubmitChanges();
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                throw new InstallationException("Failed to adjust source control.");
            }
        }

        /// <summary>
        /// Creates the web site.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void CreateWebSite(InstallParameters installParams)
        {
            Log("Installing the web site.");

            Log("  Copying files.");
            string targetPath = Path.Combine(installParams.InstallTarget, "Web");
            CopyFiles(Path.Combine(installParams.InstallSource, "web"), targetPath);
            FixupWebConfig(installParams, null);

            Log("  Configuring IIS.");
            if (installParams.IIS == null)
                installParams.IIS = new ServerManager();

            string appName = installParams.WebApplicationName == null ? "/Malevich" : "/" +
                installParams.WebApplicationName;

            Log("  Creating web application " + appName);
            Site defaultWebSite = installParams.IIS.Sites["Default Web Site"];
            defaultWebSite.Applications.Add(appName, targetPath);
            installParams.IIS.CommitChanges();

            Log("  Configuring security.");
            Microsoft.Web.Administration.Configuration config = installParams.IIS.GetApplicationHostConfiguration();

            ConfigurationSection anonymousAuthenticationSection = config.GetSection(
                "system.webServer/security/authentication/anonymousAuthentication", "");
            anonymousAuthenticationSection.OverrideMode = OverrideMode.Allow;
            installParams.IIS.CommitChanges();

            config = installParams.IIS.GetApplicationHostConfiguration();
            anonymousAuthenticationSection = config.GetSection(
                "system.webServer/security/authentication/anonymousAuthentication", "Default Web Site");
            anonymousAuthenticationSection.OverrideMode = OverrideMode.Allow;
            installParams.IIS.CommitChanges();

            config = installParams.IIS.GetApplicationHostConfiguration();
            anonymousAuthenticationSection = config.GetSection(
                "system.webServer/security/authentication/anonymousAuthentication", "Default Web Site" + appName);
            anonymousAuthenticationSection["enabled"] = false;

            ConfigurationSection windowsAuthenticationSection = config.GetSection(
                "system.webServer/security/authentication/windowsAuthentication", "Default Web Site" + appName);
            windowsAuthenticationSection["enabled"] = true;

            installParams.IIS.CommitChanges();

            ConfigureSourceControlInDatabase(installParams);
        }

        /// <summary>
        /// Updates the web site.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void UpdateWebSite(InstallParameters installParams)
        {
            Log("Updating the web site.");

            Log("  Backing up web.config.");
            string dir = Path.Combine(installParams.InstallTarget, "backup");
            if (!Directory.Exists(dir))
            {
                Log("  Creating directory.");
                Directory.CreateDirectory(dir);
            }

            DateTime now = DateTime.Now;
            string fileName = Path.Combine(dir,
                String.Format("web_config_{0}_{1}_{2}_{3:00}{4:00}{5:00}_{6:000}.txt",
                now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Millisecond));

            string sourceWebConfig = Path.Combine(installParams.WebsitePath, "web.config");
            File.Copy(sourceWebConfig, fileName);
            WebConfigParameters wcp = GetWebConfig(sourceWebConfig);
            Log("  Deleting old files.");
            string[] files = Directory.GetFiles(installParams.WebsitePath);
            foreach (string file in files)
                File.Delete(file);
            string[] dirs = Directory.GetDirectories(installParams.WebsitePath);
            foreach (string d in dirs)
                Directory.Delete(d, true);
            // Let IIS realize that the files are gone.
            Thread.Sleep(5000);
            Log("  Copying new files.");
            CopyFiles(Path.Combine(installParams.InstallSource, "web"), installParams.WebsitePath);
            FixupWebConfig(installParams, wcp);
        }

        /// <summary>
        /// Delete web site.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void RemoveWebSite(InstallParameters installParams)
        {
            Log("Uninstalling the web site.");

            Log("  Configuring IIS.");
            installParams.Website.Applications.Remove(installParams.App);
            installParams.IIS.CommitChanges();

            Log("  Deleting files.");
            try
            {
                Directory.Delete(installParams.WebsitePath, true);
            }
            catch (UnauthorizedAccessException uae)
            {
                Log("  Could not delete " + installParams.WebsitePath + ": Access Denied.");
                Log(uae.ToString());
            }
            catch (IOException ioe)
            {
                Log("  Could not delete " + installParams.WebsitePath + ": IO exception.");
                Log(ioe.ToString());
            }
        }

        /// <summary>
        /// Shares out the client.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void CopyAndShareClientBits(InstallParameters installParams)
        {
            Log("Installing review client.");

            string targetDir = installParams.ClientDirectory == null ?
                Path.Combine(installParams.InstallTarget, "client") : installParams.ClientDirectory;

            if (!Directory.Exists(targetDir))
            {
                Log("  Creating directory.");
                Directory.CreateDirectory(targetDir);
            }

            Log("  Copying files.");
            string[] files = { "review.exe", "CodeReviewDataModel.dll", "SourceControl.dll", "CommonUtils.dll" };
            foreach (string file in files)
                File.Copy(Path.Combine(installParams.InstallSource, file), Path.Combine(targetDir, file), true);

            if (installParams.ClientShare == null)
            {
                Log("  Sharing review client out.");

                ManagementClass managementClass = new ManagementClass("Win32_Share");
                ManagementBaseObject inParams = managementClass.GetMethodParameters("Create");
                inParams["Description"] = "Malevich client app";
                inParams["Name"] = "reviewclient";
                inParams["Path"] = targetDir;
                inParams["Type"] = 0x0; // Disk Drive
                ManagementBaseObject outParams = managementClass.InvokeMethod("Create", inParams, null);
                if ((uint)(outParams.Properties["ReturnValue"].Value) != 0)
                    throw new InstallationException("Unable to share directory " + targetDir);
            }
        }

        /// <summary>
        /// Remove client bits.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void RemoveClientBits(InstallParameters installParams)
        {
            Log("Uninstalling review client.");

            Log("  Removing files.");
            string[] files = { "review.exe", "CodeReviewDataModel.dll", "SourceControl.dll", "CommonUtils.dll" };
            foreach (string file in files)
            {
                string target = Path.Combine(installParams.ClientDirectory, file);
                if (File.Exists(target))
                    File.Delete(target);
            }

            Log("  Removing the share.");
            using (ManagementObject o = new ManagementObject("root\\cimv2",
                "Win32_Share.Name='" + installParams.ClientShare + "'", null))
            {
                ManagementBaseObject outParams = o.InvokeMethod("delete", null, null);
                if ((uint)(outParams.Properties["ReturnValue"].Value) != 0)
                    Log("Failed to remove share " + installParams.ClientShare);
            }

            if (IsDirectoryEmpty(installParams.ClientDirectory))
            {
                Log("  Removing the empty directory.");
                try
                {
                    Directory.Delete(installParams.ClientDirectory);
                }
                catch (IOException ioe)
                {
                    Log("    Could not remove client directory: I/O error.");
                    Log(ioe.ToString());
                }
                catch (UnauthorizedAccessException uae)
                {
                    Log("    Could not remove client directory: access denied.");
                    Log(uae.ToString());
                }
            }
        }

        /// <summary>
        /// Configures the notifier app.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void InstallAndConfigureMailer(InstallParameters installParams)
        {
            Log("Installing notifier.");

            string targetDir = installParams.NotifierDirectory == null ?
                Path.Combine(installParams.InstallTarget, "notifier") : installParams.NotifierDirectory;

            if (!Directory.Exists(targetDir))
            {
                Log("  Creating the directory.");
                Directory.CreateDirectory(targetDir);
            }

            Log("  Copying files.");
            string[] files = { "ReviewNotifier.exe", "ReviewNotifier.exe.config", "CodeReviewDataModel.dll",
                "CommonUtils.dll" };
            foreach (string file in files)
                File.Copy(Path.Combine(installParams.InstallSource, file), Path.Combine(targetDir, file), true);

            if (installParams.SmtpServer == null)
                return;

            Log("  Configuring notifier.");

            string notifier = Path.Combine(targetDir, "ReviewNotifier.exe");
            using (Process client = new Process())
            {
                Log("    Configuring smtp.");
                client.StartInfo.UseShellExecute = false;
                client.StartInfo.RedirectStandardError = true;
                client.StartInfo.RedirectStandardOutput = true;
                client.StartInfo.CreateNoWindow = true;
                client.StartInfo.FileName = notifier;
                string args = "smtp " + installParams.SmtpServer + " " + installParams.CompanyDomain;
                if (installParams.UseLdap)
                    args += " useldap";
                if (installParams.UseSsl)
                    args += " usessl";
                client.StartInfo.Arguments = args;

                client.Start();
                string stderr;
                string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
                if (!(String.IsNullOrEmpty(stderr) && String.IsNullOrEmpty(stdout)))
                {
                    Log(stderr);
                    Log(stdout);
                    throw new InstallationException("Failed setting up smpt parameters.");
                }
            }

            using (Process client = new Process())
            {
                Log("    Configuring creds.");
                client.StartInfo.UseShellExecute = false;
                client.StartInfo.RedirectStandardError = true;
                client.StartInfo.RedirectStandardOutput = true;
                client.StartInfo.CreateNoWindow = true;
                client.StartInfo.FileName = notifier;
                client.StartInfo.Arguments = "credentials \"" + Environment.UserName + "\" " +
                    installParams.AliasToSendFrom;

                client.Start();
                string stderr;
                string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
                if (!(String.IsNullOrEmpty(stderr) && String.IsNullOrEmpty(stdout)))
                {
                    Log(stderr);
                    Log(stdout);
                    throw new InstallationException("Failed setting up credentials.");
                }
            }

            using (Process client = new Process())
            {
                Log("    Configuring webserver.");
                client.StartInfo.UseShellExecute = false;
                client.StartInfo.RedirectStandardError = true;
                client.StartInfo.RedirectStandardOutput = true;
                client.StartInfo.CreateNoWindow = true;
                client.StartInfo.FileName = notifier;
                client.StartInfo.Arguments = "webserver " + Environment.MachineName.ToLower();

                client.Start();
                string stderr;
                string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
                if (!(String.IsNullOrEmpty(stderr) && String.IsNullOrEmpty(stdout)))
                {
                    Log(stderr);
                    Log(stdout);
                    throw new InstallationException("Failed setting up server name.");
                }
            }

            using (Process client = new Process())
            {
                Log("    Configuring database.");
                client.StartInfo.UseShellExecute = false;
                client.StartInfo.RedirectStandardError = true;
                client.StartInfo.RedirectStandardOutput = true;
                client.StartInfo.CreateNoWindow = true;
                client.StartInfo.FileName = notifier;
                client.StartInfo.Arguments = "database " + installParams.Database;

                client.Start();
                string stderr;
                string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
                if (!(String.IsNullOrEmpty(stderr) && String.IsNullOrEmpty(stdout)))
                {
                    Log(stderr);
                    Log(stdout);
                    throw new InstallationException("Failed setting up database.");
                }
            }

            Log("    Configuring scheduler.");
            TaskScheduler taskScheduler = new TaskScheduler();
            taskScheduler.Interval = installParams.NotifierInterval;
            taskScheduler.TaskName = "MalevichReviewNotifier";
            taskScheduler.TaskPath = notifier;

            bool success = taskScheduler.SetTask(null, null);
            if (!success)
            {
                Log("Failed to schedule the notifier, please create the task manually.");
                throw new InstallationException("Failed configuring the scheduler.");
            }
        }

        /// <summary>
        /// Removes the notifier app.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void RemoveMailer(InstallParameters installParams)
        {
            Log("Uninstalling notifier.");

            string notifier = Path.Combine(installParams.NotifierDirectory, "ReviewNotifier.exe");
            if (File.Exists(notifier))
            {
                Log("  Removing scheduled task.");
                TaskScheduler taskScheduler = new TaskScheduler();
                taskScheduler.Interval = installParams.NotifierInterval;
                taskScheduler.TaskName = "MalevichReviewNotifier";
                taskScheduler.TaskPath = notifier;

                taskScheduler.SetTask(null, null);

                using (Process client = new Process())
                {
                    Log("Removing notifier configuration.");
                    client.StartInfo.UseShellExecute = false;
                    client.StartInfo.RedirectStandardError = true;
                    client.StartInfo.RedirectStandardOutput = true;
                    client.StartInfo.CreateNoWindow = true;
                    client.StartInfo.FileName = notifier;
                    client.StartInfo.Arguments = "reset";

                    client.Start();
                    string stderr;
                    string stdout = Malevich.Util.CommonUtils.ReadProcessOutput(client, false, out stderr);
                    if (!String.IsNullOrEmpty(stderr))
                        Log(stderr);
                    if (!String.IsNullOrEmpty(stdout))
                        Log(stdout);
                }

            }

            Log("  Removing notifier files.");
            string[] files = { "ReviewNotifier.exe", "ReviewNotifier.exe.config", "CodeReviewDataModel.dll",
                "CommonUtils.dll" };

            foreach (string file in files)
            {
                string target = Path.Combine(installParams.NotifierDirectory, file);
                if (File.Exists(target))
                    File.Delete(target);
            }

            if (IsDirectoryEmpty(installParams.NotifierDirectory))
            {
                Log("  Removing the empty directory.");
                try
                {
                    Directory.Delete(installParams.NotifierDirectory);
                }
                catch (IOException ioe)
                {
                    Log("    Could not remove notifier directory: I/O error.");
                    Log(ioe.ToString());
                }
                catch (UnauthorizedAccessException uae)
                {
                    Log("    Could not remove notifier directory: access denied.");
                    Log(uae.ToString());
                }
            }
        }

        /// <summary>
        /// Gives user two options and returns  true if user picks the first one.
        /// </summary>
        /// <returns> True if user response matches the first string. </returns>
        private static bool QueryUser(string s1, string s2)
        {
            Console.Write("({0}/{1}): ", s1, s2);
            string s = Console.ReadLine();
            return s1.Equals(s, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Asks user where the base of the installation should live.
        /// </summary>
        /// <param name="prompt"> Prompt. </param>
        private static string QueryDirectory(string prompt)
        {
            for (; ; )
            {
                Console.Write(prompt);
                string result = Console.ReadLine();
                if (Directory.Exists(result))
                    return result;
                try
                {
                    Directory.CreateDirectory(result);
                    return result;
                }
                catch (IOException)
                {
                    Console.WriteLine("Could not create " + result);
                }
            }
        }

        /// <summary>
        /// Checks if a directory is empty.
        /// </summary>
        /// <param name="dir"> Directory. </param>
        /// <returns> True if it has no files. </returns>
        private static bool IsDirectoryEmpty(string dir)
        {
            return Directory.GetDirectories(dir).Length + Directory.GetFiles(dir).Length == 0;
        }

        /// <summary>
        /// Allows user to select the database instance.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void SelectDatabaseInstance(InstallParameters installParams)
        {
            List<string> databaseInstances = installParams.Platform.DatabaseInstances;

            if (databaseInstances.Count == 0)
                throw new PlatformException("No available database instances.");
            if (databaseInstances.Count == 1)
            {
                installParams.Database = databaseInstances[0];
            }
            else
            {
                Console.WriteLine("Which database instance would you like to use:");
                int count = 0;
                foreach (string database in databaseInstances)
                    Console.WriteLine("{0}. {1}", count++, database);
                int answer = -1;
                while (answer < 0 || answer >= count)
                {
                    Console.Write("[0..{0}]: ", count);
                    string response = Console.ReadLine();
                    if (!int.TryParse(response, out answer))
                        answer = -1;
                }
                installParams.Database = databaseInstances[answer];
            }
        }

        /// <summary>
        /// Gets the name of the web app from user, if it's not Malevich.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void CollectWebAppName(InstallParameters installParams)
        {
            Console.WriteLine("Malevich would be installed as an application in the default web site.");
            Console.WriteLine("By default, the web site will be addressable as http://" + Environment.MachineName +
                "/Malevich");

            for (; ; )
            {
                Console.Write("Would you like to change the path under the default web site? ");
                if (!QueryUser("Yes", "No"))
                    break;
                Console.Write("What would you like it to be? ");
                string webAppName = Console.ReadLine();
                Regex ValidWebAppName = new Regex("^[a-zA-Z0-9-_]+$");
                if (ValidWebAppName.IsMatch(webAppName))
                {
                    installParams.WebApplicationName = webAppName;
                    break;
                }

                Console.WriteLine();
                Console.WriteLine("Application path can only contain letters, numbers, - and _.");
                Console.WriteLine();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Gets user input for notifier configuration.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void CollectNotifierParameters(InstallParameters installParams)
        {
            Console.Write("Would you like to skip configuring mail notifier? ");
            if (!QueryUser("Yes", "No"))
            {
                for (; ; )
                {
                    Console.Write("Please enter the host name of your SMTP server\n" +
                        "(e.g. smtphost.redmond.corp.microsoft.com): ");
                    string smtp = Console.ReadLine();
                    Console.Write("Please enter the email domain for your company\n(e.g. microsoft.com): ");
                    string email = Console.ReadLine();
                    Console.Write("Does your SMTP server require SSL connection ");
                    bool useSsl = QueryUser("Yes", "No");
                    Console.Write("Are email aliases in your organization EXACTLY the same as user names ");
                    bool useLdap = !QueryUser("Yes", "No");
                    Console.WriteLine("The mailer will run as a scheduled task under your account.");
                    Console.WriteLine("If you would like to configure email such that it is sent from");
                    Console.WriteLine("a different account, enter the alias of this account now.");
                    Console.WriteLine("Otherwise, just press ENTER. Note: you must have the right");
                    Console.WriteLine("to send email on behalf of this account.");
                    Console.Write("Alias to send email from: ");
                    string altalias = Console.ReadLine();

                    Console.WriteLine("I will not attempt to validate the information you have given");
                    Console.WriteLine("by attempting to send a test email message.");

                    try
                    {
                        SmtpClient client = new SmtpClient(smtp);
                        client.UseDefaultCredentials = true;
                        if (useSsl)
                            client.EnableSsl = true;

                        string userName = Environment.GetEnvironmentVariable("USERNAME");
                        string sender = null;
                        if (useLdap)
                        {
                            DirectorySearcher directorySearcher = new DirectorySearcher();
                            directorySearcher.PropertiesToLoad.Add("mail");
                            directorySearcher.Filter = "(SAMAccountName=" + userName + ")";
                            SearchResult result = directorySearcher.FindOne();
                            if (result != null && result.Properties["mail"].Count > 0)
                                sender = result.Properties["mail"][0].ToString();
                            else
                                throw new ConfigurationException("Failed to resolve user name using LDAP.");
                        }
                        else
                        {
                            sender = userName + "@" + email;
                        }

                        string from = String.IsNullOrEmpty(altalias) ? sender : (altalias + "@" + email);
                        MailMessage message = new MailMessage();
                        message.To.Add(sender);
                        message.Subject = "Email from Malevich setup";
                        message.From = new MailAddress(from);
                        message.Sender = new MailAddress(from);
                        message.Body = "This is a test";
                        message.IsBodyHtml = false;

                        client.Send(message);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to send the message: " + ex);
                        continue;
                    }

                    Console.WriteLine("Please check your inbox now to make sure that you did receive the test");
                    Console.WriteLine("message. It should be titled 'Email from Malevich setup'.");
                    Console.Write("Did you receive it? ");
                    if (QueryUser("Yes", "No"))
                    {
                        if (!String.IsNullOrEmpty(altalias))
                            installParams.AliasToSendFrom = altalias;
                        installParams.SmtpServer = smtp;
                        installParams.CompanyDomain = email;
                        installParams.UseSsl = useSsl;
                        installParams.UseLdap = useLdap;
                        break;
                    }
                }

                for (; ; )
                {
                    Console.WriteLine("Email notifier runs periodically to send email about active reviews.");
                    Console.WriteLine("A reasonable interval could be between 5 and 15 minutes. It should");
                    Console.WriteLine("not be run more frequently than every 3 minutes.");
                    Console.Write("How often (in minutes) would you like it to run? ");
                    string interval = Console.ReadLine();
                    if (int.TryParse(interval, out installParams.NotifierInterval) &&
                        installParams.NotifierInterval > 3)
                        break;
                }
            }
        }

        /// <summary>
        /// Shows installation parameters and allows user to abort installation.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        /// <param name="freshInstall"> Whether this is a fresh install. </param>
        /// <returns> True if the installation is to proceed. </returns>
        private static bool DoInstallOrUpgrade(InstallParameters installParams, bool freshInstall)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("The installer is ready to configure your system.");
            Console.WriteLine();
            Console.WriteLine("The following installation parameters have been selected:");
            Console.WriteLine("  Installation directory: {0}", installParams.InstallTarget);
            Console.WriteLine("  SQL server instance: {0}", installParams.Database);
            if (installParams.DatabaseDirectory == null)
            {
                Console.WriteLine("    (A new database will be created.)");
            }
            else
            {
                Console.WriteLine("    (Database in {0} will be upgraded.)", installParams.DatabaseDirectory);
                if (installParams.FixChangeListTimeStamps)
                    Console.WriteLine("    (Change List time stamps will be converted to UTC)");
                Console.WriteLine("    (Backup will be made in {0}\\backup.)", installParams.InstallTarget);
            }

            if (freshInstall)
            {
                // WebApplicationName is not defined, nor used for the upgrade.
                Console.WriteLine("  Web site: http://{0}/{1}", Environment.MachineName,
                    installParams.WebApplicationName == null ? "Malevich" : installParams.WebApplicationName);
            }

            Console.WriteLine("  Web site path: {0}\\Web", installParams.InstallTarget);
            Console.WriteLine("  Review client share: \\\\{0}\\reviewclient", Environment.MachineName);
            Console.WriteLine("  Review client path: {0}\\client", installParams.InstallTarget);
            Console.WriteLine("  Notifier path: {0}\\notifier", installParams.InstallTarget);
            if (installParams.SmtpServer == null)
            {
                Console.WriteLine("    (The mailer will not be configured.)");
            }
            else
            {
                Console.WriteLine("    SMTP server: {0}", installParams.SmtpServer);
                Console.WriteLine("    Email domain: {0}", installParams.CompanyDomain);
                Console.WriteLine("    Email sent from: {0}.", installParams.AliasToSendFrom != null ?
                    installParams.AliasToSendFrom : "your own account.");
            }
            Console.WriteLine("   Unix utils: {0}", installParams.UnixUtilsDiff != null ?
                installParams.UnixUtilsDiff : "will be installed in " + installParams.InstallTarget +
                "\\unxutils");
            Console.WriteLine();
            for (; ; )
            {
                Console.Write("Do you want me to start installation with the above parameters? ");
                if (QueryUser("Yes", "No"))
                    break;
                Console.Write("Abort the installation? ");
                if (QueryUser("Yes", "No"))
                    return false;
            }

            Console.WriteLine();
            return true;
        }

        /// <summary>
        /// Installs Malevich.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        private static void Install(InstallParameters installParams)
        {
            Console.WriteLine();
            if (installParams.DatabaseDirectory != null)
            {
                // If the database directory is not inside SQL, we might be able to use it.
                if (!installParams.DatabaseDirectory.Contains("MSSQL"))
                {
                    Console.WriteLine("The database currently resides in {0}.", installParams.DatabaseDirectory);
                    Console.WriteLine("Do you want to use the same directory ");
                    Console.Write("for other components of Malevich? ");
                    if (QueryUser("Yes", "No"))
                        installParams.InstallTarget = installParams.DatabaseDirectory;
                    Console.WriteLine();
                }
            }

            if (installParams.InstallTarget == null)
            {
                Console.WriteLine("The base directory for Malevich will contain the web site, the backup of");
                Console.WriteLine("the code review database, the notifier program, and the database itself");
                Console.WriteLine("(it it has not been created already).");

                for (; ; )
                {
                    string dir = QueryDirectory("Please enter the name of the base directory: ");
                    if (dir.StartsWith("\\\\") || !Path.IsPathRooted(dir))
                    {
                        Console.WriteLine("This should be a full path starting with a drive letter.");
                        continue;
                    }
                    installParams.InstallTarget = dir;
                    break;
                }
                Console.WriteLine();
            }

            if (installParams.Database == null)
                SelectDatabaseInstance(installParams);

            CollectWebAppName(installParams);

            Console.WriteLine("While Malevich mail notifier can be used with both SMTP and Exchange Web");
            Console.WriteLine("Services 2007, this program only supports SMTP server-based installation.");
            Console.WriteLine("You can chose to defer mailer configuration until later and do it by hand");
            Console.WriteLine("if you prefer to use EWS 2007. However, you would then miss on automatic");
            Console.WriteLine("configuration and verification that this program provides.");
            Console.WriteLine();

            CollectNotifierParameters(installParams);
            if (!DoInstallOrUpgrade(installParams, true))
                return;

            InstallDatabase(installParams);
            if (installParams.UnixUtilsDiff == null)
                UnpackDiffer(installParams);
            CreateWebSite(installParams);
            CopyAndShareClientBits(installParams);
            InstallAndConfigureMailer(installParams);
        }

        /// <summary>
        /// Gets the common directory name for Malevich components.
        /// </summary>
        /// <param name="installParams"> Installation parameters.  </param>
        /// <param name="uninstall"> Whether this is an uninstallation. </param>
        private static void VerifyOrGetInstallTarget(InstallParameters installParams, bool uninstall)
        {
            if (installParams.InstallTarget != null)
            {
                Console.WriteLine("Installation program found existing installation base in");
                Console.WriteLine("directory {0}.", installParams.InstallTarget);
                Console.Write("Is this correct? ");
                if (QueryUser("Yes", "No"))
                    return;
            }

            if (uninstall && installParams.Database != null)
            {
                Console.WriteLine("Before uninstallation, the configuration program must backup the");
                Console.WriteLine("database.");
                string dir = QueryDirectory("Please enter the directory which will hold the backup: ");
                installParams.InstallTarget = dir;
            }

            if (!uninstall)
            {
                Console.WriteLine("Configuration program places most Malevich components under one root.");
                string dir = QueryDirectory("Please enter the directory for Malevich components: ");
                installParams.InstallTarget = dir;
            }
        }

        /// <summary>
        /// Uninstalls Malevich.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void Uninstall(InstallParameters installParams)
        {
            Console.WriteLine();
            Console.WriteLine("We will now uninstall Malevich. All components of the application,");
            Console.WriteLine("except the database will be removed. If you have made customizations,");
            Console.WriteLine("particularly to the web site, manually back them up before continuing.");
            Console.Write("Proceed with uninstallation? ");
            if (!QueryUser("Yes", "No"))
                return;

            VerifyOrGetInstallTarget(installParams, true);
            Console.WriteLine();

            if (installParams.Website != null)
                RemoveWebSite(installParams);
            if (installParams.ClientShare != null)
                RemoveClientBits(installParams);
            if (installParams.NotifierDirectory != null)
                RemoveMailer(installParams);
            if (installParams.Database != null)
                BackupDatabase(installParams);

            Log("The database has not been removed. Detach it manually in SQL Server Management");
            Log("Studio if you want it removed.");
        }

        /// <summary>
        /// Checks if timestamp fix might apply. If yes, verifies with the user.
        /// </summary>
        /// <param name="installParams"></param>
        private static void CheckIfTimestampFixRequired(InstallParameters installParams)
        {
            if (installParams.Database == null || installParams.WebsitePath == null)
                return;

            if (File.Exists(Path.Combine(installParams.WebsitePath, "datetime.js")))
                return;

            Console.WriteLine("The installation program has detected that you are upgrading from an earlier");
            Console.WriteLine("version of Malevich which had a bug - instead of storing the UTC time stamps");
            Console.WriteLine("for the change lists, it was using the local time.");
            Console.WriteLine();
            Console.WriteLine("The new code treats all time stamps as UTC. If you were to deploy this");
            Console.WriteLine("upgrade, the time stamps for change lists would show incorrectly.");
            Console.WriteLine();
            Console.WriteLine("The installer can attempt to fix the problem for you. Because of");
            Console.WriteLine("the daylight savings time, some of the automatic conversions might");
            Console.WriteLine("end up being an hour off. But if you do not apply the fix, the time");
            Console.WriteLine("stamps will be off by the time shift of your time zone (plus the daylight");
            Console.WriteLine("shift).");
            Console.WriteLine();
            Console.Write("Would you like the installer to convert the time stamps to UTC? ");
            installParams.FixChangeListTimeStamps = QueryUser("Yes", "No");
            Console.WriteLine();
        }

        /// <summary>
        /// Upgrade.
        /// </summary>
        /// <param name="installParams"> Installation parameters. </param>
        private static void Upgrade(InstallParameters installParams)
        {
            Console.WriteLine();
            Console.WriteLine("The configuration program will now upgrade Malevich components.");
            Console.WriteLine("Missing components will be installed.");
            Console.WriteLine();
            VerifyOrGetInstallTarget(installParams, true);
            Console.WriteLine();

            if (installParams.Database == null)
                SelectDatabaseInstance(installParams);
            else
                CheckIfTimestampFixRequired(installParams);

            if (installParams.Website == null)
                CollectWebAppName(installParams);

            if (installParams.NotifierDirectory == null)
            {
                Console.WriteLine("Installation program could not detect whether the mail notifier is present");
                Console.WriteLine("on this system. If it is, you will need to manually delete the scheduled");
                Console.WriteLine("tasks associated  with it. Meanwhile, the new version will be installed");
                Console.WriteLine("in a default location. The configuration of the mailer can be inherited");
                Console.WriteLine("from the previous version, or supplied anew. Note that the installation");
                Console.WriteLine("program only supports SMTP mail transport.");

                CollectNotifierParameters(installParams);
            }

            if (!DoInstallOrUpgrade(installParams, true))
                return;

            InstallDatabase(installParams);

            if (installParams.UnixUtilsDiff == null)
                UnpackDiffer(installParams);
            if (installParams.WebsitePath == null)
                CreateWebSite(installParams);
            else
                UpdateWebSite(installParams);
            CopyAndShareClientBits(installParams);
            InstallAndConfigureMailer(installParams);
        }

        /// <summary>
        /// Prints out the installation features.
        /// </summary>
        /// <param name="param"> Installation parameters. </param>
        private static void ListInstalledFeatures(InstallParameters param)
        {
            Console.WriteLine("The following components were found:");
            if (param.Database != null && param.DatabaseDirectory != null)
            {
                Console.WriteLine("  CodeReview database:");
                Console.WriteLine("    SQL server instance: {0}", param.Database);
                Console.WriteLine("    Database directory: {0}", param.DatabaseDirectory);
            }

            if (param.Website != null && param.WebsitePath != null && param.App != null)
            {
                Console.WriteLine("  Web site:");
                Console.WriteLine("    IIS site: {0}", param.Website.Name);
                Console.WriteLine("    Application: {0}", param.App.Path);
                Console.WriteLine("    Path: {0}", param.WebsitePath);
            }

            if (param.NotifierDirectory != null)
            {
                Console.WriteLine("  Mailer:");
                Console.WriteLine("    Directory: {0}", param.NotifierDirectory);
            }

            if (param.ClientShare != null)
            {
                Console.WriteLine("  Review client:");
                Console.WriteLine("    Share name: {0}", param.ClientShare);
                Console.WriteLine("    Directory: {0}", param.ClientDirectory);
            }

            if (param.UnixUtilsDiff != null)
                Console.WriteLine("  Unix utilities (diff): {0}", param.UnixUtilsDiff);
        }

        /// <summary>
        /// Main entry point in the installer.
        /// </summary>
        /// <param name="args"> Command line arguments. </param>
        static void Main(string[] args)
        {
            Console.WriteLine("Welcome to Malevich configuration program!");
            Console.WriteLine();
            Console.WriteLine("This program performs essential configuration steps");
            Console.WriteLine("required before Malevich can be run. The MSI installer");
            Console.WriteLine("uncompresses files. This program puts them in the right");
            Console.WriteLine("places.");
            Console.WriteLine();
            Console.WriteLine("Malevich configuration program should be run every time");
            Console.WriteLine("after Malevich has been installed or upgraded, and before");
            Console.WriteLine("it has been removed.");
            Console.WriteLine();
            Console.WriteLine("Please wait while we verify your computer and detect the");
            Console.WriteLine("details of current Malevich installation (if any).");
            try
            {
                DetectedPlatform platform = VerifyPlatform();
                InstallParameters param = DetectInstallState();
                param.Platform = platform;

                Console.WriteLine();

                if (param.InstallState == CurrentState.UnsupportedInstall)
                {
                    Console.WriteLine("The installation program detected an installation in a state which");
                    Console.WriteLine("it cannot support. This happens typically when parts of the");
                    Console.WriteLine("installation are present, but the database could not be found.");
                    Console.WriteLine();
                    Console.WriteLine("Such setups must be fixed by hand before this program can proceed.");
                    return;
                }

                if (param.InstallState == CurrentState.NoInstall)
                {
                    if ((param.Platform.OS == DetectedPlatform.OSType.Server2008R2 ||
                        param.Platform.OS == DetectedPlatform.OSType.Win7 ||
                        param.Platform.OS == DetectedPlatform.OSType.Vista) &&
                        param.Platform.SQL == DetectedPlatform.SqlType.SqlServer2008)
                    {
                        Console.WriteLine("Installation program has determined that you are running on");
                        Console.WriteLine("Windows Server 2008 R2, Windows 7, or Windows Vista. SQL Server");
                        Console.WriteLine("2008 does not automatically open a firewall port on these");
                        Console.WriteLine("platforms. Unless SQL Server ports are opened through a group");
                        Console.WriteLine("policy or by a previous user action, the client program will");
                        Console.WriteLine("not be able to connect to the server.");
                        Console.WriteLine();
                        Console.WriteLine("If this happens, please configure exceptions for SQL Server");
                        Console.WriteLine("and SQL Server Browser manually.");
                        Console.WriteLine();
                        if (param.Platform.OS == DetectedPlatform.OSType.Vista)
                        {
                            Console.WriteLine("In addition, on Windows Vista, the firewall exception must be");
                            Console.WriteLine("manually configured for the Web server as well.");
                            Console.WriteLine();
                        }

                        Console.WriteLine("Press Enter to continue.");
                        Console.ReadLine();
                    }
                    Install(param);
                    return;
                }

                if (param.InstallState == CurrentState.DatabaseOnly)
                {
                    Console.WriteLine("Installation found an existing Malevich database.");
                    Console.WriteLine("It will use the existing database (upgraded as necessary)");
                    Console.WriteLine("for this installation. The database will be backed up");
                    Console.WriteLine("before the upgrade.");
                    Console.WriteLine();
                    Console.Write("Do you want the installation process to continue? ");
                    if (!QueryUser("Yes", "No"))
                        return;
                    Install(param);
                    Console.WriteLine("Installation is successful. Congratulations!");
                    return;
                }

                if (param.InstallState == CurrentState.PartialInstall)
                {
                    Console.WriteLine("The installation program has detected previous partial installation");
                    Console.WriteLine("of Malevich.");
                }

                if (param.InstallState == CurrentState.CoherentInstall)
                {
                    Console.WriteLine("The installation program has detected previous full installation");
                    Console.WriteLine("of Malevich.");
                }

                ListInstalledFeatures(param);

                Console.Write("Would you like to upgrade, uninstall, or exit: ");
                string action = Console.ReadLine();
                if ("upgrade".Equals(action, StringComparison.OrdinalIgnoreCase))
                    Upgrade(param);
                else if ("uninstall".Equals(action, StringComparison.OrdinalIgnoreCase))
                    Uninstall(param);
            }
            catch (PlatformException ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }
            catch (InstallationException ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.Read();
            }
        }
    }
}
