<%@ page language="C#" autoeventwireup="true" masterpagefile="~/Default.master" inherits="Malevich._Default, App_Web_viyrg3nj" validaterequest="false" clientidmode="Static" %>
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

