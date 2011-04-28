//-----------------------------------------------------------------------
// <copyright>
// Copyright (C) Simon Hall for The Malevich Project.
//
// This file is subject to the terms and conditions of the Microsoft Public License (MS-PL).
// See http://www.microsoft.com/opensource/licenses.mspx#Ms-PL for more details.
// </copyright>
//----------------------------------------------------------------------- 

jQuery.fn.collapsible = function() {
    $(this).each(function() {
        var $this = $(this);
        if ($this.is('fieldset')) {
            $this.width($this.width());
            var legend = $this.find('legend');
            if (legend.length != 1) {
                return;
            }
            {   // Fixup the legend and add icons
                var icon_style = 'padding: 2px; padding-right: 4px;';
                var icon_div = $('<span style="display: inline-block; vertical-align: middle; height: 100%;"/>')
                .append($('<img href="/" src="images/plus-8.png"  class="collapsible_icon_expand" style="' + icon_style + '" />'))
                .append($('<img href="/" src="images/minus-8.png" class="collapsible_icon_collapse" style="' + icon_style + '" />'));
                legend.prepend(icon_div)
                  .find('[class=collapsible_icon_expand]').hide();
            }
            {   // Setup the click event
                $this.find('[class^=collapsible_icon_]').click(function(event) {
                    event.preventDefault();
                    var $this = $($(this).closest('.collapsible'));
                    $this.children('[class=collapsible_element]').slideToggle(function() {
                    $this.find('[class^=collapsible_icon_]').toggle();
                    });
                });
            }
            {   // Is it pre-collapsed?
                if ($this.is('.collapsed')) {
                    $this.removeClass('.collapsed');
                    $this.find('[class^=collapsible_]').toggle();
                }
            }
        }
    });
}


//
// Scrolls to an element
//
var scrollToElementDuration = 500;

function scrollToElement(el, cb) {
    var scrollTop = $(window).scrollTop();
    var scrollBottom = scrollTop + $(window).height();
    var context = getScrollContext();

    var docHeight = $(document).height();
    var winHeight = $(window).height();
    var elHeight = $(el).height();
    var elOffset = $(el).offset().top;
    var offset;
    if (elOffset < scrollTop)
        offset = Math.max(-elOffset, -winHeight + Math.min(elHeight + context, winHeight - context));
    else
        offset = -context;

    $(':animated').stop(true, true);
    $.scrollTo(el, scrollToElementDuration, {
        offset: {
            top: offset,
            left: 0
        },
        onAfter: cb,
        axis: 'y'
});
}

//
// Returns true if some portion of the element is visible; false otherwise.
//
function isElementVisible(el) {
    var scrollTop = $(document).scrollTop();
    var scrollBot = scrollTop + $(window).height();
    var elTop = $(el).offset().top;
    var elBot = elTop + $(el).height();

    return scrollTop < elBot && scrollBot > elTop;
}

// Apply theme to page
function applyTheme() {
    $('.collapsible').collapsible();
    $('.Accordion').accordion({ header: '> a.header' });
    $('.buttons > *,.button').button();
}