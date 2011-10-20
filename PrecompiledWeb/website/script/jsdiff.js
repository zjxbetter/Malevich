//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Simon Hall for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 

String.prototype.startsWith = function (str) {
    return (this.match('^' + str) == str);
}

String.prototype.endsWith = function(str) {
    return (this.match(str + "$") == str);
}

function unescapeHTML(html) {
    return $(document.createElement("DIV")).html('<pre>' + html + '</pre>').text();
}

function escapeHTML(text) {
    return $('<pre>').text('(' + text + ')').html().slice(1, -1);
}

function GetLineText(el) {
    if (!el || !$(el).is('td.Txt')) {
        return;
    }

    var res = $(el).find('pre').text();
    return res;
}

function SetLineText(el, text) {
    if (!el || !$(el).is('td.Txt')) {
        return;
    }

    var tdNode = $(el);

    var newPreNode = $('<div>').html('<pre class="Code">' + text + '</pre>').children().first();

    // insert into DOM
    var existingPreNode = tdNode.children('pre').first();
    if (existingPreNode.length == 0) {
        tdNode.prepend(newPreNode);
    }
    else {
        existingPreNode.replaceWith(newPreNode);
    }
}

function encloseString(str, tag, className) {
    className = className ? ' class="' + className + '"' : '';
    if (str == ' ') {
        str = '<' + tag + className + '> </' + tag + '>'
    }
    else {
        var sp = str.split('\n');
        for (var x = 0; x < sp.length; ++x) {
            var ws = sp[x].match(/^(\s*)(.*)(\s*)$/);
            sp[x] = (ws[1] || '') + '<' + tag + className + '>' + (escapeHTML(ws[2]) || '') +
                    '</' + tag + className + '>' + (ws[3] || '');
        }
        str = sp.join('\n');
    }

    return str;
}

function tidyTags(str, tag) {
    return str.replace(new RegExp('<\/' + tag + '><' + tag + '>', 'gm'), '')
              .replace(new RegExp('<' + tag + '><\/' + tag + '>', 'gm'), '');
}

/**
* Determine the common prefix of two strings
* @param {string} text1 First string.
* @param {string} text2 Second string.
* @return {number} The number of characters common to the start of each
*     string.
*/
commonPrefix = function(text1, text2) {
    // Quick check for common null cases.
    if (!text1 || !text2 || text1.charCodeAt(0) !== text2.charCodeAt(0)) {
        return 0;
    }
    // Binary search.
    // Performance analysis: http://neil.fraser.name/news/2007/10/09/
    var pointermin = 0;
    var pointermax = Math.min(text1.length, text2.length);
    var pointermid = pointermax;
    var pointerstart = 0;
    while (pointermin < pointermid) {
        if (text1.substring(pointerstart, pointermid) ==
        text2.substring(pointerstart, pointermid)) {
            pointermin = pointermid;
            pointerstart = pointermin;
        } else {
            pointermax = pointermid;
        }
        pointermid = Math.floor((pointermax - pointermin) / 2 + pointermin);
    }
    return pointermid;
};

/**
* Determine the common suffix of two strings
* @param {string} text1 First string.
* @param {string} text2 Second string.
* @return {number} The number of characters common to the end of each string.
*/
commonSuffix = function(text1, text2) {
    // Quick check for common null cases.
    if (!text1 || !text2 || text1.charCodeAt(text1.length - 1) !==
                          text2.charCodeAt(text2.length - 1)) {
        return 0;
    }
    // Binary search.
    // Performance analysis: http://neil.fraser.name/news/2007/10/09/
    var pointermin = 0;
    var pointermax = Math.min(text1.length, text2.length);
    var pointermid = pointermax;
    var pointerend = 0;
    while (pointermin < pointermid) {
        if (text1.substring(text1.length - pointermid, text1.length - pointerend) ==
        text2.substring(text2.length - pointermid, text2.length - pointerend)) {
            pointermin = pointermid;
            pointerend = pointermin;
        } else {
            pointermax = pointermid;
        }
        pointermid = Math.floor((pointermax - pointermin) / 2 + pointermin);
    }
    return pointermid;
};


//var code_word_re = new RegExp('^(([A-Z]?[a-z]+)|([0-9]+)|(\\s+))');
var code_word_re = new RegExp('^(([a-zA-Z][a-zA-Z0-9_]*)|(\\s+))');

function splitWords(str, re) {
    var out = new Array();
    out.push(str);
    while (out[out.length - 1].length > 1) {
        var rem = out.pop();
        var wordMatch = rem.match(re);
        var wordLen = wordMatch ? wordMatch[0].length : 1;
        out.push(rem.substr(0, wordLen));
        out.push(rem.substr(wordLen));
    }
    return out;
}

function diffWords_Resig_formatted(o, n) {
    return diff_Resig_formatted(
        diff_Resig(o == "" ? [] : splitWords(o, code_word_re),
                   n == "" ? [] : splitWords(n, code_word_re)));

}

function diffChars_Resig_formatted(o, n) {
    return diff_Resig_formatted(
        diff_Resig(o == "" ? [] : o.split(""),
                   n == "" ? [] : n.split("")));
}

