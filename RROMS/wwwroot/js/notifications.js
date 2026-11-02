// Owner: Member 4 — US37 in-app notifications (all customer pages, loaded by _NotificationBell.cshtml).
// - Fills the bell dropdown with the latest 5 notices (from /Customer/Notifications?handler=Summary).
// - Checks again every 30 seconds (and when the tab becomes visible) and updates the red badge.
// - Shows the shared YORU toast for notices that arrived since the last check, on whichever page
//   the customer is on. Pages that already show a live order (data-watching-order="<orderId>",
//   Member 3's order page) get no second toast for that order; the badge still updates.
// The status change itself is never affected: this script only reads.
(function () {
    'use strict';

    var bell = document.querySelector('[data-ntf-bell]');
    if (!bell) return;

    var summaryUrl = bell.getAttribute('data-summary-url');
    var badge = bell.querySelector('[data-ntf-badge]');
    var list = bell.querySelector('[data-ntf-list]');
    var markAll = bell.querySelector('[data-ntf-markall]');
    var POLL_MS = 30000;
    var SEEN_KEY = 'rroms.ntf.lastSeenId';
    var busy = false;

    function getSeen() {
        try { var v = sessionStorage.getItem(SEEN_KEY); return v === null ? null : parseInt(v, 10) || 0; }
        catch (e) { return null; }
    }
    function setSeen(id) {
        try { sessionStorage.setItem(SEEN_KEY, String(id)); } catch (e) { /* private mode: no toasts history */ }
    }

    function iconFor(n) {
        var cancelled = /cancel/i.test(n.title);
        switch (n.type) {
            case 'Order': return cancelled ? 'bi-bag-x' : 'bi-bag';
            case 'Reservation': return cancelled ? 'bi-calendar-x' : 'bi-calendar-check';
            case 'Payment': return 'bi-receipt';
            case 'Promotion': return 'bi-megaphone';
            default: return 'bi-info-circle';
        }
    }
    function toneFor(n) {
        return n.type === 'Order' ? 'is-order' : n.type === 'Payment' ? 'is-payment' : 'is-plain';
    }

    // "Just now", "8 min", "2 h", "Yesterday", "30 Oct"
    function when(iso) {
        var d = new Date(iso);
        if (isNaN(d)) return '';
        var mins = Math.round((Date.now() - d.getTime()) / 60000);
        if (mins < 1) return 'Just now';
        if (mins < 60) return mins + ' min';
        var today = new Date(); today.setHours(0, 0, 0, 0);
        if (d >= today) return Math.floor(mins / 60) + ' h';
        var yesterday = new Date(today); yesterday.setDate(today.getDate() - 1);
        if (d >= yesterday) return 'Yesterday';
        return d.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
    }

    function el(tag, cls, text) {
        var e = document.createElement(tag);
        if (cls) e.className = cls;
        if (text != null) e.textContent = text;
        return e;
    }
    function icon(n) {
        var wrap = el('span', 'ntf-icon ' + toneFor(n));
        wrap.setAttribute('aria-hidden', 'true');
        wrap.appendChild(el('i', 'bi ' + iconFor(n)));
        return wrap;
    }

    function setBadge(unread) {
        if (badge) {
            badge.textContent = unread > 9 ? '9+' : String(unread);
            badge.hidden = unread <= 0;
        }
        var link = bell.querySelector('.nav-link');
        if (link) link.setAttribute('aria-label', 'Notifications, ' + unread + ' unread');
        if (markAll) markAll.hidden = unread <= 0;
    }

    function renderList(items) {
        if (!list) return;
        list.textContent = '';
        if (!items.length) {
            list.appendChild(el('li', 'ntf-menu-empty', 'No notices yet.'));
            return;
        }
        items.forEach(function (n) {
            var li = el('li');
            var a = el('a', 'ntf-menu-item ' + (n.isRead ? 'is-read' : 'is-unread'));
            a.href = n.link;
            a.appendChild(icon(n));
            var body = el('div');
            var top = el('div', 'ntf-menu-top');
            top.appendChild(el('span', null, n.title));
            var t = el('time', null, when(n.createdAt));
            t.dateTime = n.createdAt;
            top.appendChild(t);
            body.appendChild(top);
            body.appendChild(el('div', 'ntf-menu-msg', n.message));
            a.appendChild(body);
            li.appendChild(a);
            list.appendChild(li);
        });
    }

    // Same markup as the toast in _Layout.cshtml, so it looks and moves the same.
    function showToast(n, extra) {
        var old = document.querySelector('[data-ntf-toast]');
        if (old) old.remove();

        var wrap = el('div', 'yoru-toast-wrap');
        wrap.setAttribute('data-ntf-toast', '');
        var toast = el('div', 'yoru-toast yoru-toast--info');
        toast.setAttribute('role', 'status');
        toast.setAttribute('aria-live', 'polite');

        var seal = el('span', 'yoru-toast-seal');
        seal.setAttribute('aria-hidden', 'true');
        seal.appendChild(el('i', 'bi ' + iconFor(n)));
        toast.appendChild(seal);

        var body = el('div', 'yoru-toast-body');
        var title = el('p', 'yoru-toast-title');
        var jp = el('span', 'yoru-toast-jp', 'お知らせ');
        jp.lang = 'ja';
        title.appendChild(jp);
        title.appendChild(document.createTextNode(n.title));
        body.appendChild(title);
        var msg = el('p', 'yoru-toast-msg', n.message + (extra > 0 ? ' (+' + extra + ' more)' : ''));
        var view = el('a', 'ntf-toast-link', 'View');
        view.href = n.link;
        msg.appendChild(view);
        body.appendChild(msg);
        toast.appendChild(body);

        var close = el('button', 'yoru-toast-close');
        close.type = 'button';
        close.setAttribute('aria-label', 'Close notification');
        close.appendChild(el('i', 'bi bi-x-lg'));
        toast.appendChild(close);
        var bar = el('span', 'yoru-toast-bar');
        bar.setAttribute('aria-hidden', 'true');
        toast.appendChild(bar);

        wrap.appendChild(toast);
        document.body.appendChild(wrap);

        function hide() {
            if (toast.classList.contains('is-leaving')) return;
            toast.classList.add('is-leaving');
            setTimeout(function () { wrap.remove(); }, 400);
        }
        bar.addEventListener('animationend', hide);
        close.addEventListener('click', hide);
    }

    function watchingOrder(orderId) {
        return orderId != null && document.querySelector('[data-watching-order="' + orderId + '"]') !== null;
    }

    function handleNew(items) {
        var newest = items.length ? items[0].id : 0;
        var seen = getSeen();
        if (seen === null) { setSeen(newest); return; }   // first page in this tab: don't toast old notices
        if (newest <= seen) return;

        var fresh = items.filter(function (n) {
            return n.id > seen && !n.isRead && !watchingOrder(n.orderId);
        });
        setSeen(newest);
        if (fresh.length) showToast(fresh[0], fresh.length - 1);
    }

    function refresh() {
        if (busy || !summaryUrl) return;
        busy = true;
        fetch(summaryUrl, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) {
                var type = r.headers.get('content-type') || '';
                if (!r.ok || type.indexOf('json') === -1) throw new Error('signed out');
                return r.json();
            })
            .then(function (data) {
                setBadge(data.unread || 0);
                renderList(data.items || []);
                handleNew(data.items || []);
            })
            .catch(function () { /* signed out or offline: keep what is shown */ })
            .then(function () { busy = false; });
    }

    // Mark all as read without leaving the page.
    if (markAll) {
        markAll.addEventListener('submit', function (e) {
            e.preventDefault();
            fetch(markAll.action, { method: 'POST', body: new FormData(markAll), credentials: 'same-origin' })
                .then(refresh, refresh);
        });
    }

    bell.addEventListener('show.bs.dropdown', refresh);
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') refresh();
    });
    setInterval(function () {
        if (document.visibilityState === 'visible') refresh();
    }, POLL_MS);
    refresh();

    // US37 step 2 (after the shared SignalR setup is in develop): NotificationHub pushes
    // "NotificationReceived" and this file calls refresh() straight away.
    window.rromsNotifications = { refresh: refresh };
})();
