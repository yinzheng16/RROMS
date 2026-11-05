(() => {
  'use strict';
  const page = document.querySelector('[data-history-page]');
  if (page) {
    // Progressive enhancement: real selects remain the submitted form values and no-JS fallback.
    const filters = page.querySelector('.oj-controls');
    const search = filters?.querySelector('[name="search"]');
    const searchHint = filters?.querySelector('[data-search-hint]');
    let openDropdown = null;
    // Comparing to the loaded form also permits restoring default choices.
    // A default value such as newest is still a valid change from oldest.
    const filterNames = ['dining', 'status', 'statusGroup', 'sort', 'from', 'to'];
    const filterSnapshot = () => {
      const data = new FormData(filters);
      return JSON.stringify(filterNames.map(name => data.get(name) || ''));
    };
    const initialFilters = filters ? filterSnapshot() : null;
    const filtersChanged = () => filters && filterSnapshot() !== initialFilters;
    function hideSearchHint() {
      if (searchHint) searchHint.hidden = true;
      search?.removeAttribute('aria-invalid');
    }
    function showSearchHint() {
      if (searchHint) searchHint.hidden = false;
      search?.setAttribute('aria-invalid', 'true');
      search?.focus({preventScroll: true});
    }
    // No Apply button: a filter change updates the results at once; typing searches after a short pause.
    // The URL is updated, so Back/Forward, refresh and shared links keep the same results.
    const defaults = value => ['All', 'newest', 'False', 'false'].includes(value) ? '' : value;
    function filterUrl() {
      const url = new URL(page.dataset.historyUrl || window.location.pathname, window.location.origin);
      new FormData(filters).forEach((value, key) => {
        const text = String(value).trim();
        if (defaults(text)) url.searchParams.set(key, text);
      });
      return url.href;
    }
    let filterTimer = null, lastFilterUrl = null;
    function applyFilters() {
      window.clearTimeout(filterTimer);
      const href = filterUrl();
      if (href === lastFilterUrl) return;
      lastFilterUrl = href;
      hideSearchHint();
      loadPage(href, true, {scroll: false});
    }
    search?.addEventListener('input', () => {
      hideSearchHint();
      window.clearTimeout(filterTimer);
      filterTimer = window.setTimeout(applyFilters, 350);
    });
    search?.addEventListener('keydown', event => {
      if (event.key === 'Enter') { event.preventDefault(); applyFilters(); }
    });
    filters?.addEventListener('change', event => {
      if (event.target === search) return;
      applyFilters();
    });
    filters?.addEventListener('submit', event => { event.preventDefault(); applyFilters(); });
    filters?.querySelectorAll('.oj-tool-field select').forEach((select, number) => {
      const label = select.closest('.oj-tool-field');
      const wrapper = document.createElement('div');
      wrapper.className = label.className + ' oj-custom-select';
      label.replaceWith(wrapper);
      while (label.firstChild) wrapper.append(label.firstChild);
      const name = wrapper.querySelector('.oj-sr-only')?.textContent || 'Filter';
      const button = document.createElement('button');
      button.type = 'button'; button.className = 'oj-select-trigger';
      button.setAttribute('role', 'combobox'); button.setAttribute('aria-label', name);
      button.setAttribute('aria-haspopup', 'listbox'); button.setAttribute('aria-expanded', 'false');
      const text = document.createElement('span');
      const chevron = document.createElement('span'); chevron.className = 'oj-select-chevron';
      chevron.textContent = '';  chevron.setAttribute('aria-hidden', 'true');
      button.append(text, chevron);
      const list = document.createElement('div');
      list.id = 'oj-select-list-' + number; list.className = 'oj-select-options'; list.hidden = true;
      list.setAttribute('role', 'listbox'); list.setAttribute('aria-label', name);
      button.setAttribute('aria-controls', list.id);
      let active = select.selectedIndex, typeText = '', typedAt = 0;
      const options = Array.from(select.options).map((option, index) => {
        const node = document.createElement('div');
        node.id = list.id + '-option-' + index; node.className = 'oj-select-option';
        node.setAttribute('role', 'option'); node.textContent = option.textContent;
        node.addEventListener('pointerdown', event => event.preventDefault());
        node.addEventListener('pointermove', () => setActive(index));
        node.addEventListener('click', () => choose(index));
        list.append(node); return node;
      });
      function sync() {
        text.textContent = select.options[select.selectedIndex]?.textContent || '';
        button.title = text.textContent;
        options.forEach((option, index) => option.setAttribute('aria-selected', String(index === select.selectedIndex)));
      }
      function setActive(index) {
        active = Math.max(0, Math.min(index, options.length - 1));
        options.forEach((option, i) => option.classList.toggle('is-focused', i === active));
        button.setAttribute('aria-activedescendant', options[active].id);
        const option = options[active];
        if (option.offsetTop < list.scrollTop) list.scrollTop = option.offsetTop;
        else if (option.offsetTop + option.offsetHeight > list.scrollTop + list.clientHeight)
          list.scrollTop = option.offsetTop + option.offsetHeight - list.clientHeight;
      }
      function close() {
        list.hidden = true; button.setAttribute('aria-expanded', 'false');
        button.removeAttribute('aria-activedescendant'); wrapper.classList.remove('is-open');
        if (openDropdown?.button === button) openDropdown = null;
      }
      function open() {
        openDropdown?.close();
        list.hidden = false; button.setAttribute('aria-expanded', 'true'); wrapper.classList.add('is-open');
        openDropdown = {button, wrapper, close}; setActive(select.selectedIndex);
      }
      function choose(index) {
        select.selectedIndex = index; sync(); close();
        select.dispatchEvent(new Event('change', {bubbles: true})); button.focus({preventScroll: true});
      }
      button.addEventListener('click', () => list.hidden ? open() : close());
      button.addEventListener('keydown', event => {
        if (event.key === 'Escape') { if (!list.hidden) { event.preventDefault(); event.stopPropagation(); close(); } return; }
        if (event.key === 'Tab') { close(); return; }
        if (['ArrowDown', 'ArrowUp', 'Home', 'End', 'Enter', ' '].includes(event.key)) {
          event.preventDefault();
          if (list.hidden) { open(); if (event.key === 'Home') setActive(0); if (event.key === 'End') setActive(options.length - 1); return; }
          if (event.key === 'Enter' || event.key === ' ') choose(active);
          else setActive(event.key === 'Home' ? 0 : event.key === 'End' ? options.length - 1 : active + (event.key === 'ArrowDown' ? 1 : -1));
        } else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
          event.preventDefault();
          const now = Date.now(); typeText = now - typedAt > 700 ? event.key : typeText + event.key; typedAt = now;
          if (list.hidden) open();
          const match = Array.from(select.options).findIndex(option => option.textContent.toLowerCase().startsWith(typeText.toLowerCase()));
          if (match >= 0) setActive(match);
        }
      });
      wrapper.addEventListener('focusout', event => { if (!wrapper.contains(event.relatedTarget)) close(); });
      select.addEventListener('change', sync);
      select.hidden = true; select.setAttribute('aria-hidden', 'true');
      wrapper.append(button, list); sync();
    });
    document.addEventListener('pointerdown', event => {
      if (openDropdown && !openDropdown.wrapper.contains(event.target)) openDropdown.close();
    });
    // Shared calendar for From / To. Native date inputs remain the real submitted values.
    const dateRange = filters?.querySelector('.oj-date-range');
    if (dateRange) {
      const monthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
      const today = () => { const value = new Date(); return makeDate(value.getFullYear(), value.getMonth(), value.getDate()); };
      function makeDate(year, month, day) {
        const result = new Date(0); result.setHours(12, 0, 0, 0); result.setFullYear(year, month, day); return result;
      }
      function stamp(date) {
        return String(date.getFullYear()).padStart(4, '0') + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
      }
      function parse(value) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value || '')) return null;
        const [year, month, day] = value.split('-').map(Number);
        const result = makeDate(year, month - 1, day);
        return year >= 1 && year <= 9999 && stamp(result) === value ? result : null;
      }
      const allowed = value => value.getFullYear() >= 1 && value.getFullYear() <= 9999;
      const calendar = document.createElement('div');
      calendar.className = 'oj-calendar'; calendar.id = 'oj-order-calendar'; calendar.hidden = true;
      calendar.setAttribute('role', 'dialog'); calendar.setAttribute('aria-label', 'Choose an order date');
      calendar.innerHTML = `
        <div class="oj-cal-topline"><span data-cal-field>Order date</span><button type="button" class="oj-cal-close" aria-label="Close calendar">×</button></div>
        <div class="oj-cal-header">
          <button type="button" class="oj-cal-nav" data-cal-prev aria-label="Previous month">‹</button>
          <button type="button" class="oj-cal-month" data-cal-month aria-label="Choose month" aria-expanded="false"></button>
          <label class="oj-cal-year-label"><span class="oj-sr-only">Calendar year</span><input class="oj-cal-year" type="number" min="1" max="9999" aria-label="Calendar year" /></label>
          <button type="button" class="oj-cal-nav" data-cal-next aria-label="Next month">›</button>
        </div>
        <span class="oj-sr-only" data-cal-announcement aria-live="polite"></span>
        <div class="oj-cal-months" data-cal-months hidden></div>
        <div class="oj-cal-weekdays" aria-hidden="true"><span>Su</span><span>Mo</span><span>Tu</span><span>We</span><span>Th</span><span>Fr</span><span>Sa</span></div>
        <div class="oj-cal-days" data-cal-days role="grid" aria-label="Calendar dates"></div>
        <div class="oj-cal-footer"><button type="button" data-cal-clear>Clear date</button><button type="button" data-cal-today>Today</button></div>`;
      dateRange.append(calendar);
      const monthButton = calendar.querySelector('[data-cal-month]');
      const monthPanel = calendar.querySelector('[data-cal-months]');
      const yearInput = calendar.querySelector('.oj-cal-year');
      const grid = calendar.querySelector('[data-cal-days]');
      const fields = [];
      let activeField = null, view = today(), focused = today();
      function closeCalendar(restoreFocus = false) {
        calendar.hidden = true; monthPanel.hidden = true;
        monthButton.setAttribute('aria-expanded', 'false');
        if (activeField) activeField.button.setAttribute('aria-expanded', 'false');
        dateRange.classList.remove('is-calendar-open');
        if (openDropdown?.wrapper === dateRange) openDropdown = null;
        if (restoreFocus) activeField?.button.focus({preventScroll: true});
      }
      function syncField(field) {
        const selected = parse(field.input.value);
        field.button.textContent = selected
          ? selected.toLocaleDateString('en-GB', {day: '2-digit', month: 'short', year: 'numeric'})
          : field.input.name === 'from' ? 'Start date' : 'End date';
        field.button.classList.toggle('has-date', Boolean(selected));
        field.button.title = field.button.textContent;
      }
      function choose(date) {
        activeField.input.value = date ? stamp(date) : '';
        syncField(activeField); activeField.input.dispatchEvent(new Event('change', {bubbles: true}));
        closeCalendar(true);
      }
      function focusDay() {
        grid.querySelector('[tabindex="0"]')?.focus({preventScroll: true});
      }
      function render() {
        const year = view.getFullYear(), month = view.getMonth();
        monthButton.textContent = monthNames[month]; yearInput.value = year;
        calendar.querySelector('[data-cal-field]').textContent = activeField.input.name === 'from' ? 'Start date' : 'End date';
        calendar.setAttribute('aria-label', 'Choose ' + (activeField.input.name === 'from' ? 'start date' : 'end date'));
        calendar.querySelector('[data-cal-announcement]').textContent = monthNames[month] + ' ' + year;
        calendar.querySelector('[data-cal-prev]').disabled = year === 1 && month === 0;
        calendar.querySelector('[data-cal-next]').disabled = year === 9999 && month === 11;
        const selected = activeField.input.value, now = stamp(today());
        const from = fields.find(field => field.input.name === 'from')?.input.value;
        const to = fields.find(field => field.input.name === 'to')?.input.value;
        const first = makeDate(year, month, 1);
        const start = makeDate(year, month, 1 - first.getDay());
        grid.replaceChildren();
        for (let week = 0; week < 6; week++) {
          const row = document.createElement('div'); row.className = 'oj-cal-week'; row.setAttribute('role', 'row');
          for (let column = 0; column < 7; column++) {
            const date = makeDate(start.getFullYear(), start.getMonth(), start.getDate() + week * 7 + column);
            const button = document.createElement('button'); button.type = 'button';
            button.className = 'oj-cal-day'; button.setAttribute('role', 'gridcell');
            if (!allowed(date)) { button.disabled = true; button.tabIndex = -1; button.textContent = ''; row.append(button); continue; }
            const value = stamp(date); button.textContent = date.getDate(); button.dataset.date = value;
            button.setAttribute('aria-label', date.toLocaleDateString('en-GB', {weekday: 'long', day: 'numeric', month: 'long', year: 'numeric'}));
            button.setAttribute('aria-selected', String(value === selected));
            if (value === now) button.setAttribute('aria-current', 'date');
            button.classList.toggle('is-other-month', date.getMonth() !== month);
            button.classList.toggle('is-in-range', Boolean(from && to && from <= to && value > from && value < to));
            button.classList.toggle('is-range-edge', Boolean(value === from || value === to));
            button.tabIndex = value === stamp(focused) ? 0 : -1;
            button.addEventListener('click', () => choose(date)); row.append(button);
          }
          grid.append(row);
        }
        if (!grid.querySelector('[tabindex="0"]')) {
          const firstDay = grid.querySelector('[data-date="' + stamp(first) + '"]');
          if (firstDay) { firstDay.tabIndex = 0; focused = first; }
        }
        monthPanel.querySelectorAll('button').forEach((button, index) => button.setAttribute('aria-pressed', String(index === month)));
      }
      function openCalendar(field) {
        if (!calendar.hidden && activeField === field) { closeCalendar(); return; }
        openDropdown?.close();
        activeField = field;
        view = parse(field.input.value) || parse(fields.find(item => item.input.name === 'from')?.input.value) || today();
        focused = view; calendar.hidden = false; monthPanel.hidden = true;
        dateRange.classList.add('is-calendar-open');
        calendar.classList.toggle('is-end-date', field.input.name === 'to');
        field.button.setAttribute('aria-expanded', 'true');
        openDropdown = {button: field.button, wrapper: dateRange, close: () => closeCalendar()};
        render(); focusDay();
      }
      Array.from(dateRange.querySelectorAll('input[type="date"]')).forEach(input => {
        const label = input.closest('label');
        const wrapper = document.createElement('div'); wrapper.className = 'oj-date-field';
        label.replaceWith(wrapper); while (label.firstChild) wrapper.append(label.firstChild);
        const button = document.createElement('button'); button.type = 'button'; button.className = 'oj-date-trigger';
        button.setAttribute('aria-label', input.name === 'from' ? 'From date' : 'To date');
        button.setAttribute('aria-haspopup', 'dialog'); button.setAttribute('aria-expanded', 'false');
        button.setAttribute('aria-controls', calendar.id);
        const field = {input, button}; fields.push(field);
        input.hidden = true; input.setAttribute('aria-hidden', 'true'); wrapper.append(button); syncField(field);
        input.addEventListener('change', () => syncField(field));
        button.addEventListener('click', () => openCalendar(field));
        button.addEventListener('keydown', event => {
          if (event.key === 'ArrowDown') { event.preventDefault(); openCalendar(field); }
          if (event.key === 'Escape' && !calendar.hidden) { event.preventDefault(); event.stopPropagation(); closeCalendar(true); }
        });
      });
      monthNames.forEach((name, month) => {
        const button = document.createElement('button'); button.type = 'button'; button.textContent = name.slice(0, 3);
        button.setAttribute('aria-label', name); button.addEventListener('click', () => {
          view = makeDate(view.getFullYear(), month, 1); focused = view; monthPanel.hidden = true;
          monthButton.setAttribute('aria-expanded', 'false'); render(); focusDay();
        }); monthPanel.append(button);
      });
      monthButton.addEventListener('click', () => {
        monthPanel.hidden = !monthPanel.hidden; monthButton.setAttribute('aria-expanded', String(!monthPanel.hidden));
      });
      function moveMonth(delta) {
        const next = makeDate(view.getFullYear(), view.getMonth() + delta, 1);
        if (!allowed(next)) return;
        view = next; focused = view; monthPanel.hidden = true; monthButton.setAttribute('aria-expanded', 'false'); render();
      }
      calendar.querySelector('[data-cal-prev]').addEventListener('click', () => moveMonth(-1));
      calendar.querySelector('[data-cal-next]').addEventListener('click', () => moveMonth(1));
      calendar.querySelector('.oj-cal-close').addEventListener('click', () => closeCalendar(true));
      calendar.querySelector('[data-cal-clear]').addEventListener('click', () => choose(null));
      calendar.querySelector('[data-cal-today]').addEventListener('click', () => choose(today()));
      function changeYear() {
        const year = Number(yearInput.value);
        if (!Number.isInteger(year) || year < 1 || year > 9999) { yearInput.value = view.getFullYear(); return; }
        view = makeDate(year, view.getMonth(), 1); focused = view; render();
      }
      yearInput.addEventListener('change', changeYear);
      yearInput.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); changeYear(); focusDay(); } });
      calendar.addEventListener('keydown', event => {
        if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); closeCalendar(true); return; }
        const button = event.target.closest('[data-date]');
        if (!button) return;
        const date = parse(button.dataset.date); let next = null;
        if (event.key === 'ArrowLeft') next = makeDate(date.getFullYear(), date.getMonth(), date.getDate() - 1);
        if (event.key === 'ArrowRight') next = makeDate(date.getFullYear(), date.getMonth(), date.getDate() + 1);
        if (event.key === 'ArrowUp') next = makeDate(date.getFullYear(), date.getMonth(), date.getDate() - 7);
        if (event.key === 'ArrowDown') next = makeDate(date.getFullYear(), date.getMonth(), date.getDate() + 7);
        if (event.key === 'Home') next = makeDate(date.getFullYear(), date.getMonth(), date.getDate() - date.getDay());
        if (event.key === 'End') next = makeDate(date.getFullYear(), date.getMonth(), date.getDate() + 6 - date.getDay());
        if (event.key === 'PageUp' || event.key === 'PageDown') {
          const delta = (event.key === 'PageUp' ? -1 : 1) * (event.shiftKey ? 12 : 1);
          const month = makeDate(date.getFullYear(), date.getMonth() + delta, 1);
          const last = makeDate(month.getFullYear(), month.getMonth() + 1, 0).getDate();
          next = makeDate(month.getFullYear(), month.getMonth(), Math.min(date.getDate(), last));
        }
        if (next) { event.preventDefault(); if (allowed(next)) { focused = next; view = next; render(); focusDay(); } }
      });
      calendar.addEventListener('focusout', () => {
        setTimeout(() => { if (!calendar.contains(document.activeElement) && document.activeElement !== activeField?.button) closeCalendar(); }, 0);
      });
    }

    page.classList.add('oj-controls-ready');
    const drawer = page.querySelector('#history-drawer');
    const message = page.querySelector('[data-history-message]');
    const dialog = page.querySelector('[data-history-dialog]');
    const modal = dialog && typeof dialog.showModal === 'function';
    if (modal) dialog.querySelector('[data-history-modal-host]').append(drawer);
    function showDetails() {
      drawer.hidden = false;
      if (modal && !dialog.open) dialog.showModal();
    }
    let request = null, generation = 0, lastLink = null;
    function selectCard(id) {
      for (const card of page.querySelectorAll('[data-history-card]'))
        card.classList.toggle('is-selected', card.dataset.historyCard === String(id));
    }
    function closeDrawer() {
      generation++;
      lastLink?.removeAttribute('aria-busy');
      request?.abort(); request = null;
      drawer.hidden = true; drawer.innerHTML = ''; drawer.setAttribute('aria-busy', 'false');
      if (modal && dialog.open) dialog.close();
      selectCard(''); message.textContent = '';
      const url = new URL(window.location.href); url.searchParams.delete('orderId');
      window.history.replaceState(null, '', url);
      if (lastLink?.isConnected) lastLink.focus({preventScroll: true});
    }
    page.addEventListener('click', async event => {
      const close = event.target.closest('[data-history-close]');
      if (close) { event.preventDefault(); closeDrawer(); return; }
      const link = event.target.closest('[data-history-details]');
      if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      event.preventDefault(); lastLink = link;
      const id = link.dataset.historyDetails, sequence = ++generation;
      request?.abort(); request = new AbortController();
      drawer.setAttribute('aria-busy', 'true');
      page.querySelectorAll('[data-history-details][aria-busy]').forEach(item => item.removeAttribute('aria-busy'));
      link.setAttribute('aria-busy', 'true');
      selectCard(id); message.textContent = '';
      const url = new URL(page.dataset.historyUrl, window.location.origin);
      url.searchParams.set('handler', 'Details'); url.searchParams.set('id', id);
      try {
        const response = await fetch(url, {credentials: 'same-origin', cache: 'no-store', signal: request.signal,
          headers: {'X-Requested-With': 'XMLHttpRequest'}});
        if (sequence !== generation) return;
        if (response.redirected) { window.location.assign(link.href); return; }
        if (!response.ok) throw new Error(response.status === 404 ? 'This order is no longer available.' : 'Could not open this order. Please try again.');
        const content = await response.text();
        if (sequence !== generation) return;
        const container = document.createElement('div'); container.innerHTML = content;
        if (!container.querySelector('[data-history-detail]')) throw new Error('Could not open the order details. Refresh and try again.');
        drawer.replaceChildren(...container.childNodes);
        drawer.setAttribute('aria-busy', 'false');
        showDetails();
        window.history.replaceState(null, '', link.href);
        drawer.querySelector('[data-history-detail-title]')?.focus({preventScroll: true});
        if (!modal && window.matchMedia('(max-width: 800px)').matches) drawer.scrollIntoView({behavior: 'smooth', block: 'start'});
      } catch (error) {
        if (sequence !== generation || error.name === 'AbortError') return;
        drawer.hidden = true; drawer.innerHTML = ''; drawer.setAttribute('aria-busy', 'false');
        if (modal && dialog.open) dialog.close();
        selectCard(''); message.textContent = error.message;
      } finally {
        link.removeAttribute('aria-busy');
        if (sequence === generation) request = null;
      }
    });
    if (modal) {
      dialog.addEventListener('cancel', event => { event.preventDefault(); closeDrawer(); });
      dialog.addEventListener('click', event => { if (event.target === dialog) {
        const rect = dialog.getBoundingClientRect();
        if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) closeDrawer();
      }});
    }
    if (!drawer.hidden) showDetails();
    // Previous / Next / Show all: load the next page in place and scroll smoothly back to the list top,
    // like the staff order queue. Any failure falls back to a normal page load.
    const workspace = page.querySelector('.oh-workspace');
    let pageRequest = null;
    async function loadPage(href, push, {scroll = true} = {}) {
      pageRequest?.abort(); pageRequest = new AbortController();
      workspace.setAttribute('aria-busy', 'true');
      try {
        const response = await fetch(href, {credentials: 'same-origin', cache: 'no-store', signal: pageRequest.signal});
        if (!response.ok || response.redirected) throw new Error('page');
        const doc = new DOMParser().parseFromString(await response.text(), 'text/html');
        const nextWorkspace = doc.querySelector('[data-history-page] .oh-workspace');
        if (!nextWorkspace) throw new Error('page');
        if (!drawer.hidden || (modal && dialog.open)) closeDrawer();
        [...workspace.children].forEach(child => { if (child !== drawer) child.remove(); });
        // The drawer may live inside the details dialog; only insert before it while it is still in the workspace.
        const anchor = drawer.parentElement === workspace ? drawer : null;
        [...nextWorkspace.children].forEach(child => { if (child.id !== 'history-drawer') workspace.insertBefore(document.adoptNode(child), anchor); });
        // The notes, tab links (they carry the filters) and Reset state follow the new results.
        for (const selector of ['.oj-result-note', '.oj-tabs', '[data-history-reset]']) {
          const current = page.querySelector(selector), next = doc.querySelector('[data-history-page] ' + selector);
          if (current && next) current.replaceWith(document.adoptNode(next));
        }
        if (push) window.history.pushState({historyPage: true}, '', href);
        if (scroll) {
          const top = page.querySelector('.oj-result-note') || workspace;
          top.scrollIntoView({behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start'});
        }
        message.textContent = 'Showing ' + (page.querySelector('.oh-pagination > span')?.textContent
          || page.querySelector('.oj-result-note span')?.textContent || 'your orders') + '.';
      } catch (error) {
        if (error.name !== 'AbortError') window.location.assign(href);
      } finally {
        workspace.removeAttribute('aria-busy');
      }
    }
    page.addEventListener('click', event => {
      const pager = event.target.closest('.oh-pagination a[href]');
      if (!pager || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      event.preventDefault(); loadPage(pager.href, true);
    });
    // Back/Forward: if the filters in the address differ from the form, reload so the controls match too.
    window.addEventListener('popstate', () => {
      const now = new URL(window.location.href), form = new URL(filterUrl());
      const keys = ['search', 'dining', 'status', 'statusGroup', 'sort', 'from', 'to'];
      const same = keys.every(key => defaults(now.searchParams.get(key) || '') === defaults(form.searchParams.get(key) || ''));
      if (same) loadPage(window.location.href, false);
      else window.location.reload();
    });
    document.addEventListener('keydown', event => { if (event.key === 'Escape' && (!drawer.hidden || request)) closeDrawer(); });
  }

  const reorder = document.querySelector('[data-reorder-page]');
  if (!reorder) return;
  const form = reorder.querySelector('[data-reorder-form]');
  const supportsReviewPopup = typeof HTMLDialogElement !== 'undefined' && typeof HTMLDialogElement.prototype.showModal === 'function';
  let reviewRequest = null, reviewDialog = null, reviewReturnFocus = null;

  function bindReorderConfirm(confirm) {
    if (confirm.dataset.confirmBound) return;
    confirm.dataset.confirmBound = 'true';
    let submitting = false;
    confirm.addEventListener('submit', event => {
      if (submitting) { event.preventDefault(); return; }
      submitting = true;
      const button = confirm.querySelector('button[type="submit"]');
      if (button) { button.disabled = true; button.textContent = 'Adding to your cart…'; }
    });
  }
  function closeReview() {
    if (reviewDialog?.open) reviewDialog.close();
  }
  function showReview(section) {
    if (!reviewDialog) {
      reviewDialog = document.createElement('dialog');
      reviewDialog.className = 'oh-reorder-review-dialog';
      reviewDialog.setAttribute('aria-labelledby', 'reorder-review-title');
      reorder.appendChild(reviewDialog);
      reviewDialog.addEventListener('cancel', event => { event.preventDefault(); closeReview(); });
      reviewDialog.addEventListener('close', () => {
        document.documentElement.classList.remove('oh-review-modal-open');
        document.documentElement.style.removeProperty('--oh-review-scrollbar');
        document.documentElement.style.removeProperty('--oh-review-body-padding');
        reviewDialog.replaceChildren();
        if (reviewReturnFocus?.isConnected) reviewReturnFocus.focus({preventScroll: true});
      });
      reviewDialog.addEventListener('click', event => {
        if (event.target !== reviewDialog) return;
        const rect = reviewDialog.getBoundingClientRect();
        if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) closeReview();
      });
    }
    for (const close of section.querySelectorAll('[data-review-close]')) {
      close.hidden = false;
      close.addEventListener('click', closeReview);
    }
    section.querySelector('[data-reorder-edit]')?.addEventListener('submit', event => { event.preventDefault(); closeReview(); });
    for (const confirm of section.querySelectorAll('[data-reorder-confirm]')) bindReorderConfirm(confirm);
    reviewDialog.replaceChildren(section);
    reorder.querySelector('.oh-selection').hidden = false;
    const explainer = reorder.querySelector('.oh-reorder-explainer');
    if (explainer) explainer.hidden = false;
    document.documentElement.style.setProperty('--oh-review-scrollbar', Math.max(0, innerWidth - document.documentElement.clientWidth) + 'px');
    document.documentElement.style.setProperty('--oh-review-body-padding', getComputedStyle(document.body).paddingRight);
    document.documentElement.classList.add('oh-review-modal-open');
    reviewDialog.showModal();
    const dishScroll = section.querySelector('.oh-review-scroll');
    if (dishScroll) {
      const alignDishBorders = () => {
        const gutter = Math.max(0, dishScroll.offsetWidth - dishScroll.clientWidth) + 'px';
        if (dishScroll.style.getPropertyValue('--oh-review-list-gutter') !== gutter)
          dishScroll.style.setProperty('--oh-review-list-gutter', gutter);
      };
      alignDishBorders();
      if ('ResizeObserver' in window) {
        const observer = new ResizeObserver(alignDishBorders);
        observer.observe(dishScroll);
        reviewDialog.addEventListener('close', () => observer.disconnect(), {once: true});
      }
    }
    section.querySelector('#reorder-review-title')?.focus({preventScroll: true});
  }

  if (form) {
    const reviewButton = form.querySelector('[data-review-button]');
    const reviewButtonText = reviewButton.textContent;
    const choices = () => Array.from(form.querySelectorAll('[data-reorder-choice]'));
    function updateCount() {
      let count = 0, lines = 0, valid = true;
      for (const choice of choices()) {
        const quantity = choice.closest('.oh-reorder-line').querySelector('[data-reorder-quantity]');
        for (const button of choice.closest('.oh-reorder-line').querySelectorAll('[data-quantity-step]')) {
          const current = Number(quantity.value), direction = Number(button.dataset.quantityStep);
          button.disabled = quantity.disabled || !Number.isInteger(current) || (direction < 0 ? current <= 1 : current >= 100);
        }
        if (choice.checked && !choice.disabled) {
          const value = Number(quantity.value); lines++;
          if (!Number.isInteger(value) || value < 1 || value > 100) valid = false;
          else count += value;
        }
      }
      const text = form.querySelector('[data-reorder-count]');
      if (text) text.textContent = !valid ? 'Use whole quantities from 1 to 100.' : `${lines} selected ${lines === 1 ? 'dish line' : 'dish lines'} · ${count} ${count === 1 ? 'portion' : 'portions'}`;
      reviewButton.disabled = !valid || lines === 0 || reviewRequest !== null;
    }
    function selectionChanged() {
      // Discard an in-flight quote if the selection changes while it is being checked.
      reviewRequest?.abort();
      updateCount();
    }
    function showReviewError(message) {
      const errors = reorder.querySelector('[data-reorder-errors]');
      if (!errors) return;
      errors.classList.remove('validation-summary-valid');
      errors.classList.add('validation-summary-errors');
      errors.hidden = false;
      errors.textContent = message;
      errors.focus();
    }
    form.addEventListener('click', event => {
      const button = event.target.closest('[data-quantity-step]');
      if (!button || button.disabled) return;
      const input = button.closest('.oh-reorder-line').querySelector('[data-reorder-quantity]');
      const current = Number(input.value);
      if (input.disabled || !Number.isInteger(current)) return;
      input.value = String(Math.max(1, Math.min(100, current + Number(button.dataset.quantityStep))));
      input.dispatchEvent(new Event('input', {bubbles: true}));
    });
    form.addEventListener('input', selectionChanged);
    form.addEventListener('change', selectionChanged);
    reorder.querySelector('[data-reorder-all]')?.addEventListener('click', () => { for (const choice of choices()) if (!choice.disabled) choice.checked = true; selectionChanged(); });
    reorder.querySelector('[data-reorder-none]')?.addEventListener('click', () => { for (const choice of choices()) choice.checked = false; selectionChanged(); });
    updateCount();

    if (supportsReviewPopup) form.addEventListener('submit', async event => {
      event.preventDefault();
      if (reviewRequest) return;
      if (!form.checkValidity()) { form.reportValidity(); return; }
      const errors = reorder.querySelector('[data-reorder-errors]');
      if (errors) { errors.replaceChildren(); errors.hidden = true; }
      const controller = new AbortController();
      reviewRequest = controller;
      reviewReturnFocus = reviewButton;
      reviewButton.disabled = true;
      reviewButton.textContent = 'Reviewing your dishes…';
      form.setAttribute('aria-busy', 'true');
      try {
        const response = await fetch(form.action, {
          method: 'POST', body: new FormData(form), credentials: 'same-origin',
          headers: {'Accept': 'text/html', 'X-Requested-With': 'XMLHttpRequest'}, signal: controller.signal
        });
        if (!response.ok) throw new Error(response.status === 401 || response.status === 403
          ? 'Your session has changed. Refresh the page before reviewing these dishes.'
          : 'We could not review these dishes. Please try again.');
        if (response.redirected) throw new Error('Please refresh the page and sign in again before reviewing these dishes.');
        const html = await response.text();
        if (reviewRequest !== controller || controller.signal.aborted) return;
        const documentResult = new DOMParser().parseFromString(html, 'text/html');
        const section = documentResult.querySelector('[data-reorder-review]');
        if (!section) {
          const messages = Array.from(documentResult.querySelectorAll('.validation-summary-errors li')).map(item => item.textContent.trim()).filter(Boolean);
          throw new Error(messages.join(' ') || 'No dishes could be reviewed. Check your selection and try again.');
        }
        const content = document.importNode(section, true);
        for (const script of content.querySelectorAll('script')) script.remove();
        showReview(content);
      } catch (error) {
        if (error.name !== 'AbortError' && reviewRequest === controller) showReviewError(error.message || 'We could not load your review. Please try again.');
      } finally {
        if (reviewRequest === controller) {
          reviewRequest = null;
          form.removeAttribute('aria-busy');
          reviewButton.textContent = reviewButtonText;
          updateCount();
        }
      }
    });
    document.addEventListener('keydown', event => {
      if (event.key === 'Escape' && reviewRequest && !reviewDialog?.open) { event.preventDefault(); reviewRequest.abort(); }
    });
    window.addEventListener('pageshow', event => {
      if (event.persisted) { closeReview(); reviewRequest?.abort(); updateCount(); }
    });
  }
  for (const confirm of reorder.querySelectorAll('[data-reorder-confirm]')) bindReorderConfirm(confirm);
  const serverReview = reorder.querySelector('[data-reorder-review]');
  if (serverReview && supportsReviewPopup) {
    reviewReturnFocus = form?.querySelector('[data-review-button]');
    showReview(serverReview);
  }
  reorder.classList.add('oh-review-ready');
})();
