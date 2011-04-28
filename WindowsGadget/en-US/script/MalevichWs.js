/***************************************************************
    AUTHOR: slucas
    DATE: January 17, 2009
    DESCRIPTION: 
    This file does the collecting of the data, then pushing the
    data to the content page of the gadget.
    DEPENDENCIES:
        Ui.js
**************************************************************/

// Data stored from last successful poll from the ws
var lastDataPoll = null;
var webServiceUrl = "http://sergeysomsg2/malevich/commentsexchange.asmx";
var useTestData = false;
var testData = 12;
var responsesFromWsReceived = 3;

// --------------------------------------------------------------------------
// InitializeMalevichCrData()
//      Initializes the data retrieval for the gadget
//
function InitializeMalevichCrData() {
    DebugWrite("[InitializeMalevichCrData]");
    RequestAll();
    // onetime initialize timer that will refr4esh the loader every 5 min
    window.setInterval(function() { RequestAll(); }, 60000 * 5);
}

// --------------------------------------------------------------------------
// RequestAll()
//      Make async requests for all data to the web service
//
function RequestAll() {
    
    StatusWrite("Requesting data...");
    responsesFromWsReceived = 3;
    try {
        RequestPendingRequests();
        RequestPendingResponses();
        RequestPendingTotals();
    }
    catch (e) {
        DebugWrite("[ERROR RETRIEVING DATA]");
        DebugWrite(e);
        DebugWrite(e.message);
        StatusWrite("Error in data response...");
        responsesFromWsReceived = 3;
        return;
    }
    StatusWrite("Waiting for responses...");
}

// --------------------------------------------------------------------------
// SetupCommentsExchange()
//      Create and setup the commentsexchange object, then return
//
function SetupCommentsExchange(successCallback, failureCallback) {
    var ws = new CommentsExchange();
    ws.set_path(webServiceUrl); 
    ws.set_defaultSucceededCallback(successCallback);
    ws.set_defaultFailedCallback(failureCallback);
    return ws;
}

// --------------------------------------------------------------------------
// RequestPendingRequests()
//      Make async request for Pending Requests
//
function RequestPendingRequests() {
    if (useTestData) {
        onRequestForPendingRequestSuccess(testData);
    }
    else {
        var ws = SetupCommentsExchange(onRequestForPendingRequestSuccess, onRequestFailed);
        ws.GetNumberOfReviewsWhereIAmAReviewer();
    }
}

// --------------------------------------------------------------------------
// RequestPendingResponses()
//      Make async request for Pending Responses
//
function RequestPendingResponses() {
    if (useTestData) {
        onRequestForPendingResponseSuccess(testData);
    }
    else {
        var ws = SetupCommentsExchange(onRequestForPendingResponseSuccess, onRequestFailed);
        ws.GetNumberOfReviewsWhereIAmTheReviewee();
    }
}

// --------------------------------------------------------------------------
// RequestPendingTotals()
//      Make async request for Pending Totals
//
function RequestPendingTotals() {
    if (useTestData) {
        onRequestForPendingActiveSuccess(testData);
    }
    else {
        var ws = SetupCommentsExchange(onRequestForPendingActiveSuccess, onRequestFailed);
        ws.GetNumberOfOpenReviews();
    }
}

// --------------------------------------------------------------------------
// onResponseSuccess()
//      All success responses go through this api.  If we've received the last
//      response of the 3 requests, then we update the last read time in the UI
//
function onResponseSuccess(count, controlId) {
    try {
        if (count == 0)
            $(controlId).html(count);
        else {
            $(controlId).html("<a href='http://sergeysomsg2/malevich'>" + count + "</a>");
        }
        responsesFromWsReceived--;
        if (responsesFromWsReceived <= 0) {
            UpdateStatusWithTime();
        }
    }
    catch (e) {
        DebugWrite("[onResponseSuccess] FAIL");
        DebugWrite(e.message);
        DebugWrite(e);
    }
}

////////////////////////////////////////////////////////////////////////////
// updateStatusWithTime()
//      adds the current time to the footer of the main 'data panel'
//
function UpdateStatusWithTime()
{
    var now = new Date().toTimeString();
    var nowStrParts = now.split(":");
    var hour = parseInt(nowStrParts[0], 10);
    var pm = "AM";
    if (hour > 12) {
        hour -= 12;
        pm = "PM";
    }
    else if (hour == 12)
        pm = "PM";
    else if (hour == 0)
        hour = 12;
        
    StatusWrite("Updated: " + hour + ":" + nowStrParts[1] + " " + pm);
}

// --------------------------------------------------------------------------
// onRequestForPendingRequestSuccess()
//      Callback for successful request of any of the above
//
function onRequestForPendingRequestSuccess(result) {
    onResponseSuccess(result, "#totalRequestsPending");
}

// --------------------------------------------------------------------------
// onRequestForPendingActiveSuccess()
//      Callback for successful request of any of the above
//
function onRequestForPendingActiveSuccess(result) {
    onResponseSuccess(result, "#totalActive");
}

// --------------------------------------------------------------------------
// onRequestForPendingResponseSuccess()
//      Callback for successful request of any of the above
//
function onRequestForPendingResponseSuccess(result) {
    onResponseSuccess(result, "#totalResponsesPending");
}

// --------------------------------------------------------------------------
// onRequestForPendingRequestSuccess()
//      Callback for successful request of any of the above
//
function onRequestFailed(result) {
    DebugWrite("[OnRequestFailed]" + result.toString());
    StatusWrite("FAILURE: " + result.toString());
}