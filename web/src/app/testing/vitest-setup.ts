/**
 * jsdom implements the DOM but no layout, so `Element.scrollIntoView` is not there at all.
 * The detail panel calls it when the wire pane moves, to bring the matching part of the
 * human view with it. A no-op keeps that behaviour out of the specs rather than out of the
 * component: what is being asserted is which document the pane lands on, not how far the
 * browser scrolled to it.
 */
if (typeof Element !== 'undefined' && !Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = function scrollIntoView(): void {
    // Nothing to scroll: jsdom has no viewport.
  };
}
