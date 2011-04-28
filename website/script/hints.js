//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
//
//  Javascript module to display hints about Malevich features
//
// An indicator that a hint might be displayed already.
var hintTimeout = null;

// How long to show each hint.
var hintTimePeriod = 30000;

// Various possible hints.
var HINT_FILEVIEW_CLICK_TO_TYPE = 1;
var HINT_FILEVIEW_CLICK_TO_EDIT = 2;
var HINT_FILEVIEW_CLICK_TO_RESPOND = 3;
var HINT_FILEVIEW_NEED_TO_SUBMIT = 4;
var HINT_FILEVIEW_SELECT_VERSION = 5;
var HINT_FILEVIEW_HOVER_TO_NAVIGATE = 6;
var HINT_FILEVIEW_CHANGE_TEXT_SIZE = 7;
var HINT_FILEVIEW_CLICK_A_LOT = 8;

var HINT_CHANGE_MUST_SUBMIT = 9;
var HINT_CHANGE_NEEDS_WORK_NEEDS_WORK = 10;
var HINT_CHANGE_MUST_BE_REVIEWER_TO_VOTE = 11;
var HINT_CHANGE_AUTHOR_SHOULD_RESPOND = 12;

var HINT_DASHBOARD_HELP = 13;
var HINT_DASHBOARD_ALIAS = 14;
var HINT_DASHBOARD_ANNOYED = 15;
var HINT_DASHBOARD_CL = 16;

var HINT_FILEVIEW_CLICK_TO_TYPE_TEXT = 'Click on any line to enter comments.';
var HINT_FILEVIEW_CLICK_TO_EDIT_TEXT = 'Click on the comment\'s body to edit or remove it.';
var HINT_FILEVIEW_CLICK_TO_RESPOND_TEXT = 'Click on someone else\'s comment to respond to it.';
var HINT_FILEVIEW_NEED_TO_SUBMIT_TEXT = 'Comments are not visible to others until review iteration is submitted.';
var HINT_FILEVIEW_SELECT_VERSION_TEXT = 'Use the radio buttons to select which file versions to compare.';
var HINT_FILEVIEW_HOVER_TO_NAVIGATE_TEXT = 'See help on how to quickly navigate around the diff view.';
var HINT_FILEVIEW_CHANGE_TEXT_SIZE_TEXT = 'You can change the size of the text from the Settings page.';
var HINT_FILEVIEW_CLICK_A_LOT_TEXT = 'Don\'t hold back - leaving comments is easy.';

var HINT_CHANGE_MUST_SUBMIT_TEXT =
    'You must submit the review from this page before comments become visible to others.';
var HINT_CHANGE_NEEDS_WORK_NEEDS_WORK_TEXT =
    'It is impolite to close the review if there are outstanding \'Needs work\' votes.';
var HINT_CHANGE_MUST_BE_REVIEWER_TO_VOTE_TEXT = 'Anyone can leave comments; one must be a reviewer to vote.';
var HINT_CHANGE_AUTHOR_SHOULD_RESPOND_TEXT =
    'Author can respond to review comments by submitting a review iteration.';

var HINT_DASHBOARD_HELP_TEXT = 'Use context-sensitive help (upper-right corner) to learn more about Malevich.';
var HINT_DASHBOARD_ALIAS_TEXT = 'Use ?alias=... in URL to view someone else\'s dashboard.';
var HINT_DASHBOARD_ANNOYED_TEXT = 'Hints annoy you? Turn them off in Settings.';
var HINT_DASHBOARD_CL_TEXT = 'Use ?CL=... to look up shelf set or change list by name.';

