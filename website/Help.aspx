<%@ Page Title="Malevich - Help" Language="C#" MasterPageFile="~/Default.master" AutoEventWireup="true" CodeFile="Help.aspx.cs" Inherits="Help" %>
<%@ MasterType VirtualPath="~/Default.master" %>

<asp:Content ID="Head" ContentPlaceHolderID="head" Runat="Server">
</asp:Content>

<asp:Content ID="Header" ContentPlaceHolderID="HeaderPlaceHolder" Runat="Server">
    <%
        Master.Title = "Malevich Help";
    %>
</asp:Content>

<asp:Content ID="Main" ContentPlaceHolderID="MainPlaceHolder" Runat="Server">
</asp:Content>
<asp:Content ID="Footer" ContentPlaceHolderID="FooterPlaceHolder" Runat="Server">
</asp:Content>

