// Banking assistant chat panel. Plain JS, no inline handlers (the site's CSP forbids them).
(function () {
  'use strict';

  var panel = document.getElementById('assistant-panel');
  if (!panel) return;

  var openBtn = document.getElementById('assistant-open');
  var closeBtn = document.getElementById('assistant-close');
  var resetBtn = document.getElementById('assistant-reset');
  var log = document.getElementById('assistant-log');
  var form = document.getElementById('assistant-form');
  var input = document.getElementById('assistant-input');
  var sendBtn = document.getElementById('assistant-send');
  var chips = document.getElementById('assistant-chips');
  var tokenField = form.querySelector('input[name="__RequestVerificationToken"]');
  var busy = false;
  var KEY = 'ob.assistant.open';

  function store(value) { try { sessionStorage.setItem(KEY, value); } catch (e) { /* storage blocked */ } }
  function stored() { try { return sessionStorage.getItem(KEY); } catch (e) { return null; } }

  function escapeHtml(s) {
    return s.replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  // Tiny, safe formatter: text is escaped first, then **bold** and "- " bullet lists are allowed.
  function format(text) {
    var lines = escapeHtml(text).split('\n');
    var html = '', inList = false;
    lines.forEach(function (raw) {
      var line = raw.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
      var bullet = /^\s*(?:[-•*])\s+(.*)$/.exec(line);
      if (bullet) {
        if (!inList) { html += '<ul>'; inList = true; }
        html += '<li>' + bullet[1] + '</li>';
      } else {
        if (inList) { html += '</ul>'; inList = false; }
        if (line.trim()) html += '<p>' + line + '</p>';
      }
    });
    if (inList) html += '</ul>';
    return html;
  }

  function add(role, text) {
    var div = document.createElement('div');
    div.className = 'msg msg-' + role;
    if (role === 'bot') div.innerHTML = format(text); else div.textContent = text;
    log.appendChild(div);
    log.scrollTop = log.scrollHeight;
    return div;
  }

  function addTransfer(t) {
    var card = document.createElement('div');
    card.className = 'msg msg-bot transfer-card';
    var rows = [['From', t.from], ['To', t.payee + ' · ' + t.to], ['Amount', t.amount]];
    if (t.remarks) rows.push(['Remarks', t.remarks]);
    var dl = '<p class="transfer-title">Transfer ready to review</p><dl>' + rows.map(function (r) {
      return '<dt>' + escapeHtml(r[0]) + '</dt><dd>' + escapeHtml(r[1]) + '</dd>';
    }).join('') + '</dl>';
    card.innerHTML = dl;
    var link = document.createElement('a');
    link.className = 'btn btn-primary btn-sm';
    link.textContent = 'Review and confirm with PIN';
    // Only same-site transfer URLs are ever followed
    link.href = /^\/Transfer\?/.test(t.url) ? t.url : '/Transfer';
    card.appendChild(link);
    var note = document.createElement('p');
    note.className = 'transfer-note';
    note.textContent = 'Nothing is sent until you enter your PIN on the next screen.';
    card.appendChild(note);
    log.appendChild(card);
    log.scrollTop = log.scrollHeight;
  }

  function greet() {
    if (log.childElementCount) return;
    var name = panel.getAttribute('data-name');
    add('bot', 'Hi' + (name ? ' ' + name : '') + '! I can check your balances and transactions, sum up your spending, or prepare a transfer for you to confirm.\nWhat would you like to know?');
  }

  var loaded = false;
  function restore() {
    if (loaded) return Promise.resolve();
    loaded = true;
    return fetch(panel.getAttribute('data-history'), { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : []; })
      .then(function (items) {
        if (!Array.isArray(items) || !items.length) return;
        chips.classList.add('hidden');
        items.forEach(function (m) { add(m.role === 'user' ? 'user' : 'bot', m.text || ''); });
      })
      .catch(function () { /* start fresh */ });
  }

  function open() {
    panel.classList.remove('hidden');
    openBtn.classList.add('hidden');
    openBtn.setAttribute('aria-expanded', 'true');
    store('1');
    restore().then(greet);
    setTimeout(function () { input.focus(); }, 50);
  }

  function close() {
    panel.classList.add('hidden');
    openBtn.classList.remove('hidden');
    openBtn.setAttribute('aria-expanded', 'false');
    store('0');
    openBtn.focus();
  }

  function setBusy(on) {
    busy = on;
    sendBtn.disabled = on;
    input.readOnly = on;
  }

  function send(text) {
    text = (text || '').trim();
    if (!text || busy) return;
    add('user', text);
    input.value = '';
    chips.classList.add('hidden');
    setBusy(true);
    var typing = add('bot', '…');
    typing.classList.add('typing');

    fetch(panel.getAttribute('data-send'), {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json', RequestVerificationToken: tokenField ? tokenField.value : '' },
      body: JSON.stringify({ message: text }),
    })
      .then(function (r) {
        if (r.status === 401 || r.redirected) return { reply: 'Your session has ended. Please sign in again.' };
        return r.json().catch(function () { return { reply: 'Something went wrong. Please try again.' }; });
      })
      .then(function (data) {
        typing.remove();
        add('bot', data.reply || 'Something went wrong. Please try again.');
        if (data.transfer) addTransfer(data.transfer);
      })
      .catch(function () {
        typing.remove();
        add('bot', "I couldn't reach the server. Check your connection and try again.");
      })
      .finally(function () { setBusy(false); input.focus(); });
  }

  openBtn.addEventListener('click', open);
  closeBtn.addEventListener('click', close);
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && !panel.classList.contains('hidden')) close();
  });

  resetBtn.addEventListener('click', function () {
    fetch(panel.getAttribute('data-reset'), {
      method: 'POST', credentials: 'same-origin',
      headers: { RequestVerificationToken: tokenField ? tokenField.value : '' },
    }).finally(function () {
      log.innerHTML = '';
      chips.classList.remove('hidden');
      greet();
    });
  });

  form.addEventListener('submit', function (e) {
    e.preventDefault();
    send(input.value);
  });

  input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      send(input.value);
    }
  });

  chips.addEventListener('click', function (e) {
    if (e.target.classList.contains('chip-btn')) send(e.target.textContent);
  });

  if (stored() === '1') open();
})();
