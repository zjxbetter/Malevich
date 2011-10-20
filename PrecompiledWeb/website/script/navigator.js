//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
//
//  Javascript module to navigate the file view.
//
// A three-second delay used to delay removal of navigator arrows when mouse leaves the element.
var defaultTimeoutValue = 3000;

// A handle returned by setTimeout for the delay before removing the navigator decorations.
var timeoutHandle = null;

function getScrollContext() {
    return $(window).height() / 5;
}

// An array of elements that are targets for keyboard navigation.
// Note to the maintainer: if you feel an urge of optimizing this by replacing the elements with the offsets,
// resist! Element offsets change every time a comment is entered
// (of course, cache invalidation could be based on whether $(document).height() has changed).
var NavPoints;
var FileView;
var FileViewGroups;

var NavigatorUpArrow;
var NavigatorDownArrow;
var NavigatorArrows;
var NavigatorArrowsVisible = false;

// Used for diffing displayed elements
var diffTimeoutHandle = null;

function navigatorCreateArrows(cb) {
    // Create arrow images
    NavigatorUpArrow = $('<div id="NavigatorUpArrow" class="CssNavigatorUpArrow"/>');
    NavigatorDownArrow = $('<div id="NavigatorDownArrow" class="CssNavigatorDownArrow"/>');
    NavigatorArrows = $().add(NavigatorUpArrow).add(NavigatorDownArrow).hide();
    $(document.body).append(NavigatorArrows);

    // Position arrows
    navigatorPositionArrows();

    // Events
    NavigatorArrows.mouseenter(navigatorMouseIn);
    NavigatorArrows.mouseleave(navigatorMouseOut);
    NavigatorArrows.click(navigatorArrowClicked);
    FileView.mousemove(function (event) {
        navigatorShowArrows(defaultTimeoutValue);
    });
    $(window).scroll(function (event) {
        navigatorRefreshArrows();
        if (timeoutHandle != null) {
            navigatorHideArrows(defaultTimeoutValue);
        }
        DiffDisplayedElements(scrollToElementDuration);
    });
    $(window).resize(navigatorPositionArrows);

    cb && cb();
}

function DiffDisplayedElements(timeout) {
    if (!FileView.is('.CssFileViewSplit'))
        return;

    if (diffTimeoutHandle) {
        clearTimeout(diffTimeoutHandle);
        diffTimeoutHandle = null;
    }
    if (timeout) {
        diffTimeoutHandle = setTimeout(DiffDisplayedElements, timeout);
    }
    else {
        FindDisplayedElements(FileViewGroups).filter('.Changed').each(function () {
            diffAnnotateElem(this);
        });
    }
}

function navigatorPositionArrows() {
    NavigatorUpArrow.css({
        left: ($(window).width() - NavigatorUpArrow.width()) / 2
    });
    NavigatorDownArrow.css({
        left: ($(window).width() - NavigatorDownArrow.width()) / 2
    });
}

function navigatorShowArrow(arrow) {
    if (!arrow.data('displayed')) {
        arrow.data('displayed', true);
        arrow.stop(true, true);
        var dir = (arrow == NavigatorUpArrow ? 'up' : 'down');
        arrow.effect('slide', { direction: dir, mode: 'show' });
    }
}

function navigatorHideArrow(arrow) {
    if (arrow.data('displayed')) {
        arrow.data('displayed', false);
        arrow.stop(true, true);
        var dir = (arrow == NavigatorUpArrow ? 'up' : 'down');
        arrow.effect('slide', { direction: dir, mode: 'hide' });
    }
}

function navigatorRefreshArrows() {
    if (NavigatorArrowsVisible) {
        var fvTop = FileView.offset().top;
        var fvBot = fvTop + FileView.height();
        
        var scrTop = $(window).scrollTop();
        var scrBot = scrTop + $(window).height();
        
        if (fvTop <= scrTop && scrTop < fvBot) {
            navigatorShowArrow(NavigatorUpArrow);
        }
        else {
            navigatorHideArrow(NavigatorUpArrow);
        }

        if (scrBot <= fvBot && fvTop < scrBot) {
            navigatorShowArrow(NavigatorDownArrow);
        }
        else {
            navigatorHideArrow(NavigatorDownArrow);
        }
    }
    else {
        navigatorHideArrow(NavigatorUpArrow);
        navigatorHideArrow(NavigatorDownArrow);
    }
}

function navigatorShowArrows(timeoutValue) {
    navigatorCancelHideArrows();
    NavigatorArrowsVisible = true;
    navigatorRefreshArrows();
    if (timeoutValue) {
        navigatorHideArrows(timeoutValue);
    }
}

function navigatorHideArrows(timeoutValue) {
    navigatorCancelHideArrows();
    if (timeoutValue) {
        timeoutHandle = setTimeout(navigatorHideArrows, timeoutValue);
    }
    else {
        NavigatorArrowsVisible = false;
        navigatorRefreshArrows();
    }
}

function navigatorCancelHideArrows() {
    if (timeoutHandle) {
        clearTimeout(timeoutHandle);
        timeoutHandle = null;
    }
}

