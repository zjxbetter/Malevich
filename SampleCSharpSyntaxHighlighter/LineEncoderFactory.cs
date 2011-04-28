//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
using Malevich.Extensions;

/// <summary>
/// Line encoder factory class.
/// </summary>
public class LineEncoderFactory : ILineEncoderFactory
{
    public ILineEncoder GetLineEncoder(string fileType)
    {
        if ("cs".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.CSharp);

        if ("c".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.C);

        if ("cxx".Equals(fileType) || "cpp".Equals(fileType) || "hpp".Equals(fileType) || "hxx".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.CPlusPlus);

        if ("js".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.JavaScript);

        if ("java".Equals(fileType) || "jav".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.JavaScript);

        if ("m".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.ObjectiveC);

        if ("mm".Equals(fileType) ||
            "h".Equals(fileType))
            return new CStyleLineEncoder(CStyleLanguage.ObjectiveCPlusPlus);

        if ("xml".Equals(fileType) || "xsd".Equals(fileType) || "xslt".Equals(fileType) ||
            "htm".Equals(fileType) || "html".Equals(fileType) ||
            "aspx".Equals(fileType) || "asmx".Equals(fileType) || "ascx".Equals(fileType) ||
            "csproj".Equals(fileType) || "vcproj".Equals(fileType) ||
            "vdproj".Equals(fileType) || "dbproj".Equals(fileType) ||
            "config".Equals(fileType) || "resx".Equals(fileType) || "xaml".Equals(fileType) ||
            "wxs".Equals(fileType) || "wxi".Equals(fileType) || "wxl".Equals(fileType))
            return new MarkupLineEncoder();

        if ("sql".Equals(fileType))
            return new SqlLineEncoder();

        throw new InvalidEncoderState("This file (" + fileType + ")type is not supported by the encoder!");
    }
}


