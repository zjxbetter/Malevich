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

namespace Malevich.Extensions
{
    /// <summary>
    /// Line encoder interface. Implement it to supply alternative HTML encoding
    /// (which may implement such features as syntax highlighting).
    /// </summary>
    public interface ILineEncoder : IDisposable
    {
        /// <summary>
        /// Processes one line of code. This should be done in the following effective order:
        /// (1) Tabs substituted.
        /// (2) Line broken into chunks of no more than specified maximum lengths
        /// (3) The substrings must be HTML-encoded and reconcatenated with "<br>" as a chunk separator
        /// 
        /// Note that if you do color-coding, take care that the colors you use do not match the background
        /// colors for the rendered page, or parts of the strings may be rendered invisible.
        /// 
        /// Please take a look at the provided samples before attempting to write your own syntax highlighter:
        /// this job is harder than it appears.
        /// </summary>
        /// <param name="line">Line to encode. </param>
        /// <returns>Encoded string.</returns>
        string EncodeLine(string line, int maxLineLength, string tabSubstitute);

        /// <summary>
        /// Returns a stream that generates the CSS used by the encoder.
        /// </summary>
        /// <returns>Text stream for CSS used by the encoder. If null is returned, it is assumed that the encoder
        /// does not use any CSS classes, and this step is simply ignored. </returns>
        TextReader GetEncoderCssStream();
    }

    /// <summary>
    /// Gets a factory object for the line encoder interfaces. One encoder may support multiple languages.
    /// </summary>
    public interface ILineEncoderFactory
    {
        /// <summary>
        /// Gets a line encoder for a given file extension.
        /// </summary>
        /// <param name="fileExtension">File extension for which the encoder is returned. </param>
        /// <returns> Line encoder interface. </returns>
        ILineEncoder GetLineEncoder(string fileExtension);
    }
}
