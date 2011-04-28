//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Eldar Musaev & Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
namespace ReviewNotifier
{
    public interface ILog
    {
        void Log(string format, params object[] args);
        void Close();
    }
}