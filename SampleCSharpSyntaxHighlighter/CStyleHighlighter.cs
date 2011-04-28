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
/// Language.
/// </summary>
public enum CStyleLanguage
{
    C = 0,
    CPlusPlus = 1,
    CSharp = 2,
    JavaScript = 3,
    Java = 4,
    ObjectiveC = 5,
    ObjectiveCPlusPlus = 6
}

/// <summary>
/// Implements a very primitive syntax highlighter which emulates Visual Studio C/C++/C#/JavaScript syntax coloring.
/// 
/// This is just a sample code, and is not a part of Malevich installation. Nevertheless, it is fairly
/// complex. If you develop your own based on this sample, please consider running the test to make
/// sure corner cases are handled. The test is SampleHighlighterTest
/// 
/// Note that, obviously, we can't do much for type names - there is simply no sufficient information
/// to parse types. However, this could be adapted to know about some standard runtime types such as
/// Console. I did not do that.
/// </summary>
public class CStyleLineEncoder : ILineEncoder
{
    /// <summary>
    /// Font for text constants.
    /// </summary>
    private const string SpanText = "<span class=\"CssHlText\">";

    /// <summary>
    /// Font for comments.
    /// </summary>
    private const string SpanComment = "<span class=\"CssHlComment\">";

    /// <summary>
    /// Font for keywords.
    /// </summary>
    private const string SpanKeyword = "<span class=\"CssHlKeyword\">";

    /// <summary>
    /// End of color-coded segment.
    /// </summary>
    private const string SpanEnd = "</span>";

    /// <summary>
    /// Standard definitions
    /// </summary>
    private const string CStyles =
        ".CssHlText\n" +
        "{\n" +
        "    color: #cc0000;\n" +
        "}\n" +
        ".CssHlKeyword\n" +
        "{\n" +
        "    color: #0000cc;\n" +
        "}\n" +
        ".CssHlComment\n" +
        "{\n" +
        "    color: #009900;\n" +
        "}\n";

