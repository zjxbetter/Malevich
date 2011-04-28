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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using Malevich.Extensions;

namespace SampleSyntaxHighlighterTest
{
    /// <summary>
    /// Malevich sample highlighter test.
    /// </summary>
    class Program
    {
        /// <summary>
        /// Prints out a listing for a file.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="factory"></param>
        /// <param name="encodedFile"></param>
        private static void ListFile(string fileName, LineEncoderFactory factory, StreamWriter encodedFile)
        {
            string ext = Path.GetExtension(fileName).Substring(1);
            string name = Path.GetFileName(fileName);
            encodedFile.WriteLine("<tr><td>&nbsp;</td></tr>");
            encodedFile.WriteLine("<tr><td>&nbsp;</td></tr>");
            encodedFile.WriteLine("<tr><td>{0}</td></tr>", name);
            encodedFile.WriteLine("<tr><td>&nbsp;</td></tr>");

            ILineEncoder encoder = factory.GetLineEncoder(ext);
            StreamReader r = new StreamReader(fileName);
            for (; ; )
            {
                string s = r.ReadLine();
                if (s == null)
                    break;

                if ("".Equals(s))
                {
                    encodedFile.WriteLine("<tr><td>&nbsp;</td></tr>");
                    continue;
                }

                encodedFile.WriteLine("<tr><td>{0}</td></tr>", encoder.EncodeLine(s, 80, "\\t"));
            }
            r.Close();
        }

        /// <summary>
        /// Test correctness of various encoders.
        /// </summary>
        /// <param name="encodedFile"></param>
        /// <param name="factory"></param>
        private static void CorrectnessTest(StreamWriter encodedFile, LineEncoderFactory factory)
        {
            DateTime now = DateTime.Now;

            string testFileName = "test.txt";
            string baseDir = "";
            for (int i = 0; i < 3; ++i)
            {
                if (File.Exists(testFileName))
                    break;

                testFileName = "..\\" + testFileName;
                baseDir = "..\\" + baseDir;
            }

            if (!File.Exists(testFileName))
            {
                Console.Error.WriteLine(
                    "Could not find file text.txt. It should be either in the same or in a parent directory.");
                return;
            }

            baseDir = "..\\" + baseDir;

            encodedFile.Write("<h1>Simple correctness test.</h1>");
            encodedFile.WriteLine(
                "<table style=\"table-layout: fixed;border-collapse: collapse; font-family:consolas\">");

            StreamReader testFile = new StreamReader(testFileName);
            StringBuilder css = new StringBuilder();
            ILineEncoder encoder = null;

            for (; ; )
            {
                string s = testFile.ReadLine();
                if (s == null)
                    break;

                if (s.StartsWith("start new file "))
                {
                    string ext = s.Substring(15);
                    Console.WriteLine("Starting {0} @ {1}", ext, DateTime.Now - now);
                    encoder = factory.GetLineEncoder(ext);
                    Console.WriteLine("Got encoder for {0} @ {1}", ext, DateTime.Now - now);
                    css.Append(encoder.GetEncoderCssStream().ReadToEnd());
                    continue;
                }

                if ("".Equals(s))
                {
                    encodedFile.WriteLine("<tr><td>&nbsp;</td></tr>");
                    continue;
                }

                if (encoder != null)
                    encodedFile.WriteLine("<tr><td>{0}</td></tr>", encoder.EncodeLine(s, 80, "\\t"));
            }

            ListFile(baseDir + "notifier\\MailTemplates.cs", factory, encodedFile);
            ListFile(baseDir + "notifier\\Notifier.csproj", factory, encodedFile);
            ListFile(baseDir + "notifier\\Iteration.html", factory, encodedFile);
            ListFile(baseDir +
                "Database\\Schema Objects\\Schemas\\dbo\\Programmability\\Stored Procedures\\AddComment.proc.sql",
                factory, encodedFile);

            Console.WriteLine("All done @ {0}", DateTime.Now - now);

            if (encoder != null)
                encoder.Dispose();

            encodedFile.WriteLine("</table><style>" + css.ToString() + "</style>");
            testFile.Close();
        }

