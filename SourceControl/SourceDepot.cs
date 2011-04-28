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
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Win32;

namespace SourceControl
{
    /// <summary>
    /// The implementation of the source depot class.
    /// </summary>
    public sealed class SourceDepot : ISourceControlSystem
    {
        /// <summary>
        /// The endpoint of the source depot server (servername:tcpport).
        /// The name (Port) is a bit misleading, but it is in keeping with the source depot terminology.
        /// </summary>
        public string Port;

        /// <summary>
        /// The proxy server name. Null if none.
        /// </summary>
        public string Proxy;

        /// <summary>
        /// The client string.
        /// </summary>
        public string Client;

        SourceControlType ISourceControlSystem.ServerType
        { get { return SourceControlType.SD; } }

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="port"> The endpoint of the source depot server (servername:tcpport). </param>
        /// <param name="client"> The name of the client. </param>
        /// <param name="proxy"> Proxy server, same format as port name. Can be null if proxy is not used. </param>
        public SourceDepot(string port, string client, string proxy)
        {
            Port = port;
            Proxy = proxy;
            Client = client;
        }
    }

    /// <summary>
    /// The wrapper for source depot version of PerforceBase source control interface.
    /// </summary>
    public sealed class SourceDepotInterface : PerforceBase
    {
        /// <summary>
        /// Verifies that SD installation can actually be used. Throws SourceControlRuntimeError if it cannot be.
        /// </summary>
        private static void VerifySourceDepotRequirements()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Source Depot\Environment");
            if (key == null)
                return;

            try
            {
                if (key.GetValue("SDDIFF") != null || key.GetValue("SDUDIFF") != null)
                {
                    Console.WriteLine();
                    Console.WriteLine("SDDIFF or SDUDIFF variables are set in the registry.");
                    Console.WriteLine("Review submission tool relies on default SD differ");
                    Console.WriteLine("will not work correctly with any other tool.");
                    Console.WriteLine();
                    Console.WriteLine("Consider moving SDDIFF and SDUDIFF configuration to");
                    Console.WriteLine("sd.ini or your environment, and remove their definition");
                    Console.WriteLine("from the registry by running:");
                    Console.WriteLine("    sd set SDDIFF=");
                    Console.WriteLine("    sd set SDUDIFF=");

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
        /// <param name="sdClient"> The location of sd command. </param>
        /// <param name="port"> The port of source depot: servername:tcpport form. See sd.ini. </param>
        /// <param name="client"> The client name. See sd.ini. </param>
        /// <param name="proxy"> The proxy server (same format as port) or null if none. </param>
        private SourceDepotInterface(string sdClient, string port, string client, string proxy)
            : base(sdClient, new SourceDepot(port, client, proxy))
        {
        }

        /// <summary>
        /// Factory for the source depot connector instances.
        /// </summary>
        /// <param name="sdClient"> The location of sd command. </param>
        /// <param name="port"> The port of source depot: servername:tcpport form. See sd.ini. </param>
        /// <param name="client"> The client name. See sd.ini. </param>
        /// <param name="proxy"> The proxy server (same format as port) or null if none. </param>
        /// <returns> The source control instance. </returns>
        public static ISourceControl GetInstance(string sdClient, string port, string client, string proxy)
        {
            VerifySourceDepotRequirements();

            return new SourceDepotInterface(sdClient, port, client, proxy);
        }

        /// <summary>
        /// Gets the source depot settings.
        /// </summary>
        /// <returns></returns>
        public static SourceControlSettings GetSettings()
        {
            SourceControlSettings settings = new SourceControlSettings();
            settings.Port = Environment.GetEnvironmentVariable("SDPORT");
            if (settings.Port != null)
                settings.Port = settings.Port.Trim();

            settings.Proxy = Environment.GetEnvironmentVariable("SDPROXY");
            if (settings.Proxy != null)
                settings.Proxy = settings.Proxy.Trim();

            settings.Client = Environment.GetEnvironmentVariable("SDCLIENT");
            if (settings.Client != null)
                settings.Client = settings.Client.Trim();

            string path = Environment.GetEnvironmentVariable("path").Replace("\"", "");
            string[] pathArray = path.Split(';');
            for (int i = 0; i < pathArray.Length; ++i)
            {
                string sd = Path.Combine(pathArray[i], "sd.exe");
                if (File.Exists(sd))
                {
                    settings.ClientExe = sd;
                    break;
                }
            }

            string dir = Directory.GetCurrentDirectory();
            string root = Path.GetPathRoot(dir);
            while (!dir.Equals(root))
            {
                string sd = Path.Combine(dir, "sd.ini");
                if (File.Exists(sd))
                {
                    Regex portRegex = new Regex(@"^(\s)*SDPORT(\s)*=(\s)*(?<port>(\S)*)(\s)*$",
                        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                    Regex proxyRegex = new Regex(@"^(\s)*SDPROXY(\s)*=(\s)*(?<port>(\S)*)(\s)*$",
                        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                    Regex clientRegex = new Regex(@"^(\s)*SDCLIENT(\s)*=(\s)*(?<client>(\S)*)(\s)*$",
                        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
                    StreamReader sr = new StreamReader(sd);
                    while (settings.Port == null || settings.Client == null || settings.Proxy == null)
                    {
                        string l = sr.ReadLine();
                        if (l == null)
                            break;

                        if (settings.Proxy == null)
                        {
                            Match proxyMatch = proxyRegex.Match(l);
                            if (proxyMatch.Success)
                            {
                                settings.Proxy = proxyMatch.Groups[1].Value;
                                continue;
                            }
                        }

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
                    }
                    sr.Close();
                    break;
                }

                dir = Path.GetDirectoryName(dir);
            }


            return settings;
        }
    }
}
