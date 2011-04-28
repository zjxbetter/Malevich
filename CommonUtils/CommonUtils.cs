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
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;

using Malevich.Util;
using Malevich.Extensions;

namespace Malevich.Util
{
    /// <summary>
    /// Implements a bunch of useful 
    /// </summary>
    public static class CommonUtils
    {
        /// <summary>
        /// Delegate type for async string reader.
        /// </summary>
        /// <returns></returns>
        private delegate string StringDelegate();

        /// <summary>
        /// Read the process output.
        /// </summary>
        /// <param name="proc"> The process class. This should be already started. </param>
        /// <param name="eatFirstLine"> If true, the first line of standard out is ignored. </param>
        /// <param name="errorMessage"> Error message. </param>
        /// <returns> Standard output. </returns>
        public static string ReadProcessOutput(Process proc, bool eatFirstLine, out string errorMessage)
        {
            errorMessage = null;
            StringDelegate outputStreamAsyncReader = new StringDelegate(proc.StandardOutput.ReadToEnd);
            StringDelegate errorStreamAsyncReader = new StringDelegate(proc.StandardError.ReadToEnd);
            IAsyncResult outAsyncResult = outputStreamAsyncReader.BeginInvoke(null, null);
            IAsyncResult errAsyncResult = errorStreamAsyncReader.BeginInvoke(null, null);

            // WaitHandle.WaitAll does not work in STA.
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                while (!(outAsyncResult.IsCompleted && errAsyncResult.IsCompleted))
                    Thread.Sleep(500);
            }
            else
            {
                WaitHandle[] handles = { outAsyncResult.AsyncWaitHandle, errAsyncResult.AsyncWaitHandle };
                if (!WaitHandle.WaitAll(handles))
                {
                    Console.WriteLine("Execution aborted!");
                    return null;
                }
            }

            string results = outputStreamAsyncReader.EndInvoke(outAsyncResult);
            errorMessage = errorStreamAsyncReader.EndInvoke(errAsyncResult);

            proc.WaitForExit();

            if (eatFirstLine)
            {
                int index = results.IndexOf("\r\n");
                if (index != -1)
                    results = results.Substring(index + 2);
            }

            return results;
        }

        /// <summary>
        /// Maps verdict code to string.
        /// </summary>
        /// <param name="status"> Review verdict. </param>
        /// <returns> String representation of status. </returns>
        public static string ReviewStatusToString(int status)
        {
            switch (status)
            {
                case 0: return "Needs work";
                case 1: return "LGTM with minor tweaks";
                case 2: return "LGTM";
            }
            return "Non-scoring comment";
        }
    }

    /// <summary>
    /// Creates and manages automatic deletion of a temporary file.
    /// </summary>
    public class TempFile : IDisposable
    {
        private FileInfo _file;

        /// <summary>
        /// Creates a zero-length temporary file. Deletes the file when finalized.
        /// </summary>
        public TempFile()
        {
            _file = new FileInfo(Path.GetTempFileName());
            ShouldDelete = true;
        }

        /// <summary>
        /// Manages the given file name as a temporary file. Deletes the file
        /// when finalized.
        /// </summary>
        /// <param name="fileName">Filename to manage.</param>
        TempFile(string fileName)
        {
            _file = new FileInfo(fileName);
            ShouldDelete = true;
        }

        /// <summary>
        /// Rename the temporary file.
        /// </summary>
        /// <param name="newFileName">The new file name.</param>
        public void Rename(string newFileName)
        {
            _file.MoveTo(newFileName);
        }

        /// <summary>
        /// Creates a temporary file with the given extension.
        /// </summary>
        /// <param name="extension">Extension for the new temporary file.</param>
        /// <returns></returns>
        public static TempFile CreateNewForExtension(string extension)
        {
            if (extension.IsNullOrEmpty())
                throw new ArgumentException("Invalid argument: extension.");

            var tmpFile = new TempFile();
            tmpFile.Rename(tmpFile.FullName + extension);
            return tmpFile;
        }

        /// <summary>
        /// Manages the given file name as a temporary file. Deletes the file
        /// when finalized.
        /// </summary>
        /// <param name="fileName">Filename to manage.</param>
        /// <returns></returns>
        public static TempFile CreateFromExisting(string fileName)
        {
            return new TempFile(fileName);
        }

        /// <summary>
        /// The full path and name of the temporary file.
        /// </summary>
        public string FullName
        {
            get { return _file.FullName; }
        }

        /// <summary>
        /// Used to set whether or not the file should be deleted upon
        /// finalization.
        /// </summary>
        private bool _shouldDelete;
        public bool ShouldDelete
        {
            get { return _shouldDelete; }
            set { _shouldDelete = value; }
        }

        /// <summary>
        /// Called upon finalization, will delete the file if ShouldDelete
        /// is set to true.
        /// </summary>
        void IDisposable.Dispose()
        {
            if (ShouldDelete && _file.Exists)
                _file.Delete();
        }
    }
}

