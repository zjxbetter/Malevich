//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Jerry Ju for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Diagnostics;
using System.IO;

namespace Malevich.Util
{
    /// <summary>
    /// Add, update or delete a repeated task with Windows Task Scheduler.
    /// </summary>
    public class TaskScheduler
    {
        private readonly string exeFilePath = Path.Combine(Environment.SystemDirectory, "SCHTASKS.exe");

        /// <summary>
        /// Get or set repetition interval in minutes.
        /// NOTICE: set Interval to zero to delete an existing task.
        /// </summary>
        public int Interval { get; set; }

        /// <summary>
        /// Get or set task name.
        /// </summary>
        public string TaskName { get; set; }

        /// <summary>
        /// Get or set the executable file path to run.
        /// </summary>
        public string TaskPath { get; set; }

        /// <summary>
        /// Add, update or delete a repeated task.
        /// </summary>
        /// <param name="userName"> User to run as. If null, current user. </param>
        /// <param name="password"> Password of this user. If null, ask. </param>
        /// <returns>Whether the operation is successful.</returns>
        public bool SetTask(string userName, string password)
        {
            if (!File.Exists(exeFilePath))
            {
                Console.WriteLine("Error: cannot find {0}.", exeFilePath);
                return false;
            }

            if (userName == null)
            {
                if (!string.IsNullOrEmpty(Environment.UserDomainName))
                    userName = Environment.UserDomainName + "\\" + Environment.UserName;
                else
                    userName = Environment.UserName;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exeFilePath;
            startInfo.ErrorDialog = false;
            startInfo.LoadUserProfile = true;
            startInfo.UseShellExecute = false;

            if (Interval > 0) //set or update the task
            {
                string args = string.Format(
                    "/Create /SC MINUTE /MO {0} /TN {1} /TR \"\\\"{2}\\\"\" /F /RL HIGHEST /RU \"{3}\" /RP",
                    Interval, TaskName, TaskPath, userName);
                if (password != null)
                    args = args + " " + password;
                startInfo.Arguments = args;
            }
            else  // remove existing task
            {
                startInfo.Arguments = string.Format(@"/DELETE /TN {0} /F", TaskName);
            }

            Process p = Process.Start(startInfo);
            p.WaitForExit();

            if (p.ExitCode != 0)
                Console.WriteLine(
                    "If you got an \"Access is denied\" error, please re-run the command as an Administrator.");

            return p.ExitCode == 0;
        }
    }
}