// Table of all hints.
var HINT_TABLE = [];
HINT_TABLE[HINT_FILEVIEW_CLICK_TO_TYPE] = HINT_FILEVIEW_CLICK_TO_TYPE_TEXT;
HINT_TABLE[HINT_FILEVIEW_CLICK_TO_EDIT] = HINT_FILEVIEW_CLICK_TO_EDIT_TEXT;
HINT_TABLE[HINT_FILEVIEW_CLICK_TO_RESPOND] = HINT_FILEVIEW_CLICK_TO_RESPOND_TEXT;
HINT_TABLE[HINT_FILEVIEW_NEED_TO_SUBMIT] = HINT_FILEVIEW_NEED_TO_SUBMIT_TEXT;
HINT_TABLE[HINT_FILEVIEW_SELECT_VERSION] = HINT_FILEVIEW_SELECT_VERSION_TEXT;
HINT_TABLE[HINT_FILEVIEW_HOVER_TO_NAVIGATE] = HINT_FILEVIEW_HOVER_TO_NAVIGATE_TEXT;
HINT_TABLE[HINT_FILEVIEW_CHANGE_TEXT_SIZE] = HINT_FILEVIEW_CHANGE_TEXT_SIZE_TEXT;
HINT_TABLE[HINT_FILEVIEW_CLICK_A_LOT] = HINT_FILEVIEW_CLICK_A_LOT_TEXT;
HINT_TABLE[HINT_FILEVIEW_CLICK_A_LOT] = HINT_FILEVIEW_CLICK_A_LOT_TEXT;
HINT_TABLE[HINT_CHANGE_MUST_SUBMIT] = HINT_CHANGE_MUST_SUBMIT_TEXT;
HINT_TABLE[HINT_CHANGE_NEEDS_WORK_NEEDS_WORK] = HINT_CHANGE_NEEDS_WORK_NEEDS_WORK_TEXT;
HINT_TABLE[HINT_CHANGE_MUST_BE_REVIEWER_TO_VOTE] = HINT_CHANGE_MUST_BE_REVIEWER_TO_VOTE_TEXT;
HINT_TABLE[HINT_CHANGE_AUTHOR_SHOULD_RESPOND] = HINT_CHANGE_AUTHOR_SHOULD_RESPOND_TEXT;
HINT_TABLE[HINT_DASHBOARD_HELP] = HINT_DASHBOARD_HELP_TEXT;
HINT_TABLE[HINT_DASHBOARD_ALIAS] = HINT_DASHBOARD_ALIAS_TEXT;
HINT_TABLE[HINT_DASHBOARD_ANNOYED] = HINT_DASHBOARD_ANNOYED_TEXT;
HINT_TABLE[HINT_DASHBOARD_CL] = HINT_DASHBOARD_CL_TEXT;

// Currently made comments, or -1 if comments are disabled. This is emitted by ASP.NET code.
var HINT_MASK = -1;

//
// Removes the hint.
//
function undisplayHint()
{
    if (hintTimeout)
    {
        clearTimeout(hintTimeout);
        hintTimeout = null;
    }

    var el = document.getElementById('CurrentHint');
    if (el)
        el.parentNode.removeChild(el);

    window.onresize = null;
    window.onscroll = null;
}

//
// Centers hint after scroll or resize.
//
function recenterHint()
{
    var el = document.getElementById('CurrentHint');
    if (!el)
        return;

    var baseEl = (document.documentElement && document.compatMode == 'CSS1Compat') ?
        document.documentElement : document.body;

    var viewWidth = self.innerWidth ? self.innerWidth : baseEl.clientWidth;

    el.style.left = ((viewWidth - el.offsetWidth) / 2) + 'px';
    el.style.top = baseEl.scrollTop + 'px';
}

//
// Shows a hint.
//
// hint The text to show.
function displayHint(hint)
{
    var hintDiv = document.createElement('div');
    hintDiv.id = 'CurrentHint';
    hintDiv.className = 'CssHint';
    hintDiv.innerHTML = hint;
    document.body.appendChild(hintDiv);

    // Center it
    var baseEl = (document.documentElement && document.compatMode == 'CSS1Compat') ?
        document.documentElement : document.body;

    var viewWidth = self.innerWidth ? self.innerWidth : baseEl.clientWidth;

    hintDiv.style.left = ((viewWidth - hintDiv.offsetWidth) / 2) + 'px';
    hintDiv.style.top = baseEl.scrollTop + 'px';
     
    hintDiv.onclick = undisplayHint;

    hintTimeout = setTimeout(undisplayHint, hintTimePeriod);

    window.onresize = recenterHint;
    window.onscroll = recenterHint;
}

//
// Shows one of the hints passed in the argument array, if no other hint is displayed.
// Only shows hints that were not displayed in the past.
//
// arguments The list of potential hints to show.
//
function maybeDisplayHints()
{
    if (HINT_MASK == -1)
        return;

    if (hintTimeout != null)
        return;

    for (var arg = 0; arg < arguments.length;  ++arg)
    {
        var hint = arguments[arg];
        var hintTest = 1 << (hint - 1);

        if (HINT_MASK & hintTest)
            continue;

        HINT_MASK &= ~hintTest;
        displayHint(HINT_TABLE[hint]);

        CommentsExchange.RecordHintShowing(hint);
        
        break;
    }
}
