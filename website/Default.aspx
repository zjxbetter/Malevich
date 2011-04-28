<%@ Page Language="C#" AutoEventWireup="true"  CodeFile="Default.aspx.cs" MasterPageFile="~/Default.master" Inherits="Malevich._Default" ValidateRequest="false" ClientIDMode="Static" %>
<%@ MasterType VirtualPath="~/Default.master" %>

<asp:Content id="Header" ContentPlaceHolderID="HeaderPlaceHolder" runat="server">
    <%
        RenderHeaderSkin();
        RenderStyle();
    %>
    <asp:ScriptManager ID="MalevichScriptManager" runat="server">
        <Services>
            <asp:ServiceReference Path="~/CommentsExchange.asmx" />
        </Services>
    </asp:ScriptManager>
</asp:Content>

<asp:Content id="Main" ContentPlaceHolderID="MainPlaceHolder" runat="server">
    <asp:PlaceHolder id="ActivePage" runat="server" />
</asp:Content>

<asp:Content id="Footer" ContentPlaceHolderID="FooterPlaceHolder" runat="server">
    <%
        RenderHintScriptSupport();
        RenderFooterSkin();
    %>
</asp:Content>

