/* **************************************************************
AUTHOR: slucas
DATE: January 17, 2009
DESCRIPTION: 
    This is the entry point for all work in the gadget.
DEPENDENCIES: 
    UI.js
    MalevichWs.js
    VersionCheck.js
    Gadget.js
************************************************************* */
var totalWriteMessages = 0;

function init_REMOVETHISSUFFIXFORHTMLTESTING() {
    DebugWrite("Gadget init'd.  REMINDER: this needs to be the REAL gadget js code when we switch to a gadget.");
}

// -------------------------------------------------------------------
// InitGadget
//  Initialize all gadget stuff
//
function InitGadget() {
    InitializeUi();
    InitializeMalevichCrData();
    InitializeVersionCheck();
}
