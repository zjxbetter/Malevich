//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

using Malevich.Extensions;

/// <summary>
/// Implements a very primitive syntax highlighter which emulates Visual Studio HTML/XML syntax coloring.
/// 
/// This is just a sample code, and is not a part of Malevich installation. Nevertheless, it is fairly
/// complex. If you develop your own based on this sample, please consider running the test to make
/// sure corner cases are handled. The test is SampleHighlighterTest
/// 
/// Important note: this code is ridiculously slow. It needs to be rewritten using regular expressions.
/// </summary>
public class MarkupLineEncoder : ILineEncoder
{
    /// <summary>
    /// Font for comments.
    /// </summary>
    private const string SpanBrackets = "<span class=\"CssHlXBkts\">";

    /// <summary>
    /// Font for comments.
    /// </summary>
    private const string SpanComments = "<span class=\"CssHlXComment\">";

    /// <summary>
    /// Font for CDATA segment.
    /// </summary>
    private const string SpanCDATA = "<span class=\"CssHlXCdata\">";

    /// <summary>
    /// Font for text constants.
    /// </summary>
    private const string SpanText = "<span class=\"CssHlXText\">";

    /// <summary>
    /// Font for tags.
    /// </summary>
    private const string SpanTag = "<span class=\"CssHlXTag\">";

    /// <summary>
    /// Font for attributes.
    /// </summary>
    private const string SpanAttr = "<span class=\"CssHlXAttr\">";

    /// <summary>
    /// End of color-coded segment.
    /// </summary>
    private const string SpanEnd = "</span>";

    /// <summary>
    /// Standard definitions
    /// </summary>
    private const string MarkupStyles =
        ".CssHlXText\n" +
        "{\n" +
        "    color: #0000ff;\n" +
        "}\n" +
        ".CssHlXBkts\n" +
        "{\n" +
        "    color: #0000ff;\n" +
        "}\n" +
        ".CssHlXAttr\n" +
        "{\n" +
        "    color: #cc0000;\n" +
        "}\n" +
        ".CssHlXTag\n" +
        "{\n" +
        "    color: #990000;\n" +
        "}\n" +
        ".CssHlXCdata\n" +
        "{\n" +
        "    color: #999999;\n" +
        "}\n" +
        ".CssHlXComment\n" +
        "{\n" +
        "    color: #009900;\n" +
        "}\n";

    /// <summary>
    /// Whether we are inside a quoted string, and what types of quotes are used.
    /// </summary>
    QuoteType quotes = QuoteType.None;

    /// <summary>
    /// Previous multiline comment has not yet terminated.
    /// </summary>
    private bool inComments = false;

    /// <summary>
    /// Previous CDATA segment has not yet termindated.
    /// </summary>
    private bool inCDATA = false;

    /// <summary>
    /// Previous tag has not terminated.
    /// </summary>
    private bool inTag = false;

    /// <summary>
    /// Instantiates the MarkupLineEncoder.
    /// </summary>
    public MarkupLineEncoder()
    {
    }

