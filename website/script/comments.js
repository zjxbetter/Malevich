﻿//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
//
//  Javascript module to manipulate the contents of reviewer comments.
//
var COMMENT_PROMPT = 'Enter your comments here...';

// The id of the last comment entered on the page. Used by auto-collapse option.
var last_comment_id = null;

var COMMENT_TOKEN = '_comment';

//
// Creates an auto-sizing textarea.
//
// commentId The comment to resize.
function textAreaAutoSize(event) {
    var comment = getComment(event);
    if (!comment || !comment.edit_textarea)
        return;

    var textarea = comment.edit_textarea[0];

    var lineCount = textarea.value.split('\n').length;

    //@TODO: also add height for each line that causes a word wrap due to length.

    // No changes if the number of lines has not changed.
    if (lineCount == textarea.lineCount)
        return;

    $(textarea).attr("rows", lineCount + 1);

    textarea.lineCount = lineCount;
}

//
// Hides an element.
//
// element The element to hide.
function hideElement(el, callback) {
    if (!isVisible(el))
        return;

    //$(el).fadeTo(0, 1);
    //$(el).show(0);

    $(el).animate({
        height: 'hide',
        //opacity: '0',
        duration: 100
    }, callback);
}

//
// Makes an element visible.
//
// element The element to show.
function showElement(el, callback) {
    if (isVisible(el))
        return;

    //$(el).fadeTo(0, 0);
    //$(el).hide(0);

    $(el).animate({
        height: 'show',
        //opacity: '1',
        duration: 100
    }, callback);
}

//
// Toggles element visibility.
//
// element The element to show.
function toggleElement(el, callback) {
    $(el).animate({
        height: 'toggle',
        //opacity: 'toggle',
        duration: 100
    }, callback);
}


//
// Returns true if element is visible.
//
function isVisible(element) {
    return $(element).is(':visible');
}

//
// Marks comment addition as successful.
//
// result The output of the function.
function OnCommentExchangeComplete(result) {
}

//
// Processes an error if completing has failed.
//
function OnCommentExchangeFailed() {
    alert('Failed to save this comment!');
}

//
// Sends comment to server.
//
// commentId The id of the comment.
// commentString The comment itself.
function sendCommentToServer(commentId, commentString) {
    CommentsExchange.AddComment(commentId, commentString, OnCommentExchangeComplete, OnCommentExchangeFailed);
}

//
// Deletes comment from server.
//
// commentId The id of the comment.
function removeCommentFromServer(commentId) {
    CommentsExchange.DeleteComment(commentId, OnCommentExchangeComplete, OnCommentExchangeFailed);
}

// Creates new comment.
//
// el The table cell where the new comment is created.
// id The base id of the comment (diff_linenumber, or base_linenumber).
// stamp Proposed time stamp or null.
// insertAfter Child node after which to insert the new comment. Can be null.
// Important note: HTML generated by this function should be kept in sync with the one generated by
// AddCommentToCell in Default.aspx.cs.
function createNewComment(el, id, stamp, insertAfter) {
    var currentTime = new Date();
    var currentTimeString = RebuildDate(currentTime);
    var commentId = id + '_' + (stamp || currentTime.valueOf()) + COMMENT_TOKEN;

    // This mirrors code in AddCommentToCell in Default.aspx.cs to maintain consistent spacing.
    var html =
  '<div class="Edit">' +
    '<textarea rows="6" cols="0" ' +
        '"onkeyup="textAreaAutoSize(\'{commentId}\');">{commentText}</textarea>' +
    '<div class="Buttons">' +
      '<input class="button" type="button" value="submit" ' +
             'onclick="onSubmitComment(event);"/>' +
      '<input class="button" type="button" value="cancel" ' +
            'onclick="onCancelComment(event);"/>' +
      '<input class="button" type="button" value="remove" ' +
            'onclick="onRemoveComment(event);" style="display:none"/>' +
    '</div>' +
  '</div>' +
  '<div class="Display" style="display:none">' +
    '<div class="Header">' +
      '{userName} on {timeStamp}:' +
    '</div>' +
    '<div class="Body"/>' +
  '</div>';

    html = html.replace(/{commentId}/g, commentId);
    html = html.replace(/{userName}/g, username);
    html = html.replace(/{commentText}/g, COMMENT_PROMPT);
    html = html.replace(/{timeStamp}/g, currentTimeString);

    var newComment = document.createElement('div');
    newComment.id = commentId;
    newComment.className = 'Comment';
    newComment.innerHTML = html;
    $(newComment).hide().fadeOut(0)
		.keyup(function (e) {
			if (e.keyCode == 27) { $(newComment).find('input[value="cancel"]').click(); } // esc
		});

    if (insertAfter)
        newComment = $(insertAfter).after(newComment).next();
    else
        newComment = $(el).append(newComment).children().last();

    var comment = getComment(newComment);
    comment.edit_textarea.css('max-height', ($(window).height() / 2) + 'px');
    comment.edit_textarea.keyup(textAreaAutoSize);
    comment.edit_textarea.keyup();

    comment.edit.find('.button').button();

    showElement(newComment, function () {
        newComment.find('div.Edit > textarea').focus();
        newComment.find('div.Edit > textarea').select();
    });

    last_comment_id = commentId;
}