    /// <summary>
    /// C++ keywords.
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] cPlusPlusKeywords =
    {
        "and", 
        "and_eq",
        "asm",
        "auto",
        "bitand",
        "bitor",
        "bool",
        "break",
        "case",
        "catch",
        "char",
        "class",
        "compl",
        "const",
        "const_cast",
        "continue",
        "default",
        "delete",
        "do",
        "double",
        "dynamic_cast",
        "else",
        "enum",
        "explicit",
        "export",
        "extern",
        "false",
        "float",
        "for",
        "friend",
        "goto",
        "if",
        "inline",
        "int",
        "long",
        "mutable",
        "namespace",
        "new",
        "not",
        "not_eq",
        "operator",
        "or",
        "or_eq",
        "private",
        "protected",
        "public",
        "register",
        "reinterpret_cast",
        "return",
        "short",
        "signed",
        "sizeof",
        "static",
        "static_cast",
        "struct",
        "switch",
        "template",
        "this",
        "throw",
        "true",
        "try",
        "typedef",
        "typeid",
        "typename",
        "union",
        "unsigned",
        "using",
        "virtual",
        "void",
        "volatile",
        "wchar_t",
        "while",
        "xor",
        "xor_eq",
        "#define",
        "#elif",
        "#else",
        "#endif",
        "#error",
        "#ifdef",
        "#ifndef",
        "#if",
        "#import",
        "#include",
        "#line",
        "#pragma",
        "#undef",
        "#using"
    };

    /// <summary>
    /// C keywords.
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] cKeywords =
    {
        "asm",
        "auto",
        "break",
        "case",
        "char",
        "const",
        "continue",
        "default",
        "do",
        "double",
        "else",
        "enum",
        "extern",
        "float",
        "for",
        "goto",
        "if",
        "int",
        "long",
        "register",
        "return",
        "short",
        "signed",
        "sizeof",
        "static",
        "struct",
        "switch",
        "typedef",
        "union",
        "unsigned",
        "void",
        "volatile",
        "while",
        "#define",
        "#elif",
        "#else",
        "#endif",
        "#error",
        "#ifdef",
        "#ifndef",
        "#if",
        "#import",
        "#include",
        "#line",
        "#pragma",
        "#undef",
        "#using"
    };

    /// <summary>
    /// C# keywords.
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] cSharpKeywords =
    {
        "abstract",
        "event",
        "new",
        "struct",
        "as",
        "explicit",
        "null",
        "switch",
        "base",
        "extern",
        "object",
        "this",
        "bool",
        "false",
        "operator",
        "throw",
        "break",
        "finally",
        "out",
        "true",
        "byte",
        "fixed",
        "override",
        "try",
        "case",
        "float",
        "params",
        "typeof",
        "catch",
        "for",
        "private",
        "uint",
        "char",
        "foreach",
        "protected",
        "ulong",
        "checked",
        "goto",
        "public",
        "unchecked",
        "class",
        "if",
        "readonly",
        "unsafe",
        "const",
        "implicit",
        "ref",
        "ushort",
        "continue",
        "in",
        "return",
        "using",
        "decimal",
        "int",
        "sbyte",
        "virtual",
        "default",
        "interface",
        "sealed",
        "volatile",
        "delegate",
        "internal",
        "short",
        "void",
        "do",
        "is",
        "sizeof",
        "while",
        "double",
        "lock",
        "stackalloc",
        "else",
        "long",
        "static",
        "enum",
        "namespace",
        "string",
        "#define",
        "#elif",
        "#else",
        "#endif",
        "#endregion",
        "#error",
        "#ifdef",
        "#ifndef",
        "#if",
        "#import",
        "#include",
        "#line",
        "#pragma",
        "#region",
        "#undef",
        "#using",
        "#warning"
    };

    /// <summary>
    /// JavaScript keywords.
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] javaScriptKeywords =
    {
        "abstract",
        "boolean",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "class",
        "const",
        "continue",
        "debugger",
        "default",
        "delete",
        "do",
        "double",
        "else",
        "enum",
        "export",
        "extends",
        "false",
        "final",
        "finally",
        "float",
        "for",
        "function",
        "goto",
        "if",
        "implements",
        "import",
        "in",
        "instanceof",
        "int",
        "interface",
        "long",
        "native",
        "new",
        "null",
        "package",
        "private",
        "protected",
        "public",
        "return",
        "short",
        "static",
        "super",
        "switch",
        "synchronized",
        "this",
        "throw",
        "throws",
        "transient",
        "true",
        "try",
        "typeof",
        "var",
        "void",
        "volatile",
        "while",
        "with"
    };

    /// <summary>
    /// Java keywords. Taken from http://java.sun.com/docs/books/tutorial/java/nutsandbolts/_keywords.html
    /// Plus "null", "true" and "false" (not officially keywords)
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] javaKeywords =
    {
        "abstract",
        "assert",
        "boolean",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "class",
        "const",
        "continue",
        "default",
        "do",
        "double",
        "else",
        "enum",
        "extends",
        "false",
        "final",
        "finally",
        "float",
        "for",
        "goto",
        "if",
        "implements",
        "import",
        "instanceof",
        "int",
        "interface",
        "long",
        "native",
        "new",
        "null",
        "package",
        "private",
        "protected",
        "public",
        "return",
        "short",
        "static",
        "strictfp",
        "super",
        "switch",
        "synchronized",
        "this",
        "throw",
        "throws",
        "transient",
        "true",
        "try",
        "void",
        "volatile",
        "while",
    };

    /// <summary>
    /// Objective C++ keywords. These keywords are a superset of the C++ keywords. 
    /// It adds "id", "self", "super", "SEL", "YES", "NO", and the "@"-prefixed keywords
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] objectiveCPlusPlusKeywords =
    {
        "and", 
        "and_eq",
        "asm",
        "auto",
        "bitand",
        "bitor",
        "bool",
        "break",
        "case",
        "catch",
        "char",
        "class",
        "compl",
        "const",
        "const_cast",
        "continue",
        "default",
        "delete",
        "do",
        "double",
        "dynamic_cast",
        "else",
        "enum",
        "explicit",
        "export",
        "extern",
        "false",
        "float",
        "for",
        "friend",
        "goto",
        "if",
        "inline",
        "int",
        "long",
        "mutable",
        "namespace",
        "new",
        "not",
        "not_eq",
        "operator",
        "or",
        "or_eq",
        "private",
        "protected",
        "public",
        "register",
        "reinterpret_cast",
        "return",
        "short",
        "signed",
        "sizeof",
        "static",
        "static_cast",
        "struct",
        "switch",
        "template",
        "this",
        "throw",
        "true",
        "try",
        "typedef",
        "typeid",
        "typename",
        "union",
        "unsigned",
        "using",
        "virtual",
        "void",
        "volatile",
        "wchar_t",
        "while",
        "xor",
        "xor_eq",
        "#define",
        "#elif",
        "#else",
        "#endif",
        "#error",
        "#ifdef",
        "#ifndef",
        "#if",
        "#import",
        "#include",
        "#line",
        "#pragma",
        "#undef",
        "#using",
        "SEL",
        "YES",
        "NO",
        "id",
        "self",
        "super",
        "BOOL",
        "@catch",
        "@class",
        "@dynamic",
        "@encode",
        "@end",
        "@finally",
        "@implementation",
        "@interface",
        "@private",
        "@property",
        "@protected",
        "@protocol",
        "@public",
        "@selector",
        "@synthesize",
        "@throw",
        "@try",
    };

    /// <summary>
    /// Objective C keywords. These keywords are a superset of the C keywords
    /// 
    /// Important note: order matters. If one keyword is a prefix of another keyword, the shorter one MUST be
    /// after the longer one.
    /// </summary>
    private static string[] objectiveCKeywords =
    {
        "asm",
        "auto",
        "break",
        "case",
        "char",
        "const",
        "continue",
        "default",
        "do",
        "double",
        "else",
        "enum",
        "extern",
        "float",
        "for",
        "goto",
        "if",
        "int",
        "long",
        "register",
        "return",
        "short",
        "signed",
        "sizeof",
        "static",
        "struct",
        "switch",
        "typedef",
        "union",
        "unsigned",
        "void",
        "volatile",
        "while",
        "#define",
        "#elif",
        "#else",
        "#endif",
        "#error",
        "#ifdef",
        "#ifndef",
        "#if",
        "#import",
        "#include",
        "#line",
        "#pragma",
        "#undef",
        "#using",
        "SEL",
        "YES",
        "NO",
        "id",
        "self",
        "super",
        "BOOL",
        "@catch",
        "@class",
        "@dynamic",
        "@encode",
        "@end",
        "@finally",
        "@implementation",
        "@interface",
        "@private",
        "@property",
        "@protected",
        "@protocol",
        "@public",
        "@selector",
        "@synthesize",
        "@throw",
        "@try",
    };

    /// <summary>
    /// Previous multiline string constant has not yet terminated.
    /// </summary>
    private QuoteType quotes = QuoteType.None;

    /// <summary>
    /// Previous multiline comment has not yet terminated.
    /// </summary>
    private bool inComments = false;

    /// <summary>
    /// Keywords regular expression.
    /// </summary>
    private Regex keywordRegex;

    /// <summary>
    /// Keyword dictionary.
    /// </summary>
    private HashSet<string> keywordDictionary;

    /// <summary>
    /// What language this encoder will implement.
    /// </summary>
    private CStyleLanguage language;

    /// <summary>
    /// Instantiates the CStyleLineEncoder.
    /// </summary>
    /// <param name="languageType">Which language is this?</param>
    public CStyleLineEncoder(CStyleLanguage languageType)
    {
        language = languageType;

        string keywordRE = "&quot;|'|//|/\\*|#?[a-z_0-9]+";
        string[] keywords;

        switch (languageType)
        {
            case CStyleLanguage.C:
                keywords = cKeywords;
                break;
            case CStyleLanguage.CPlusPlus:
                keywords = cPlusPlusKeywords;
                break;
            case CStyleLanguage.CSharp:
                keywords = cSharpKeywords;
                keywordRE = "@&quot;|" + keywordRE;
                break;
            case CStyleLanguage.JavaScript:
                keywords = javaScriptKeywords;
                break;
            case CStyleLanguage.Java:
                keywords = javaKeywords;
                break;
            case CStyleLanguage.ObjectiveC:
                keywords = objectiveCKeywords;
                keywordRE = "&quot;|'|//|/\\*|[@#]?[a-z_0-9A-Z]+";
                break;
            case CStyleLanguage.ObjectiveCPlusPlus:
                keywords = objectiveCPlusPlusKeywords;
                keywordRE = "&quot;|'|//|/\\*|[@#]?[a-z_0-9A-Z]+";
                break;
            default:
                throw new InvalidEncoderState("This language type is not supported");
        }

        keywordDictionary = new HashSet<string>(keywords);
        keywordRegex = new Regex(keywordRE, RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
            encoded.Append(SpanComment);

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

            if (quotes == QuoteType.Verbatim)
            {
                int endQuotes = s.IndexOf("&quot;", i);
                if (endQuotes == -1)
                {
                    // The entire string is in quotes. The tag will close after the loop.
                    encoded.Append(s.Substring(i));
                    break;
                }

                if (s.IndexOf("&quot;&quot;", endQuotes) == endQuotes)
                {
                    // This is an escaped quote.
                    encoded.Append(s.Substring(i, endQuotes + 12 - i));
                    i = endQuotes + 12;
                    continue;
                }

                encoded.Append(s.Substring(i, endQuotes + 6 - i));
                quotes = QuoteType.None;
                encoded.Append(SpanEnd);

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

            Match nextMatch = keywordRegex.Match(s, i);
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

            if (matched.Equals("@&quot;", System.StringComparison.OrdinalIgnoreCase))
            {
                encoded.Append(SpanText + "@&quot;");
                quotes = QuoteType.Verbatim;
                i += 7;
                continue;
            }

            if (matched.Equals("//"))
            {
                // The rest of the line is comments.
                encoded.Append(SpanComment);
                encoded.Append(s.Substring(i));
                encoded.Append(SpanEnd);
                break;
            }

            if (matched.Equals("/*"))
            {
                // Comments start.
                encoded.Append(SpanComment + "/*");
                inComments = true;
                i += 2;
                continue;
            }

            if (keywordDictionary.Contains(matched))
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

        if (language == CStyleLanguage.CSharp)
        {
            // Only verbatim string constants can span multiple lines in C#.
            if (quotes != QuoteType.Verbatim)
                quotes = QuoteType.None;
        }
        else if (language == CStyleLanguage.C || language == CStyleLanguage.CPlusPlus)
        {
            // C and C++ support string continuation if the last character on the line is a backslash.
            if (quotes != QuoteType.Double || !s.EndsWith("\\"))
                quotes = QuoteType.None;
        }
        else
        {
            // Java and JavaScript are the only "other" language, and they do not support
            // line continuations at all. Most browsers do support C-style multiline strings, but we support
            // only ECMA standard here or Java, at least for now...
            quotes = QuoteType.None;
        }

        return EncoderCommon.StringBufferToChunks(encoded, maxLineWidth, s);
    }

    /// <summary>
    /// Returns the CSS used by the encoder.
    /// </summary>
    /// <returns></returns>
    public TextReader GetEncoderCssStream()
    {
        return new StringReader(CStyles);
    }

    /// <summary>
    /// Does nothing, just satisfies ILineEncoder interface.
    /// </summary>
    public void Dispose()
    {
    }
}
