/* **************************************************************
AUTHOR: slucas
DATE: January 17, 2009
DESCRIPTION: Here's how version check works
- Timer fires CheckVersion fn
- CheckVersion grabs external XML from URL
- CheckVersion grabs internal XML from URL (these two xml files are the
same in terms of content.  When a new version is published, the
the developer will need to copy the internal Gadget.xml to the
URL that will be checked by subsequent gadgets
DEPENDENCIES: UI.js
************************************************************* */

var localVersionFile = "Gadget.xml";
var remoteVersionFile = "http://sergeysomsg2/Gadget.xml";
var versionInterval = 60000 * 5; // five minutes

function InitializeVersionCheck() {
    // onetime initialize timer that will refresh the loader every 30 minutes
    window.setInterval(function() { VersionCheck(); }, versionInterval);
    VersionCheck();
}

///////////////////////////////////////////////////////////////
// UpgradeGadget()
//  Triggered by user clicking the "upgrade button".  This will 
//  navigate the user to the URL specified in the remote file
//  giving them an "upgrade experience"
function UpgradeGadget() {
    var xdocRemote = GetVersionFile(remoteVersionFile);
    var upgradeUrl = xdocRemote.getElementsByTagName("upgradeUrl")[0].childNodes[0].nodeValue;
    window.location.href = upgradeUrl;
}

///////////////////////////////////////////////////////////////
// VersionCheck
//  This is the entry point for checking for version.
//
function VersionCheck() {
    try {
        // Get the documents
        var xdocRemote = GetVersionFile(remoteVersionFile);
        var xdocLocal = GetVersionFile(localVersionFile);

        // Compare the two versions
        var timeToUpgrade = CompareVersions(xdocLocal.getElementsByTagName("version")[0].childNodes[0].nodeValue,
            xdocRemote.getElementsByTagName("version")[0].childNodes[0].nodeValue);
        if (timeToUpgrade > 0) {
            ShowUpdateAvailable();
        } else
            DebugWrite("Version is uptodate");
    }
    catch (e) {
        StatusWrite("<a href='javascript:;' onclick='ToggleContentDebugPage();'>ERROR</a> in checkversion.");
        DebugWrite("Problem checking version: " + e.name + "(" + e.message + ")");
    }
}

function ShowUpdateAvailable() {
    StatusWrite("<a href='javascript:;' onclick='UpgradeGadget();'>New Version</a> available");
}

//////////////////////////////////////////////////////////
// CompareVersions(string, string)
//    Given two versions return 0 if they are the same, 
//    -1 if ver1 is higher, +1 is version two is higher
//
function CompareVersions(ver1, ver2) {
    var ver1Split = ver1.split(".");
    var ver2Split = ver2.split(".");
    for (i = 0; i < ver1Split.length; i++) {
        var i1 = parseInt(ver1Split[i]);
        var i2 = parseInt(ver2Split[i]);
        if (i1 > i2) return -1;
        if (i1 < i2) return 1;
    }
    return 0;
}

//////////////////////////////////////////////////////////
//  XmlDocument GetVersionFile(versionFileUrl)
//
//   Load the version file for local or remote specified here, grab the contents, and send
//   it back as XmlDocument
//
function GetVersionFile(versionFileUrl) {
    var xdoc = new ActiveXObject("Microsoft.XMLDOM");
    xdoc.async = false;
    xdoc.load(versionFileUrl);
    return xdoc;
}
