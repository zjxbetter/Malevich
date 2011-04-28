//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

using Malevich.Extensions;

/// <summary>
/// Implements a very primitive syntax highlighter which emulates SQL Server Management Studio SQL syntax coloring.
/// 
/// This is just a sample code, and is not a part of Malevich installation. Nevertheless, it is fairly
/// complex. If you develop your own based on this sample, please consider running the test to make
/// sure corner cases are handled. The test is SampleHighlighterTest
/// </summary>
public class SqlLineEncoder : ILineEncoder
{
    /// <summary>
    /// Font for text constants.
    /// </summary>
    private const string SpanText = "<span class=\"CssHlSqlText\">";

    /// <summary>
    /// Font for comments.
    /// </summary>
    private const string SpanComments = "<span class=\"CssHlSqlComment\">";

    /// <summary>
    /// Font for keywords.
    /// </summary>
    private const string SpanKeyword = "<span class=\"CssHlSqlKeyword\">";

    /// <summary>
    /// Font for built-in variables (the ones preceeded by @@).
    /// </summary>
    private const string SpanBuiltin = "<span class=\"CssHlSqlBuiltin\">";

    /// <summary>
    /// End of color-coded segment.
    /// </summary>
    private const string SpanEnd = "</span>";

    /// <summary>
    /// Standard definitions
    /// </summary>
    private const string SqlStyles =
        ".CssHlSqlText\n" +
        "{\n" +
        "    color: #cc0000;\n" +
        "}\n" +
        ".CssHlSqlKeyword\n" +
        "{\n" +
        "    color: #0000cc;\n" +
        "}\n" +
        ".CssHlSqlBuiltin\n" +
        "{\n" +
        "    color: #ff0099;\n" +
        "}\n" +
        ".CssHlSqlComment\n" +
        "{\n" +
        "    color: #009900;\n" +
        "}\n";