        /// <summary>
        /// Recursively adds all files in a directory into one really big file.
        /// </summary>
        /// <param name="file"></param>
        /// <param name="dir"></param>
        private static void RecursivelyAddFileContent(StreamWriter file, string dir, string ext)
        {
            string[] files = Directory.GetFiles(dir, "*." + ext);
            foreach (string f in files)
            {
                StreamReader r = new StreamReader(f);
                for (; ; )
                {
                    string line = r.ReadLine();
                    if (line == null)
                        break;

                    file.WriteLine("{0}", line);
                }

                file.WriteLine();
                file.WriteLine();

                r.Close();
            }

            string[] dirs = Directory.GetDirectories(dir);
            foreach (string d in dirs)
                RecursivelyAddFileContent(file, d, ext);
        }

        /// <summary>
        /// Combines all malevich sources with a particular extension into a single very big file, and runs
        /// a performance test on it.
        /// </summary>
        /// <param name="ext"></param>
        private static void TestBigFile(string baseDir, string ext, StreamWriter encodedFile,
            LineEncoderFactory factory)
        {
            Console.WriteLine("Testing performance of the encoder for {0}", ext);

            string bigFileNameTmp = "ReallyBigTest." + ext + ".tmp";
            if (File.Exists(bigFileNameTmp))
                File.Delete(bigFileNameTmp);

            StreamWriter bigFile = new StreamWriter(bigFileNameTmp);
            RecursivelyAddFileContent(bigFile, baseDir, ext);
            bigFile.Close();

            ILineEncoder encoder = factory.GetLineEncoder(ext);
            StreamReader r = new StreamReader(bigFileNameTmp);
            int nLines = 0;
            DateTime start = DateTime.Now;
            for (; ; )
            {
                string s = r.ReadLine();
                if (s == null)
                    break;

                if ("".Equals(s))
                    continue;

                ++nLines;
                encoder.EncodeLine(s, 80, "\\t");
            }

            double ms = (DateTime.Now - start).TotalMilliseconds;
            encodedFile.WriteLine("<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td></tr>",
                ext, nLines, ms.ToString(), nLines == 0 ? 0.0 : (ms / nLines));

            r.Close();

            File.Delete(bigFileNameTmp);
        }

        /// <summary>
        /// Runs performance tests on a few REALLY BIG Malevich files.
        /// </summary>
        /// <param name="encodedFile"></param>
        /// <param name="factory"></param>
        private static void PerformanceTest(StreamWriter encodedFile, LineEncoderFactory factory)
        {
            string malevichBaseDir = "";
            for (int i = 0; i < 5; ++i)
            {
                if (File.Exists(malevichBaseDir + "Malevich.sln"))
                    break;

                malevichBaseDir = "..\\" + malevichBaseDir;
            }

            if (!File.Exists(malevichBaseDir + "Malevich.sln"))
            {
                Console.Error.WriteLine("This test must be run from somewhere in Malevich tree!");
                return;
            }


            encodedFile.Write("<h1>Simple performance test.</h1><table>");
            encodedFile.WriteLine(
                "<tr><td>Extension</td><td>Total lines processed</td><td>Milliseconds</td>" +
                "<td>Seconds per KLOC</td></tr>");

            TestBigFile(malevichBaseDir, "csproj", encodedFile, factory);
            TestBigFile(malevichBaseDir, "cs", encodedFile, factory);
            TestBigFile(malevichBaseDir, "sql", encodedFile, factory);

            encodedFile.Write("</table>");
        }

        /// <summary>
        /// Does all the work.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            if (File.Exists("test.html"))
                File.Delete("test.html");

            StreamWriter encodedFile = new StreamWriter("test.html");
            encodedFile.WriteLine("<html><header><title>Test</title></header><body>");

            LineEncoderFactory factory = new LineEncoderFactory();

            PerformanceTest(encodedFile, factory);
            CorrectnessTest(encodedFile, factory);

            encodedFile.WriteLine("</body></html>");
            encodedFile.Close();

            Process p = new Process();
            p.EnableRaisingEvents = false;
            p.StartInfo.FileName = "test.html";
            p.Start();

            Console.WriteLine("An internet explorer windows should have opened with the results of the test.");
        }
    }
}
