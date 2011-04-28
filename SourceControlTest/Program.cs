//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using SourceControl;

namespace SourceControlTest
{
    /// <summary>
    /// Wrapper for a testable object that replaces client output with a pre-determined set of string.
    /// </summary>
    class TestPerforceClient : PerforceBase
    {
        /// <summary>
        /// The hashtable that holds the strings for the client output for given command lines.
        /// </summary>
        private Hashtable ClientOutput;

        /// <summary>
        /// Trivial constructor.
        /// </summary>
        /// <param name="clientOutput"> The dictionary with the outputs for specific client utility command lines.
        /// </param>
        public TestPerforceClient(Hashtable clientOutput)
            : base("", new Perforce("testperforceserver:1666", "client", "user", "password"))
        {
            ClientOutput = clientOutput;
        }

        /// <summary>
        /// Overrides client utility to simulate client runs.
        /// Gives predetermined output for pre-determined command lines.
        /// </summary>
        /// <param name="commandLine"></param>
        /// <param name="eatFirstLine"></param>
        /// <returns></returns>
        override protected string RunClient(string commandLine, bool eatFirstLine)
        {
            Console.WriteLine(commandLine);
            return (string)ClientOutput[commandLine];
        }
    }

    class Program
    {
        static void TestPerforceChangeDescriptionParsing()
        {
            Hashtable clientOutput = new Hashtable();
            clientOutput["describe 5599"] =
                "Change 5599 by REDMOND\\sergeyso@SERGEYSOD830-msg_dev-1 on 2009/01/09 15:54:09 *pending*\n\n" +
                "\tUpdating code review tool. Added help on -?, /h and empty command line.\n\n" +
                "Affected files ...\n\n" +
                "... //depot/msg_dev/tools/review/CodeReviewDataModel.dll#2 edit\n" +
                "... //depot/msg_dev/tools/review/CodeReviewDataModel.pdb#2 edit\n" +
                "... //depot/msg_dev/tools/review/CommonUtils.dll#2 edit\n" +
                "... //depot/msg_dev/tools/review/CommonUtils.pdb#2 edit\n" +
                "... //depot/msg_dev/tools/review/review.exe#2 edit\n" +
                "... //depot/msg_dev/tools/review/review.pdb#2 edit\n" +
                "... //depot/msg_dev/tools/review/SourceControl.dll#2 edit\n" +
                "... //depot/msg_dev/tools/review/SourceControl.pdb#2 edit\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/CodeReviewDataModel.dll\""] =
                "//depot/msg_dev/tools/review/CodeReviewDataModel.dll#2 - edit change 5599 (binary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/CodeReviewDataModel.pdb\""] =
                "//depot/msg_dev/tools/review/CodeReviewDataModel.pdb#2 - edit change 5599 (binary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/CommonUtils.dll\""] =
                "//depot/msg_dev/tools/review/CommonUtils.dll#2 - edit change 5599 (binary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/CommonUtils.pdb\""] =
                "//depot/msg_dev/tools/review/CommonUtils.pdb#2 - edit change 5599 (binary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/review.exe\""] =
                "//depot/msg_dev/tools/review/review.exe#2 - edit change 5599 (xbinary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/review.pdb\""] =
                "//depot/msg_dev/tools/review/review.pdb#2 - edit change 5599 (binary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/SourceControl.dll\""] =
                "//depot/msg_dev/tools/review/SourceControl.dll#2 - edit change 5599 (binary)\n\n";
            clientOutput["opened \"//depot/msg_dev/tools/review/SourceControl.pdb\""] =
                "//depot/msg_dev/tools/review/SourceControl.pdb#2 - edit change 5599 (binary)\n\n";
            clientOutput["where \"//depot/msg_dev/tools/review/CodeReviewDataModel.dll\""] =
                "//depot/msg_dev/tools/review/CodeReviewDataModel.dll " +
                "//SERGEYSOD830-msg_dev-1/tools/review/CodeReviewDataModel.dll " +
                "C:\\src\\tools\\review\\CodeReviewDataModel.dll\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/CodeReviewDataModel.pdb\""] =
                "//depot/msg_dev/tools/review/CodeReviewDataModel.pdb " +
                "//SERGEYSOD830-msg_dev-1/tools/review/CodeReviewDataModel.pdb " +
                "C:\\src\\tools\\review\\CodeReviewDataModel.pdb\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/CommonUtils.dll\""] =
                "//depot/msg_dev/tools/review/CommonUtils.dll //SERGEYSOD830-msg_dev-1/tools/review/CommonUtils.dll " +
                "C:\\src\\tools\\review\\CommonUtils.dll\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/CommonUtils.pdb\""] =
                "//depot/msg_dev/tools/review/CommonUtils.pdb //SERGEYSOD830-msg_dev-1/tools/review/CommonUtils.pdb " +
                "C:\\src\\tools\\review\\CommonUtils.pdb\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/review.exe\""] =
                "//depot/msg_dev/tools/review/review.exe //SERGEYSOD830-msg_dev-1/tools/review/review.exe " + 
                "C:\\src\\tools\\review\\review.exe\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/review.pdb\""] =
                "//depot/msg_dev/tools/review/review.pdb //SERGEYSOD830-msg_dev-1/tools/review/review.pdb " + 
                "C:\\src\\tools\\review\\review.pdb\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/SourceControl.dll\""] =
                "//depot/msg_dev/tools/review/SourceControl.dll " +
                "//SERGEYSOD830-msg_dev-1/tools/review/SourceControl.dll C:\\src\\tools\\review\\SourceControl.dll\r\n";
            clientOutput["where \"//depot/msg_dev/tools/review/SourceControl.pdb\""] =
                "//depot/msg_dev/tools/review/SourceControl.pdb " +
                "//SERGEYSOD830-msg_dev-1/tools/review/SourceControl.pdb C:\\src\\tools\\review\\SourceControl.pdb\r\n";

            TestPerforceClient client = new TestPerforceClient(clientOutput);
            Change change = client.GetChange("5599", false);
            Assert.AreEqual("5599", change.ChangeListId);
            Assert.AreEqual("Updating code review tool. Added help on -?, /h and empty command line.\n",
                change.Description);
            Assert.AreEqual(change.Files.Count(), 8);
            foreach (ChangeFile f in change.Files)
            {
                Assert.IsFalse(f.IsText);
            }
        }

        static void Main(string[] args)
        {
            TestPerforceChangeDescriptionParsing();
        }
    }
}