//
// Handles clicking on the arrow.
//
// event The mouse click event for the button.
//
function navigatorArrowClicked(event) {
    if ($(event.target).is('#NavigatorUpArrow')) {
        navigatorMoveToPreviousChange(navigatorRefreshArrows);
    }
    else {
        navigatorMoveToNextChange(navigatorRefreshArrows);
    }
}

//
// Handles mouse in event for the elements where navigation is active.
//
// If an element is tagged with NavigatorPrevAnchor and/or NavigatorNextAnchor
// attributes, it creates up and/or down arrows surrounding the element
// from, respectively, top and bottom.
//
// event The mouse in event that has just fired on this element.
//
function navigatorMouseIn(event) {
    navigatorCancelHideArrows();
    event.preventDefault();
}

//
// Handles mouse out event for the elements where navigation is active.
//
// A 3-second timeout is scheduled to remove the arrows created by the mouse-in event.
//
// event The mouse out event that has just fired on this element.
//
function navigatorMouseOut(event) {
    navigatorHideArrows(defaultTimeoutValue);
    event.preventDefault();
}

//
// Navigation using keyboard shortcuts (F7/F8).
//

//
// Key code for F7 = previous change.
//
var F7_KEY_CODE = 118;

//
// Key code for F8 = next change.
//
var F8_KEY_CODE = 119;

//
// Processes the key. Finds the next (or previous) change - depending on what key was pressed - relative to what
// is currently displayed on the screen - and jumps to that change.
//
function processPrevNextKeys(event) {
    if (NavPoints.length <= 0)
        return;

    if (event.keyCode != F7_KEY_CODE && event.keyCode != F8_KEY_CODE)
        return;

    // Forward
    if (event.keyCode == F8_KEY_CODE) {
        navigatorMoveToNextChange();
    }
    // Backward
    else if (event.keyCode == F7_KEY_CODE) {
        navigatorMoveToPreviousChange();
    }

    event.preventDefault();
    return false;
}

function navigatorMoveToNextChange(cb) {
    var context = getScrollContext();
    var screenTop = $(window).scrollTop();
    var screenBot = screenTop + $(window).height();
    var target = undefined;
    var idx;

    idx = binarySearch.call(NavPoints, function(el) {
        return $(el).offset().top < screenBot - context + 10 &&
            $(el).offset().top + $(el).height() < screenBot;
    });
    idx = Math.min(idx, NavPoints.length - 1);
    target = NavPoints[idx];
    scrollToElement(target, cb);
}

function navigatorMoveToPreviousChange(cb) {
    var context = getScrollContext() + 10;
    var screenTop = $(window).scrollTop();
    var screenBot = screenTop + $(window).height();
    var target = undefined;
    var idx;

    idx = binarySearch.call(NavPoints, function(el) {
        return $(el).offset().top < screenTop;
    });
    idx = Math.max(idx - 1, 0);
    target = NavPoints[idx];
    scrollToElement(target, cb);
}

//
// Performs a binary search on this.
//
// fn - returns true if the argument is less than the target value; false otherwise.
//
binarySearch = function(fn) {
    var h = this.length;
    var l = -1;
    var m;
    while (h - l > 1) {
        if (fn(this[m = h + l >> 1])) {
            l = m;
        }
        else {
            h = m;
        }
    }
    return h;
};

//
// Builds NavigatorArray - an array of all target change elements - once the page loads.
// When the page is rendered, the changes are assigned ids ("row17"), and linked into almost a linked
// list where the next change ID is in an attribute called NavigatorNextAnchor. Almost, but not always -
// if the change is contiguous, only the last row has this attribute set.
//
// When this function ends executing, NavigatorArray contains all elements that are targets for jumping to
// on F7/F8 keyboard shortcuts.
//
function BuildNavigatorMap() {
    FileView = $('#fileview');
    FileViewGroups = FileView.find('.RowGroup');
    NavPoints = $()
                .add(FileView.find('tr').first())
                .add(FileView.find('.RowGroup:not(.Unchanged),.Comment'))
                .add(FileView.find('tr').last());

    $(document).keydown(processPrevNextKeys);
}

function FindDisplayedElements(elems) {
    var screenTop = $(window).scrollTop();
    var screenBot = screenTop + $(window).height();

    // Find first.
    idx = binarySearch.call(elems, function (el) {
        return $(el).offset().top < screenTop;
    });
    var res = $();
    while (idx < elems.length) {
        if ($(elems[idx]).offset().top < screenBot) {
            res = res.add(elems[idx]);
        }
        ++idx;
    }
    return res;
}

function ResetNavigatorMap() {
    NavPoints = undefined;
    FileView = undefined;
    FileViewGroups = undefined;

    NavigatorUpArrow = undefined;
    NavigatorDownArrow = undefined;
    NavigatorArrows = undefined;
    NavigatorArrowsVisible = false;
}

//
// Makes BuildNavigatorMap be called on page load.
//
$(document).ready(function () {
    ResetNavigatorMap();
    BuildNavigatorMap();
    if (NavPoints.length) {
        navigatorCreateArrows(function () {
            $(window).scroll();
            navigatorShowArrows(defaultTimeoutValue);
        });
    }
});
