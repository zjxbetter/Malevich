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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace SourceControl
{
    /// <summary>
    /// The implementation of the perforce class.
    /// </summary>
    public sealed class Perforce : ISourceControlSystem
    {
        /// <summary>
        /// The endpoint of the perforce server (servername:tcpport).
        /// The name (Port) is a bit misleading, but it is in keeping with the perforce terminology.
        /// </summary>
        public string Port;

        /// <summary>
        /// The client string.
        /// </summary>
        public string Client;

        /// <summary>
        /// Perforce user name. Can be null.
        /// </summary>
        public string User;

        /// <summary>
        /// Perforce password. Can be null.
        /// </summary>
        public string Passwd;

        SourceControlType ISourceControlSystem.ServerType
        { get { return SourceControlType.PERFORCE; } }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="port"> The endpoint of the source perforce server (servername:tcpport). </param>
        /// <param name="client"> The name of the client. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        public Perforce(string port, string client, string user, string passwd)
        {
            Port = port;
            Client = client;
            User = user;
            Passwd = passwd;
        }
    }

    /// <summary>
    /// The wrapper for perforce version of PerforceBase source control interface.
    /// </summary>
    public sealed class PerforceInterface : PerforceBase
    {
        /// <summary>
        /// Verifies that perforce installation can actually be used. Throws SourceControlRuntimeError if it cannot be.
        /// </summary>
        private static void VerifyPerforceRequirements()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Perforce\Environment");
            if (key == null)
                return;

            try
            {
                if (key.GetValue("P4DIFF") != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("P4DIFF variable is set in the registry.");
                    Console.WriteLine("Review submission tool relies on default P4 differ");
                    Console.WriteLine("will not work correctly with any other tool.");
                    Console.WriteLine();
                    Console.WriteLine("Consider moving P4DIFF configuration to p4config");
                    Console.WriteLine("file or your environment, and remove its definition");
                    Console.WriteLine("from the registry by running:");
                    Console.WriteLine("    p4 set P4DIFF=");

                    throw new SourceControlRuntimeError();
                }
            }
            finally
            {
                key.Close();
            }
        }

        /// <summary>
        /// Trivial constructor. Just wraps around the Perforce base.
        /// </summary>
        /// <param name="p4Client"> The location of p4 command. </param>
        /// <param name="port"> The port of perforce: servername:tcpport form. </param>
        /// <param name="client"> The client name. See sd.ini. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        private PerforceInterface(string p4Client, string port, string client, string user, string passwd)
            : base(p4Client, new Perforce(port, client, user, passwd))
        {
        }

        /// <summary>
        /// Factory for the perforce connector instances.
        /// </summary>
        /// <param name="p4Client"> The location of p4 command. </param>
        /// <param name="port"> The port of perforce: servername:tcpport form. </param>
        /// <param name="client"> The client name. See sd.ini. </param>
        /// <param name="user"> Perforce user name, can be null. </param>
        /// <param name="passwd"> Perforce password, can be null. </param>
        /// <returns> The source control instance. </returns>
        public static ISourceControl GetInstance(string p4Client, string port, string client,
            string user, string passwd)
        {
            VerifyPerforceRequirements();

            return new PerforceInterface(p4Client, port, client, user, passwd);
        }

        /// <summary>
        /// Gets the perforce client settings.
        /// </summary>
        /// <returns></returns>
        public static SourceControlSettings GetSettings()
        {
            SourceControlSettings settings = new SourceControlSettings();
            settings.Port = Environment.GetEnvironmentVariable("P4PORT");
            if (settings.Port != null)
                settings.Port = settings.Port.Trim();

            settings.Client = Environment.GetEnvironmentVariable("P4CLIENT");
            if (settings.Client != null)
                settings.Client = settings.Client.Trim();

            settings.User = Environment.GetEnvironmentVariable("P4USER");
            if (settings.User != null)
                settings.User = settings.User.Trim();

            settings.Password = Environment.GetEnvironmentVariable("P4PASSWD");

            string path = Environment.GetEnvironmentVariable("path").Replace("\"", "");
            string[] pathArray = path.Split(';');
            for (int i = 0; i < pathArray.Length; ++i)
            {
                string p4 = Path.Combine(pathArray[i], "p4.exe");
                if (File.Exists(p4))
                {
                    settings.ClientExe = p4;
                    break;
                }
            }

            string p4Config = Environment.GetEnvironmentVariable("P4CONFIG");
            if (p4Config != null)
            {
                string dir = Directory.GetCurrentDirectory();
                string root = Path.GetPathRoot(dir);
                while (!dir.Equals(root))
                {
                    string p4 = Path.Combine(dir, p4Config);
                    if (File.Exists(p4))
                    {
                        Regex portRegex = new Regex(@"^(\s)*P4PORT(\s)*=(\s)*(?<port>(\S)*)(\s)*$",
                            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                        Regex clientRegex = new Regex(@"^(\s)*P4CLIENT(\s)*=(\s)*(?<client>(\S)*)(\s)*$",
                            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                        Regex userRegex = new Regex(@"^(\s)*P4USER(\s)*=(\s)*(?<user>(\S)*)(\s)*$",
                            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                        Regex passwdRegex = new Regex(@"^(\s)*P4PASSWD(\s)*=(\s)*(?<passwd>(\S)*)(\s)*$",
                            RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                        StreamReader sr = new StreamReader(p4);
                        while (settings.Port == null || settings.Client == null || settings.User == null ||
                            settings.Password == null)
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
                                    continue;
                                }
                            }

                            if (settings.Client == null)
                            {
                                Match clientMatch = clientRegex.Match(l);
                                if (clientMatch.Success)
                                {
                                    settings.Client = clientMatch.Groups[1].Value;
                                    continue;
                                }
                            }

                            if (settings.User == null)
                            {
                                Match userMatch = userRegex.Match(l);
                                if (userMatch.Success)
                                {
                                    settings.User = userMatch.Groups[1].Value;
                                    continue;
                                }
                            }

                            if (settings.Password == null)
                            {
                                Match passwdMatch = passwdRegex.Match(l);
                                if (passwdMatch.Success)
                                {
                                    settings.Password = passwdMatch.Groups[1].Value;
                                    continue;
                                }
                            }
                        }
                        sr.Close();
                        break;
                    }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            return settings;
        }
    }
}