    /// <summary>
    /// Encodes the string. Unlike standard HtmlEncode, our custom version preserves spaces correctly.
    /// Also, converts tabs to "\t", and breaks lines in chunks of maxLineWidth non-breaking segments.
    /// </summary>
    /// <param name="s"> The string to encode. </param>
    /// <param name="maxLineWidth"> The maximum width. </param>
    /// <param name="tab"> Text string to replace tabs with. </param>
    /// <returns> The string which can be safely displayed in HTML. </returns>
    public string EncodeLine(string s, int maxLineWidth, string tab)
    {
        if (s == null)
            return null;

        s = HttpUtility.HtmlEncode(s.Replace("\t", tab)).Replace(" ", "&nbsp;");

        StringBuilder encoded = new StringBuilder();

        if (inComments)
            encoded.Append(SpanComments);

        if (inTag)
            encoded.Append(SpanAttr);

        if (inCDATA)
            encoded.Append(SpanCDATA);

        if (quotes != QuoteType.None)
            encoded.Append(SpanText);

        for (int i = 0; i < s.Length; ++i)
        {
            if (quotes == QuoteType.Double)
            {
                int endQuotes = s.IndexOf("&quot;", i);
                if (endQuotes == -1)
                {
                    // The entire string is in quotes. The tag will close after the loop.
                    encoded.Append(s.Substring(i));
                    break;
                }

                encoded.Append(s.Substring(i, endQuotes + 6 - i));
                quotes = QuoteType.None;
                encoded.Append(SpanEnd);

                i = endQuotes + 5;
                continue;
            }

            if (quotes == QuoteType.Single)
            {
                int endQuotes = s.IndexOf('\'', i);
                if (endQuotes == -1)
                {
                    // The entire string is in quotes. The tag will close after the loop.
                    encoded.Append(s.Substring(i));
                    break;
                }

                encoded.Append(s.Substring(i, endQuotes + 1 - i));
                quotes = QuoteType.None;
                encoded.Append(SpanEnd);

                i = endQuotes;
                continue;
            }

            if (inComments)
            {
                int endComments = s.IndexOf("--&gt;", i);
                if (endComments == -1)
                {
                    // The entire string is in comments. The tag will close after the loop.
                    encoded.Append(s.Substring(i));
                    break;
                }

                inComments = false;

                encoded.Append(s.Substring(i, endComments - i));
                encoded.Append(SpanEnd + SpanBrackets + "--&gt;" + SpanEnd);
                i = endComments + 5;
                continue;
            }

            if (inCDATA)
            {
                int endCDATA = s.IndexOf("]]&gt;", i);
                if (endCDATA == -1)
                {
                    // The entire string is in cdata. The tag will close after the loop.
                    encoded.Append(s.Substring(i));
                    break;
                }

                inCDATA = false;

                encoded.Append(s.Substring(i, endCDATA - i));
                encoded.Append(SpanEnd + SpanBrackets + "]]&gt;" + SpanEnd);
                i = endCDATA + 5;
                continue;
            }

            if (inTag)
            {
                if (s[i] == '\'')
                {
                    encoded.Append(SpanText + "'");
                    quotes = QuoteType.Single;
                    continue;
                }

                if (s[i] == '=')
                {
                    encoded.Append(SpanBrackets + '=' + SpanEnd);
                    continue;
                }

                if (s[i] == '/')
                {
                    if (i < s.Length - 4 && s.Substring(i, 5).Equals("/&gt;"))
                    {
                        encoded.Append(SpanEnd + SpanBrackets + "/&gt;" + SpanEnd);
                        inTag = false;
                        i += 4;
                        continue;
                    }
                }

                if (s[i] == '&')
                {
                    if (i < s.Length - 5 && s.Substring(i, 6).Equals("&quot;"))
                    {
                        encoded.Append(SpanText + "&quot;");
                        quotes = QuoteType.Double;
                        i += 5;
                        continue;
                    }

                    if (i < s.Length - 3 && s.Substring(i, 4).Equals("&gt;"))
                    {
                        encoded.Append(SpanEnd + SpanBrackets + "&gt;" + SpanEnd);
                        inTag = false;
                        i += 3;
                        continue;
                    }
                }

                encoded.Append(s[i]);
                continue;
            }
            
            if (s[i] == '&')
            {
                if (i < s.Length - 11 && s.Substring(i, 12).Equals("&lt;![CDATA["))
                {
                    encoded.Append(SpanBrackets + "&lt;![CDATA[" + SpanEnd + SpanCDATA);
                    inCDATA = true;
                    i += 11;

                    continue;
                }

                if (i < s.Length - 6 && s.Substring(i, 7).Equals("&lt;!--"))
                {
                    encoded.Append(SpanBrackets + "&lt;!--" + SpanEnd + SpanComments);
                    inComments = true;
                    i += 6;

                    continue;
                }

                if (i < s.Length - 4 && s.Substring(i, 5).Equals("&lt;/"))
                {
                    i += 5;
                    int j = i;

                    while (i < s.Length && (char.IsLetterOrDigit(s, i) || s[i] == '_' || s[i] == ':'))
                        ++i;

                    encoded.Append(SpanBrackets + "&lt;/" + SpanEnd + SpanTag + s.Substring(j, i - j) + SpanEnd +
                        SpanAttr);

                    --i;

                    inTag = true;

                    continue;
                }

                if (i < s.Length - 3 && s.Substring(i, 4).Equals("&lt;"))
                {
                    i += 4;
                    int j = i;

                    while (i < s.Length && (char.IsLetterOrDigit(s, i) || s[i] == '_' || s[i] == ':'))
                        ++i;

                    encoded.Append(SpanBrackets + "&lt;" + SpanEnd + SpanTag + s.Substring(j, i - j) + SpanEnd +
                        SpanAttr);

                    --i;

                    inTag = true;

                    continue;
                }
            }

            encoded.Append(s[i]);
        }

        if (inComments || inCDATA || inTag || quotes != QuoteType.None)
            encoded.Append(SpanEnd);

        return EncoderCommon.StringBufferToChunks(encoded, maxLineWidth, s);
    }

    /// <summary>
    /// Returns the CSS used by the encoder.
    /// </summary>
    /// <returns></returns>
    public TextReader GetEncoderCssStream()
    {
        return new StringReader(MarkupStyles);
    }

    /// <summary>
    /// Does nothing, just satisfies ILineEncoder interface.
    /// </summary>
    public void Dispose()
    {
    }
}