//
// Removes a comment.
//
// commentId The id of the comment.
function deleteComment(commentId, callback) {
    var comment = $('#' + commentId);
    if (comment.length == 1) {
        removeCommentFromServer(commentId);
        hideElement(comment, function () {
            comment.remove();
            callback && callback();
        });
        return;
    }
    callback && callback();
}

//
// Deletes last comment made, if no interesting content.
//
function deleteLastCommentIfEmpty(callback) {
    if (last_comment_id) {
        var c = getComment(last_comment_id);
        if (c && c.edit_textarea[0].value == COMMENT_PROMPT) {
            deleteComment(last_comment_id, function () {
                last_comment_id = null;
                callback && callback();
            });
            return;
        }
    }
    callback && callback();
}

//
// Flips an existing comment into edit mode.
//
// commentId The id of the comment.
function editExistingComment(commentId) {
    var c = getComment(commentId);
    if (!c)
        return;

    // If it's already in edit mode, just return.
    if (c.edit.length == 1 && !c.edit.is(':hidden'))
        return;

    if (c.edit.length != 1) { // This is a read-only comment_div. We need to create a response.
        var pos = commentId.split("_");
        createNewComment(c.elem.parent()[0], [pos[0], pos[1], pos[2]].join("_"), parseInt(pos[3]) + 1, c.elem[0]);
        return;
    }

    c.edit_textarea.text(c.display_body.text());

    hideElement(c.display, function () {
    	c.edit.find('[value=remove]:button').show();
    	c.edit_textarea.keyup(function (e) {
    		if (e.keyCode == 27) { c.edit.find('input[value="cancel"]').click(); } // esc
    	});
    	showElement(c.edit);
    	c.edit_textarea.focus();
    });
}

//
// Gets the comment ID from an element. Returns undefined if
// no comment ID exists for element.
//
// element The element to retrieve the comment ID from.
function getCommentId(element) {
    var comment = $(element).closest('div.Comment');
    if (comment.length != 1)
        return undefined;

    var index = comment[0].id.indexOf(COMMENT_TOKEN);
    if (index == -1)
        return undefined;

    return comment[0].id.substring(0, index + COMMENT_TOKEN.length);
}

//
// Returns an object containing all comment fields.
//
// element The comment ID or a DOM element within a comment div.
function getComment(element) {
    var comment = jQuery();
    // CommentID string
    if (typeof element === "string") {
        comment = $('#' + element);
    }
    else {
        // jQuery event
        if (element.target) {
            element = element.target;
        }
        // jQuery object
        if (element.jquery) {
            element = element[0];
        }
        // DOM element
        if (element.nodeType) {
            element = $(element);
            if (element.is('div.Comment'))
                comment = element;
            else
                comment = element.closest('div.Comment');
        }
    }

    if (comment.length != 1)
        return null;

    return {
        elem: comment,
        id: comment[0].id,
        display: comment.find('div.Display'),
        display_header: comment.find('div.Display > div.Header'),
        display_body: comment.find('div.Display > div.Body'),
        edit: comment.find('div.Edit'),
        edit_textarea: comment.find('div.Edit > textarea')
    };
}

