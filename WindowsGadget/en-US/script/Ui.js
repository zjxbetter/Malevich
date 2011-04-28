var CONTENTPAGE = '#contentPage';
var CONTENTDEBUGPAGE = '#contentDebugPage';
var currentView = CONTENTPAGE;
var debugCount = 0;

function InitializeUi() {
    $(CONTENTDEBUGPAGE).hide();
}

// --------------------------------------------------------------------------
// DebugWrite(msg)
//      Write a message to the error block in the HTML page
//
function DebugWrite(msg) {
    var debugPage = $(CONTENTDEBUGPAGE);
    debugPage.html("[" + debugCount + "] " + msg + "</br>" + debugPage.html());
    debugCount++;
    System.Debug.outputString(msg);
}

// --------------------------------------------------------------------------
// ToggleContentDebugPage
//      Toggle between content and debug pages
//
function ToggleContentDebugPage() {
    var currentPage;
    var newPage;
    if (currentView == CONTENTPAGE) {
        currentPage = CONTENTPAGE;
        newPage = CONTENTDEBUGPAGE;
    }
    else {
        currentPage = CONTENTDEBUGPAGE;
        newPage = CONTENTPAGE;
    }
    $(newPage).fadeIn("slow");
    $(currentPage).fadeOut();
    currentView = newPage;
}

// --------------------------------------------------------------------------
// StatusWrite(msg)
//      Write a status message update to the status box at the bottom of the page
//
function StatusWrite(msg) {
    $('#status').html(msg);
}

