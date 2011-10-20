//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Simon Hall for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//-----------------------------------------------------------------------
// Based on John Resig's Javascript Diff Algorithm, though highly modified
// to suit the needs of differencing programming languages rather than the
// English language.
//
// See http://ejohn.org/projects/javascript-diff-algorithm/ for more details.
//-----------------------------------------------------------------------

function diff_Resig(o, n) {
    var ns = new Array();
    var os = new Array();

    var nsWords = new Array();
    var maxWordLen = 0;

    for (var i = 0; i < n.length; i++) {
        if (ns[n[i]] == null || ns[n[i]].rows == null)
            ns[n[i]] = { rows: new Array() };
        ns[n[i]].rows.push(i);
        nsWords[n[i].length] = (nsWords[n[i].length] || new Array());
        nsWords[n[i].length].push(n[i]);
        maxWordLen = Math.max(maxWordLen, n[i].length);
    }

    for (var i = 0; i < o.length; i++) {
        if (os[o[i]] == null || os[o[i]].rows == null)
            os[o[i]] = { rows: new Array() };
        os[o[i]].rows.push(i);
    }

    function crossLink(i) {
        for (var j = 0; j < n.length; ++j) {
            if (!n[j].text)
                continue;

            for (var k = 0; k < ns[i].rows.length; ++k) {
                if (!((j < ns[i].rows[k]) == (n[j].row < os[i].rows[k]))) {
                    return true;
                }
            }
        }
        return false;
    }

    for (var curLen = maxWordLen; curLen > 0; --curLen) {
        for (var curWord in nsWords[curLen]) {
            var i = nsWords[curLen][curWord];
            if (os[i] &&
                    os[i].rows &&
                    os[i].rows.length == ns[i].rows.length &&
                    !n[ns[i].rows[0]].text) {
                if (!crossLink(i)) {
                    for (k = 0; k < ns[i].rows.length; ++k) {
                        n[ns[i].rows[k]] = { text: n[ns[i].rows[k]], row: os[i].rows[k] };
                        o[os[i].rows[k]] = { text: o[os[i].rows[k]], row: ns[i].rows[k] };
                    }
                }
            }
        }
    }

    for (var i = 0; i < n.length - 1; i++) {
        if (n[i].text != null &&
                n[i + 1].text == null &&
                n[i].row + 1 < o.length &&
                o[n[i].row + 1].text == null &&
                n[i + 1] == o[n[i].row + 1]) {
            n[i + 1] = { text: n[i + 1], row: n[i].row + 1 };
            o[n[i].row + 1] = { text: o[n[i].row + 1], row: i + 1 };
        }
    }

    for (var i = n.length - 1; i > 0; i--) {
        if (n[i].text != null &&
                n[i - 1].text == null &&
                n[i].row > 0 &&
                o[n[i].row - 1].text == null &&
                n[i - 1] == o[n[i].row - 1]) {
            n[i - 1] = { text: n[i - 1], row: n[i].row - 1 };
            o[n[i].row - 1] = { text: o[n[i].row - 1], row: i - 1 };
        }
    }

    return { o: o, n: n };
}

