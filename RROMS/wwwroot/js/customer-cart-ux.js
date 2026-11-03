/* Review-photo fallback only. No menu quantity badges or requests. */
(() => {
    'use strict';
    if (window.yoruReviewPhotoFallback) return;
    window.yoruReviewPhotoFallback = true;
    document.addEventListener('error', event => {
        const img = event.target;
        if (!(img instanceof HTMLImageElement) || !img.matches('.checkout-review__photo')) return;
        const fallback = document.createElement('span');
        fallback.className = 'checkout-review__photo checkout-review__photo--placeholder';
        fallback.setAttribute('aria-hidden', 'true'); fallback.textContent = '夜';
        img.replaceWith(fallback);
    }, true);
})();
