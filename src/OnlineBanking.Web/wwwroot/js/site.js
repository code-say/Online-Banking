// Small progressive enhancements. Every page works without JavaScript.
(function () {
  'use strict';

  // Ask before destructive or money-moving actions: <form data-confirm="Are you sure?">
  document.addEventListener('submit', function (e) {
    var form = e.target;
    var msg = form.getAttribute('data-confirm');
    if (msg && !window.confirm(msg)) {
      e.preventDefault();
      return;
    }
    // Stop double submits: <form data-once>
    if (form.hasAttribute('data-once')) {
      if (form.dataset.sent) { e.preventDefault(); return; }
      form.dataset.sent = '1';
      var btn = e.submitter || form.querySelector('[type=submit]');
      if (btn) { btn.setAttribute('aria-busy', 'true'); btn.textContent = btn.getAttribute('data-busy') || 'Please wait…'; }
    }
  }, true);

  // Payee name check on the transfer form
  var input = document.querySelector('[data-payee-lookup]');
  var out = document.getElementById('payee');
  if (input && out) {
    var timer, last = '';
    var check = function () {
      var digits = input.value.replace(/\D/g, '');
      if (digits === last) return;
      last = digits;
      out.className = 'payee';
      out.textContent = '';
      if (digits.length !== 12) return;
      out.textContent = 'Checking…';
      fetch(input.getAttribute('data-payee-lookup') + '?number=' + encodeURIComponent(digits), { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
        .then(function (r) { return r.ok ? r.json() : { found: false }; })
        .then(function (d) {
          if (digits !== last) return;
          out.className = 'payee ' + (d.found ? 'ok' : 'bad');
          out.textContent = d.found ? '✓ Beneficiary: ' + d.name : 'No active account with this number.';
        })
        .catch(function () { out.textContent = ''; });
    };
    input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(check, 300); });
    check();
  }

  // Show balance and today's remaining limit for the chosen account
  var from = document.querySelector('[data-account-info]');
  var info = document.getElementById('account-info');
  if (from && info) {
    var show = function () {
      var opt = from.options[from.selectedIndex];
      info.textContent = opt && opt.dataset.balance ? 'Available ' + opt.dataset.balance + ' · You can send ' + opt.dataset.remaining + ' more today' : '';
    };
    from.addEventListener('change', show);
    show();
  }

  // Digits only for PIN fields
  document.querySelectorAll('input[inputmode=numeric]').forEach(function (el) {
    el.addEventListener('input', function () { el.value = el.value.replace(/\D/g, '').slice(0, el.maxLength > 0 ? el.maxLength : 32); });
  });

  // Print buttons
  document.querySelectorAll('[data-print]').forEach(function (el) {
    el.addEventListener('click', function () { window.print(); });
  });
})();
