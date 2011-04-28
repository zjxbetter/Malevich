//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System;
using System.Text;

/// <summary>
/// Thrown when an encoder reaches invalid state.
/// </summary>
public class InvalidEncoderState : Exception
{
    /// <summary>
    /// Trivial constructor.
    /// </summary>
    /// <param name="status"></param>
    public InvalidEncoderState(string status)
        : base(status)
    {
    }
}

/// <summary>
/// Types of quote string.
/// </summary>
enum QuoteType
{
    None,
    Single,
    Double,
    Verbatim
}

/// <summary>
/// Common helper functions.
/// </summary>
public static class EncoderCommon
{
    /// <summary>
    /// Breaks the string up in maxLineWidth-sized segments while accounting for tags
    /// and HTML escapes. "<br>" elements are inserted at the boundary of the substring.
    /// </summary>
    /// <param name="encoded">The string buffer to process.</param>
    /// <param name="maxLineWidth">The maximum length of string before it gets broken up. </param>
    /// <param name="s">Original string for error reporting. </param>
    /// <returns>String broken up in maxLineWidth segments.</returns>
    public static string StringBufferToChunks(StringBuilder encoded, int maxLineWidth, string s)
    {
        StringBuilder result = new StringBuilder();
        bool inTag = false;
        bool inEsc = false;
        int nChar = 0;
        for (int i = 0; i < encoded.Length; ++i)
        {
            if (encoded[i] == '<')
            {
                if (inTag)
                    throw new InvalidEncoderState("unexpected '<' at position " + i + " in " + s);
                inTag = true;
            }
            else if (encoded[i] == '>')
            {
                if (!inTag)
                    throw new InvalidEncoderState("unexpected '>' at position " + i + " in " + s);
                inTag = false;
                --nChar; // Will be added back below.
            }
            else if (encoded[i] == '&')
            {
                if (inEsc)
                    throw new InvalidEncoderState("unexpected '&' at position " + i + " in " + s);
                inEsc = true;
            }
            else if (encoded[i] == ';' && inEsc)
            {
                inEsc = false;
            }

            result.Append(encoded[i]);

            if (inTag || inEsc)
                continue;

            ++nChar;
            if (nChar == maxLineWidth)
            {
                result.Append("<br>");
                nChar = 0;
            }
        }

        return result.ToString();
    }
}