namespace Malevich.Extensions
{
    /// <summary>
    /// Provides a case-insensitive comparer for use in sorting.
    /// </summary>
    public class CaseInsensitiveComparer : IComparer<string>
    {
        /// <summary>
        /// Compares two strings in a case-insensitive manner.
        /// </summary>
        /// <param name="x">The first string.</param>
        /// <param name="y">The second string.</param>
        int IComparer<string>.Compare(string x, string y)
        {
            return string.Compare(x, y, true);
        }
    }

    /// <summary>
    /// Provides extension methods to simplify common string operations.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Performs a ordinal case-insensitive equality test.
        /// </summary>
        /// <param name="lhs"> The "this" parameter </param>
        /// <param name="prefix"> The string to test for equality. </param>
        /// <returns> true if strings are equal, false otherwise. </returns>
        public static bool EqualsIgnoreCase(this String lhs, String rhs)
        {
            return lhs.Equals(rhs, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Performs a ordinal case-insensitive test to see if the target
        /// string starts with given string.
        /// </summary>
        /// <param name="lhs"> The "this" parameter </param>
        /// <param name="prefix"> The prefix string. </param>
        /// <returns> true if 'this' starts with 'prefix'; false otherwise. </returns>
        public static bool StartsWithIgnoreCase(this string lhs, string prefix)
        {
            return lhs.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Performs a ordinal case-insensitive test to see if the target
        /// string ends with given string.
        /// </summary>
        /// <param name="lhs"> The "this" parameter </param>
        /// <param name="postfix"> The postfix string. </param>
        /// <returns> true if 'this' ends with 'postfix'; false otherwise. </returns>
        public static bool EndsWithIgnoreCase(this string lhs, string postfix)
        {
            return lhs.EndsWith(postfix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Used to determine if a string is null or empty.
        /// </summary>
        /// <returns>
        /// Returns true if string reference null or is the empty string; returns false otherwise.
        /// </returns>
        public static bool IsNullOrEmpty(this string _this)
        {
            return string.IsNullOrEmpty(_this);
        }

        public static string ToLowerCultureInvariant(this string _this)
        {
            return _this.ToLower(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Provides extension methods to simplify common List operations.
    /// </summary>
    public static class ListExtensions
    {
        /// <summary>
        /// Returns true if _this is null or has an element count of zero.
        /// </summary>
        public static bool IsEmpty<T>(this List<T> _this)
        {
            return _this.Count() == 0;
        }

        /// <summary>
        /// Returns true if _this is null or has an element count of zero.
        /// </summary>
        public static bool IsEmpty<T>(this IList<T> _this)
        {
            return _this.Count() == 0;
        }

        /// <summary>
        /// Returns true if _this is null or has an element count of zero.
        /// </summary>
        public static bool IsNullOrEmpty<T>(this List<T> _this)
        {
            return _this == null || _this.IsEmpty();
        }

        /// <summary>
        /// Returns true if _this is null or has an element count of zero.
        /// </summary>
        public static bool IsNullOrEmpty<T>(this IList<T> _this)
        {
            return _this == null || _this.IsEmpty();
        }

        /// <summary>
        /// Returns the index of the first element for which predicate returns true;
        /// otherwise returns this.Count().
        /// </summary>
        public static int IndexOfFirst<T>(this IList<T> _this, Func<T, bool> predicate)
        {
            return _this.IndexOfFirst(predicate, 0);
        }

        /// <summary>
        /// Returns the index of the first element for which predicate returns true;
        /// otherwise returns this.Count().
        /// </summary>
        /// <param name="startIndex">The index at which to start the search.</param>
        public static int IndexOfFirst<T>(this IList<T> _this, Func<T, bool> predicate, int startIndex)
        {
            int i = startIndex;
            for (; i < _this.Count(); ++i)
                if (predicate(_this[i]))
                    return i;
            return i;
        }
    }
}