//
// Submits a comment.
//
// event The event dispatched by the browser.
function onSubmitComment(event) {
    stopEventPropagation(event);

    var c = getComment(event.srcElement || event.target);
    if (!c)
        return null;

    var commentText = c.edit_textarea.val();
    if (!commentText || commentText == COMMENT_PROMPT) {
        deleteComment(c.id);
        return;
    }

    // Copy over the value.
    c.display_body.text(commentText);

    // Show display DIV, hide edit DIV.
    hideElement(c.edit, function () {
        showElement(c.display);
    });

    sendCommentToServer(c.id, c.display_body.text());

    maybeDisplayHints(HINT_FILEVIEW_CLICK_TO_EDIT, HINT_FILEVIEW_NEED_TO_SUBMIT);
}

//
// Rejects a comment.
//
// event The event dispatched by the browser.
function onCancelComment(event) {
    stopEventPropagation(event);

    var c = getComment(event.srcElement || event.target);
    if (!c)
        return;

    if (!c.display_body.text()) {
        deleteComment(c.id);
        return;
    }

    // Show display DIV, hide edit DIV.
    hideElement(c.edit, function () {
        showElement(c.display);
    });
}

//
// Deletes a comment.
//
// event The event dispatched by the browser.
function onRemoveComment(event) {
    stopEventPropagation(event);

    var commentId = getCommentId(event.srcElement || event.target);
    if (!commentId)
        return;

    deleteComment(commentId);
}

function stopEventPropagation(event) {
    if (event.stopPropagation)
        event.stopPropagation();
    else
        event.cancelBubble = true;
}

//
// Processes the mouse click anywhere in the file view.
//
// event The event dispatched by the browser.
// autocollapse User preference whether to remove empty comments
function onMouseClick(event, autoCollapse, clickLineNumberOnly) {
    // If the user was selecting the text, do not open the input box.
    var selectedText = null;
    if (document.selection && document.selection.createRange) {
        var range = document.selection.createRange();
        selectedText = range.text;
    }
    else if (document.getSelection) {
        // The "" is here for Chrome, which returns a selection element
        // even where there is nothing selected.
        selectedText = "" + document.getSelection();
    }

    if (selectedText)
        return;

    stopEventPropagation(event);

    var el = $(event.srcElement || event.target).closest('[id^=diff_],[id^=base_]').get(0);

    if (!el)
        return;

    var id = el.id;

    var pos = id.split("_");
    if (pos.length == 3) {
        // If the user has enabled "click only on line number to add comment", then skip direct clicks.
        if (clickLineNumberOnly)
            return;
        if (autoCollapse)
            deleteLastCommentIfEmpty(function () {
                createNewComment(el, id, null, null);
            });
        else
            createNewComment(el, id, null, null);
    }
    else if (pos.length == 4 &&
             pos[3] == 'linenumber') {
        id = id.substring(0, id.length - '_linenumber'.length);
        el = document.getElementById(id);
        if (!el)
            return;
        if (autoCollapse)
            deleteLastCommentIfEmpty(function () {
                createNewComment(el, id, null, null);
            });
        else
            createNewComment(el, id, null, null);
    }
    else if (pos.length >= 5 &&
             pos[4] == "comment" &&
             el.nodeName != 'INPUT') {
        var commentId = [pos[0], pos[1], pos[2], pos[3], pos[4]].join("_");
        if (autoCollapse && commentId != last_comment_id)
            deleteLastCommentIfEmpty(function () {
                editExistingComment(commentId);
            });
        else
            editExistingComment(commentId);
    }
}