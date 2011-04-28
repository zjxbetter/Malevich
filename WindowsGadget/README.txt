Windows Vista/7 Malevich CR Gadget
==============================================
Date: Jan 20, 2009

This gadget is meant accompany the Malevich web service hosted for CommentsExchange.asmx.  
The following are details around debugging, deploying, and configuration edits to make the gadget work
in various environments.

Debugging and Development
==============================================
There are a few ways to develop a gadget.  The simplest is to develop most in a web page and then "port" 
gadget code for some built-in Windows gadget support (like creating a Settings page that requires registration
with the Gadget infrastructure).  To debug as an HTML page you'll need to comment out the call to "init()"
in Malevich.html <body>.  Also, you'll also need to comment out the "Gadget Support" javascript includes in 
Malevich.html.  Then run VS under debug using Malevich.html as the "startup page".
The problem with running this as an HTML page is that IE does not allow cross-domain posting, so calls to 
the web service will not succeed.  If you're testing for Style and not functionality you can easily fix
this by adjusting the Malevichws.js 'useTestData' variable = true.  Just remember to revert it when building
when your gadget.

Building the Gadget
==============================================
To build a gadget you simply highlight all files under the Malevich root folder and right-click "Send To Compressed
File".  The output file is a ZIP.  Renaming the ZIP to .Gadget is all that is required to officially make it a
gadget.  For each build remember to increment your version number (found in gadget.xml).

Deploying the Gadget
==============================================
Once you have built the gadget, you'll need to do two things:
1) copy the gadget.xml to the location specified in VersionCheck.js remoteVersionFile.  (this is how
   the gadget knows when an update is available)
2) post the Malevich.gadget file to a common area (could be your wwwroot).  If you use wwwroot, you'll 
   also need to make sure IIS has the MIME type set up for *.gadget.  Add this MIME type to IIS:
   .gadget : application/x-windows-gadget
    
Configurables
==============================================
All configuration is done by adjusting variables in the jscripts (located under en-us\scripts).
VersionCheck.js[remoteVersionFile]: URL to the gadget.xml that contains the gadget version number
VersionCheck.js[versionInterval]: time idle between version checks against remoteVersionFile
MalevichWS.js[webServiceUrl]: url to the CommentsExchange.asmx
MalevichWs.js[useTestData]: forces the MalevichWs.js code to use test data instead of hitting CommentsExchange
MalevichWs.js[testData]: simulated value returned from calls to CommentsExchange when useTestData == true
