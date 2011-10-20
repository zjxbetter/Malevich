//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Sergey Solyanik for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 
//
// Javascript module to recalculate times to browser local.
//

//
// Is time a 24-hour format or AM/PM?
//
var TIME_FORMAT_24HR = ComputeDateFormat();

//
// Recomputes all date times from UTC to local.
//
function RebuildDate(date) {
    var month = date.getMonth() + 1
    var year = date.getYear()
    var day = date.getDate()

    if (day < 10) day = '0' + day
    if (month < 10) month = '0' + month
    if (year < 1000) year += 1900

    var hour = date.getHours();
    var minute = date.getMinutes();
    var seconds = date.getSeconds();

    var suffix = '';
    if (TIME_FORMAT_24HR) {
        if (hour < 10) hour = '0' + hour;
    }
    else {
        if (hour < 12) suffix = ' AM'; else suffix = ' PM';
        if (hour == 0) hour = 12;
        if (hour > 12) hour -= 12;
        if (hour < 10) hour = '  ' + hour;
    }

    if (minute < 10) minute = '0' + minute;
    if (seconds < 10) seconds = '0' + seconds;

    return [month, '/', day, '/', year, ' ', hour, ':', minute, ':', seconds, suffix].join('');
}

function RebuildDates() {
    var dates = $('[name=timestamp]').each(function() {
        $(this).text(RebuildDate(new Date($(this).attr('ticks') * 1000)));
    });
}

//
// Checks if the date in the current locale is AM/PM or 24 hours.
//
function ComputeDateFormat() {
    var dateString = (new Date()).toLocaleTimeString();
    if (dateString.indexOf(' AM') == -1 && dateString.indexOf(' PM') == -1)
        return true;
    return false;
}

//
// Makes RebuildDates be called on page load.
//
if (window.attachEvent) {
    window.attachEvent('onload', RebuildDates);
}
else {
    if (window.onload) {
        var curronload = window.onload;
        window.onload = function() {
            curronload();
            RebuildDates();
        };
    }
    else {
        window.onload = RebuildDates;
    }
}