function diff_Resig_formatted(out) {
    var str =
    {
        base: "",
        diff: "",
        inline: "",
        sameCount: 0
    };

    var oi = 0;
    var ni = 0;

    var curBaseLineLen = 0;
    var curDiffLineLen = 0;

    while (oi < out.o.length || ni < out.n.length) {
        var strDel = '';
        var strIns = '';

        while (oi < out.o.length && out.o[oi].text != null &&
               ni < out.n.length && out.n[ni].text != null) {
            strDel += out.o[oi].text;
            strIns += out.n[ni].text;
            ++oi;
            ++ni;
        }

        while (oi < out.o.length && out.o[oi].text == null) {
            strDel += out.o[oi];
            ++oi;
        }

        while (ni < out.n.length && out.n[ni].text == null) {
            strIns += out.n[ni];
            ++ni;
        }

        var numPrefix = commonPrefix(strDel, strIns);
        var prefix = strDel.substr(0, numPrefix);
        var prefixHTML = escapeHTML(prefix);

        var numSuffix = commonSuffix(strDel.substr(numPrefix), strIns.substr(numPrefix));
        var suffix = strDel.substr(strDel.length - numSuffix)
        var suffixHTML = escapeHTML(suffix);

        str.base += prefixHTML;
        str.diff += prefixHTML;
        str.inline += prefixHTML;
        str.sameCount += prefix.replace(/\s+/mg, ' ').length;

        str.base += encloseString(strDel.substring(numPrefix, strDel.length - numSuffix), 'del');
        str.diff += encloseString(strIns.substring(numPrefix, strIns.length - numSuffix), 'ins');
        str.inline += encloseString(strDel.substring(numPrefix, strDel.length - numSuffix), 'del');
        str.inline += encloseString(strIns.substring(numPrefix, strIns.length - numSuffix), 'ins');

        str.base += suffixHTML;
        str.diff += suffixHTML;
        str.inline += suffixHTML;
        str.sameCount += suffix.replace(/\s+/mg, ' ').length;
    }

    str.base = tidyTags(str.base, 'del');
    str.diff = tidyTags(str.diff, 'ins');
    str.inline = tidyTags(str.inline, 'del');
    str.inline = tidyTags(str.inline, 'ins');

    return str;
}

var fastDiffFunc = diffWords_Resig_formatted;
var slowDiffFunc = fastDiffFunc;

var workItemQueue = new Array();
function QueueWorkItem(fn, cb) {
    workItemQueue.unshift([fn, cb]);
    if (workItemQueue.length == 1)
        setTimeout('ExecuteQueuedWorkItem()', 0);
}
function ExecuteQueuedWorkItem() {
    var item = workItemQueue.pop();
    if (item) {
        item[0]();
        if (item[1]) {
            item[1]();
        }
        setTimeout('ExecuteQueuedWorkItem()', 0);
    }
}

function GetReplaceLineCB(el, newText) {
    return function() {
        if (!newText)
            return;
        SetLineText(el, newText);
        var insdel = $(el).find('ins').add($(el).find('del'));
        for (var i = 0; i < insdel.length; ++i) {
            var curEl = $(insdel[i]);
            var curParent = curEl;
            var bkgClr = curParent.css('background-color');
            while (curParent.length && (!bkgClr || bkgClr == 'transparent')) {
                curParent = curParent.parent();
                bkgClr = curParent.css('background-color');
            }
            curEl.css('backgroundColor', 'yellow')
                .animate({ backgroundColor: bkgClr }, { duration: 1000, queue: false });
        }
    };
};

function diffAnnotateElem(elem) {
    var tgroup = $(elem).closest('tbody.Changed:not([Annotated])');
    if (tgroup.length != 1)
        return;

    tgroup.attr('Annotated', 'true');

    var rows = tgroup.children('tr');

    // Get the base lines
    var base = {
        txt: rows.find('td.Txt.Base'),
        num: rows.find('td.Num.Base')
    };
    base.str = base.txt.map(function(idx, dom) {
        return GetLineText(dom);
    }).get();

    // Get the diff lines
    var diff = {
        txt: rows.find('td.Txt.Diff'),
        num: rows.find('td.Num.Diff')
    };
    diff.str = diff.txt.map(function(idx, dom) {
        return GetLineText(dom);
    }).get();

    var diffFunc = Math.max(base.str.length, diff.str.length) < 15 ? slowDiffFunc : fastDiffFunc;
    var diffOut = diffFunc(base.str.join('\n'), diff.str.join('\n'));
    if (((diffOut.sameCount * 100) / base.str.join('\n').replace(/\s+/mg, ' ').length) > 25) {
        var baseStr = diffOut.base.split('\n');
        base.txt.each(function(idx, tdEl) {
            $(tdEl).queue('AnnotateDiffQueue', GetReplaceLineCB(tdEl, baseStr[idx]));
        });

        var diffStr = diffOut.diff.split('\n');
        diff.txt.each(function(idx, tdEl) {
            $(tdEl).queue('AnnotateDiffQueue', GetReplaceLineCB(tdEl, diffStr[idx]));
        });
        tgroup.find('td.Txt').dequeue('AnnotateDiffQueue');
    }
}

function diffAnnotate() {
    var fileview = $('fileview');
    if (!fileview) {
        return;
    }
    var tableElems = fileview.childNodes;
    for (var i = 0; i < tableElems.length; ++i) {
        var tableElem = tableElems[i];
        if (tableElem.className.indexOf('RowGroup') != -1 && tableElem.className.indexOf('Changed') != -1) {
            diffAnnotateElem(tableElem);
        }
    }
}