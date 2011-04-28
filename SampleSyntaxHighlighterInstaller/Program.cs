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
using System.Xml;
using System.Xml.XPath;

using Microsoft.Web.Administration;

namespace SampleSyntaxHighlighterInstaller
{
    /// <summary>
    /// Installs, uninstalls, or upgrades the Sample Syntax Highlighter.
    /// </summary>
    class Program
    {
        /// <summary>
        /// All the languages for which we need encoders.
        /// </summary>
        private static string[] encoderLanguages =
        { 
            "c", "cpp", "cxx", "h", "hpp", "hxx", "cs", "java", "jav", "js",
            "m", "mm",
            "htm", "html", "xml", "xsd", "xslt", "aspx", "ascx", "asmx", "wxs", "wxl", "wxi",
            "csproj", "vcproj", "vdproj", "dbproj", "resx", "config", "xaml",
            "sql"
        };

        /// <summary>
        /// File name of the highlighter.
        /// </summary>
        private const string highlighterName = "SampleSyntaxHighlighter.dll";

        /// <summary>
        /// Does all the installation work.
        /// </summary>
        private static void InstallDriver()
        {
            string highlighter = Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]),
                highlighterName);
            if (!File.Exists(highlighter))
            {
                Console.WriteLine("Could not find the highlighter dll.\n" +
                    "Please ensure that the installation sources are correct.");
                return;
            }

            string webSitePath = null;

            Console.WriteLine("  Looking for Malevich website.");
            ServerManager iis = new ServerManager();
            foreach (Site site in iis.Sites)
            {
                Console.WriteLine("    Checking " + site.Name);
                foreach (Application app in site.Applications)
                {
                    Console.WriteLine("      Checking application " + app.Path);
                    foreach (VirtualDirectory dir in app.VirtualDirectories)
                    {
                        Console.WriteLine("        Checking directory " + dir.PhysicalPath);
                        if (File.Exists(Path.Combine(dir.PhysicalPath, "ReviewStyle.css")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"comments.js")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"hints.js")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"navigator.js")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"bin\CodeReviewDataModel.dll")) &&
                            File.Exists(Path.Combine(dir.PhysicalPath, @"bin\CommonUtils.dll")))
                        {
                            Console.WriteLine("    Found Malevich on " + site.Name + " (" + app.Path + ") in " + dir.PhysicalPath);
                            webSitePath = dir.PhysicalPath;
                            goto done;
                        }
                    }
                }
            }

        done:
            if (webSitePath == null)
            {
                Console.WriteLine("Could not find Malevich installation.");
                return;
            }

            string highlighterNameRel = "\\" + highlighterName;

            string file = Path.Combine(webSitePath, "web.config");
            XmlDocument webConfig = new XmlDocument();
            webConfig.Load(file);
            bool upgrade = false;
            bool remove = false;
            int removed = 0;
            int upgraded = 0;
            XPathNavigator cursor = webConfig.CreateNavigator();
            XPathNodeIterator appSettings = cursor.Select("/configuration/appSettings/add");
            HashSet<string> encodersConfigured = new HashSet<string>();
            List<XPathNavigator> nodesToRemove = new List<XPathNavigator>();
            foreach (XPathNavigator appSetting in appSettings)
            {
                string name = appSetting.GetAttribute("key", "").ToLower();
                if (name.StartsWith("encoder_"))
                {
                    string currentValue = appSetting.GetAttribute("value", "");
                    if (string.IsNullOrEmpty(currentValue) || currentValue.EndsWith(highlighterNameRel,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        if (!(upgrade || remove))
                        {
                            Console.Write("Would you like to upgrade, uninstall, or exit: ");
                            string action = Console.ReadLine();
                            if ("upgrade".Equals(action, StringComparison.OrdinalIgnoreCase))
                                upgrade = true;
                            else if ("uninstall".Equals(action, StringComparison.OrdinalIgnoreCase))
                                remove = true;
                            else
                                return;
                        }

                        if (remove)
                        {
                            Console.WriteLine("Removing the setting for {0}", name);
                            nodesToRemove.Add(appSetting);
                            ++removed;
                        }
                        else
                        {
                            Console.WriteLine("Resetting {0} to {1}", name, highlighter);
                            appSetting.MoveToAttribute("value", "");
                            appSetting.SetValue(highlighter);
                            ++upgraded;
                        }
                    }

                    encodersConfigured.Add(name);
                }
            }

            if (remove)
            {
                foreach (XPathNavigator node in nodesToRemove)
                    node.DeleteSelf();
                webConfig.Save(file);
                Console.WriteLine("Removed {0} file handlers.", removed);
                return;
            }

            if (!upgrade)
            {
                Console.WriteLine("Previous installation of the sample syntax highlighter was not found.");
                if (encodersConfigured.Count > 0)
                {
                    Console.WriteLine("However, the encoders for the following languages have been detected:");

                    foreach (string s in encodersConfigured)
                        Console.WriteLine("    {0}", s.Substring(8));
                }

                Console.WriteLine("Would you like to add the the sample syntax highlighter?");
                Console.Write("Already existing settings will be unaffected. (Y/N): ");
                string response = Console.ReadLine();
                if (!response.StartsWith("y", StringComparison.OrdinalIgnoreCase))
                    return;
            }

            int added = 0;
            foreach (string l in encoderLanguages)
            {
                XPathNavigator appSettingsKey = cursor.SelectSingleNode("/configuration/appSettings");

                string name = "encoder_" + l;
                if (!encodersConfigured.Contains(name))
                {
                    Console.WriteLine("Adding the setting for {0} as {1}", name, highlighter);
                    appSettingsKey.AppendChild("<add key=\"" + name + "\" value=\"" + highlighter + "\" />");
                    ++added;
                }
            }

            webConfig.Save(file);

            if (upgrade)
                Console.WriteLine("Upgraded {0} file handlers.", upgraded);
            Console.WriteLine("Added {0} file handlers.", added);
        }

        /// <summary>
        /// Just a wrapper over InstallDriver. Does nothing other than pause output before existing.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            try
            {
                InstallDriver();
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.Read();
            }
        }


    }
}
