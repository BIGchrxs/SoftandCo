// Soft & Co. — shell behaviour.
// Below the sidebar breakpoint the navigation folds into a header strip and
// this toggles the nav open. Delegated from document so it survives any future
// re-render of the header.
document.addEventListener('click', function (e) {
    var toggle = e.target.closest('#scNavToggle');
    if (!toggle) return;

    var body = document.getElementById('scSidebarBody');
    if (!body) return;

    var open = body.classList.toggle('show');
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
});

// Progressive enhancement: without JavaScript every payment column remains visible.
var paymentToggle = document.getElementById('scPaymentToggle');
var ordersTable = document.getElementById('scOrdersTable');
if (paymentToggle && ordersTable) {
    paymentToggle.hidden = false;
    ordersTable.classList.add('sc-payments-collapsed');
    paymentToggle.addEventListener('click', function () {
        var collapsed = ordersTable.classList.toggle('sc-payments-collapsed');
        paymentToggle.setAttribute('aria-pressed', collapsed ? 'false' : 'true');
        paymentToggle.textContent = collapsed ? 'Show payment columns' : 'Hide payment columns';
    });
}

document.querySelectorAll('.sc-nav a.active').forEach(function (link) {
    link.setAttribute('aria-current', 'page');
});

// Picking a local supplier switches the default currency to ZAR - a South African supplier
// invoicing in yuan is almost always a mis-click. Only moves it off a foreign default; if the
// user has deliberately chosen a currency after switching, their choice stands.
document.addEventListener('change', function (e) {
    if (!e.target.matches('#supplierOrigin')) return;

    var currency = document.getElementById('supplierCurrency');
    if (!currency) return;

    if (e.target.value === 'Local' && currency.value !== 'ZAR') {
        currency.value = 'ZAR';
    }
});