    /// <summary>
    /// SQL keywords.
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] keywords =
    {
        "ADD",
        "ALL",
        "ALTER",
        "AND",
        "ANY",
        "ASC",
        "AS",
        "AUTHORIZATION",
        "BACKUP",
        "BEGIN",
        "BETWEEN",
        "BIGINT",
        "BINARY",
        "BIT",
        "BREAK",
        "BROWSE",
        "BULK",
        "BY",
        "CASCADE",
        "CASE",
        "CHAR",
        "CHECKPOINT",
        "CHECK",
        "CLOSE",
        "CLUSTERED",
        "COALESCE",
        "COLLATE",
        "COLUMN",
        "COMMIT",
        "COMPUTE",
        "CONSTRAINT",
        "CONTAINSTABLE",
        "CONTAINS",
        "CONTINUE",
        "CONVERT",
        "CREATE",
        "CROSS",
        "CURRENT_DATE",
        "CURRENT_TIMESTAMP",
        "CURRENT_TIME",
        "CURRENT_USER",
        "CURRENT",
        "CURSOR",
        "DATABASE",
        "DATETIME2",
        "DATETIMEOFFSET",
        "DATETIME",
        "DATE",
        "DBCC",
        "DEALLOCATE",
        "DECIMAL",
        "DECLARE",
        "DEFAULT",
        "DELETE",
        "DENY",
        "DESC",
        "DISK",
        "DISTINCT",
        "DISTRIBUTED",
        "DOUBLE",
        "DROP",
        "DUMP",
        "ELSE",
        "END",
        "ERRLVL",
        "ESCAPE",
        "EXCEPT",
        "EXECUTE",
        "EXEC",
        "EXISTS",
        "EXIT",
        "EXTERNAL",
        "FETCH",
        "FILE",
        "FILLFACTOR",
        "FLOAT",
        "FOREIGN",
        "FOR",
        "FREETEXTTABLE",
        "FREETEXT",
        "FROM",
        "FULL",
        "FUNCTION",
        "GOTO",
        "GRANT",
        "GROUP",
        "HAVING",
        "HIERARCHYID",
        "HOLDLOCK",
        "IDENTITY_INSERT",
        "IDENTITYCOL",
        "IDENTITY",
        "IF",
        "IMAGE",
        "INDEX",
        "INNER",
        "INSERT",
        "INTERSECT",
        "INTO",
        "INT",
        "IN",
        "IS",
        "JOIN",
        "KEY",
        "KILL",
        "LEFT",
        "LIKE",
        "LINENO",
        "LOAD",
        "MERGE",
        "MONEY",
        "NATIONAL",
        "NCHAR",
        "NOCHECK",
        "NONCLUSTERED",
        "NOT",
        "NTEXT",
        "NULLIF",
        "NULL",
        "NUMERIC",
        "NVARCHAR",
        "OFFSETS",
        "OFF",
        "OF",
        "ON",
        "OPENDATASOURCE",
        "OPENQUERY",
        "OPENROWSET",
        "OPENXML",
        "OPEN",
        "OPTION",
        "ORDER",
        "OR",
        "OUTER",
        "OVER",
        "PERCENT",
        "PIVOT",
        "PLAN",
        "PRECISION",
        "PRIMARY",
        "PRINT",
        "PROC",
        "PROCEDURE",
        "PUBLIC",
        "RAISERROR",
        "READTEXT",
        "READ",
        "REAL",
        "RECONFIGURE",
        "REFERENCES",
        "REPLICATION",
        "RESTORE",
        "RESTRICT",
        "RETURNS",
        "RETURN",
        "REVERT",
        "REVOKE",
        "RIGHT",
        "ROLLBACK",
        "ROWCOUNT",
        "ROWGUIDCOL",
        "RULE",
        "SAVE",
        "SCHEMA",
        "SECURITYAUDIT",
        "SELECT",
        "SESSION_USER",
        "SETUSER",
        "SET",
        "SHUTDOWN",
        "SMALLDATETIME",
        "SMALLINT",
        "SMALLMONEY",
        "SOME",
        "SQL_VARIANT",
        "STATISTICS",
        "SYSTEM_USER",
        "TABLESAMPLE",
        "TABLE",
        "TEXTSIZE",
        "TEXT",
        "THEN",
        "TIMESTAMP",
        "TIME",
        "TINYINT",
        "TOP",
        "TO",
        "TRANSACTION",
        "TRAN",
        "TRIGGER",
        "TRUNCATE",
        "TSEQUAL",
        "UNION",
        "UNIQUEIDENTIFIER",
        "UNIQUE",
        "UNPIVOT",
        "UPDATE",
        "UPDATETEXT",
        "USER",
        "USE",
        "VALUES",
        "VARBINARY",
        "VARCHAR",
        "VARYING",
        "VIEW",
        "WAITFOR",
        "WHEN",
        "WHERE",
        "WHILE",
        "WITH",
        "WRITETEXT",
        "XML"
    };

    /// <summary>
    /// Regular expression for the next SQL "event" - a quote, a comment, a keyword...
    /// </summary>
    private static Regex nextEvent;

    /// <summary>
    /// Contains all SQL keywords for quick lookup.
    /// </summary>
    private static HashSet<string> keywordDictionary;

    /// <summary>
    /// Previous multiline comment has not yet terminated.
    /// </summary>
    private bool inComments = false;

    /// <summary>
    /// Previous multiline string constant has not yet terminated.
    /// </summary>
    private QuoteType quotes = QuoteType.None;

    /// <summary>
    /// Trivial static constructor.
    /// </summary>
    static SqlLineEncoder()
    {
        keywordDictionary = new HashSet<string>(keywords);
        nextEvent = new Regex("'|&quot;|/\\*|--|@@|[a-zA-Z_]+", RegexOptions.Compiled);
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

        if (quotes != QuoteType.None && inComments)
            throw new InvalidEncoderState("in quotes and in comments - something is broken!");

        StringBuilder encoded = new StringBuilder();

        if (quotes != QuoteType.None)
            encoded.Append(SpanText);

        if (inComments)
            encoded.Append(SpanComments);

        int i = 0;
        while (i < s.Length)
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
                if ((endQuotes > 0 && s[endQuotes - 1] != '\\') ||
                    (endQuotes > 1 && s[endQuotes - 1] == '\\' && s[endQuotes - 2] == '\\'))
                {
                    // This is not an escaped quote.
                    quotes = QuoteType.None;
                    encoded.Append(SpanEnd);
                }

                i = endQuotes + 6;
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
                if ((endQuotes > 0 && s[endQuotes - 1] != '\\') ||
                    (endQuotes > 1 && s[endQuotes - 1] == '\\' && s[endQuotes - 2] == '\\'))
                {
                    // This is not an escaped quote.
                    quotes = QuoteType.None;
                    encoded.Append(SpanEnd);
                }

                i = endQuotes + 1;
                continue;
            }

            if (inComments)
            {
                int endComments = s.IndexOf("*/", i);
                if (endComments == -1)
                {
                    // The entire string is in comments. The tag will close after the loop.
                    encoded.Append(s.Substring(i));
                    break;
                }

                inComments = false;

                encoded.Append(s.Substring(i, endComments + 2 - i));
                encoded.Append(SpanEnd);
                i = endComments + 2;
                continue;
            }

            Match nextMatch = nextEvent.Match(s, i);
            if (!nextMatch.Success)
            {
                encoded.Append(s, i, s.Length - i);
                break;
            }

            if (i != nextMatch.Index)
            {
                encoded.Append(s, i, nextMatch.Index - i);
                i = nextMatch.Index;
            }

            string matched = nextMatch.Groups[0].Value;

            if (matched.Equals("'"))
            {
                encoded.Append(SpanText + "'");
                quotes = QuoteType.Single;
                ++i;
                continue;
            }

            if (matched.Equals("&quot;", System.StringComparison.OrdinalIgnoreCase))
            {
                encoded.Append(SpanText + "&quot;");
                quotes = QuoteType.Double;
                i += 6;
                continue;
            }

            if (matched.Equals("--"))
            {
                // The rest of the line is comments.
                encoded.Append(SpanComments);
                encoded.Append(s.Substring(i));
                encoded.Append(SpanEnd);
                break;
            }

            if (matched.Equals("/*"))
            {
                // Comments start.
                encoded.Append(SpanComments + "/*");
                inComments = true;
                i += 2;

                continue;
            }

            if (matched.Equals("@@"))
            {
                // Built-in variable
                int j = i + 2;
                while (j < s.Length && char.IsLetterOrDigit(s[j]))
                    ++j;

                if (j == i + 2)
                {
                    encoded.Append("@@");
                    i += 2;

                    continue;
                }

                encoded.Append(SpanBuiltin + s.Substring(i, j - i) + SpanEnd);
                i = j;
                continue;
            }

            if (keywordDictionary.Contains(matched.ToUpper()))
            {
                // Keyword.
                encoded.Append(SpanKeyword);
                encoded.Append(matched);
                encoded.Append(SpanEnd);
                i += matched.Length;
            }
            else
            {
                encoded.Append(matched);
                i += matched.Length;
            }
        }

        if (inComments || quotes != QuoteType.None)
            encoded.Append(SpanEnd);

        return EncoderCommon.StringBufferToChunks(encoded, maxLineWidth, s);
    }

    /// <summary>
    /// Returns the CSS used by the encoder.
    /// </summary>
    /// <returns></returns>
    public TextReader GetEncoderCssStream()
    {
        return new StringReader(SqlStyles);
    }

    /// <summary>
    /// Does nothing, just satisfies ILineEncoder interface.
    /// </summary>
    public void Dispose()
    {
    }
}
