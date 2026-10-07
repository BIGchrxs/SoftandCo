// Allocation shares appear only once an order's cost is actually being split.
//
// Most orders serve a single project and should never see the field; the server sets their share to
// 1 regardless, so the visibility here is purely about not asking a question that has one answer.
//
// The disabling is NOT cosmetic. A hidden share input still posts, and an empty one fails to bind
// to a decimal - which made the whole form invalid before any of its own rules ran, so an order
// would be silently refused with no message anywhere. Disabled inputs are not submitted at all.
//
// Shared by the international and local order forms via _ProjectAllocation.cshtml.
(function () {
    var checks = Array.prototype.slice.call(document.querySelectorAll('[data-project-check]'));
    if (!checks.length) return;

    var hint = document.querySelector('[data-share-hint]');

    function sync() {
        var split = checks.filter(function (c) { return c.checked; }).length > 1;

        checks.forEach(function (c) {
            var box = document.querySelector('[data-project-share="' + c.value + '"]');
            if (!box) return;

            var show = split && c.checked;
            box.hidden = !show;

            var input = box.querySelector('input');
            if (input) input.disabled = !show;
        });

        if (hint) hint.hidden = !split;
    }

    checks.forEach(function (c) { c.addEventListener('change', sync); });
    sync();
})();
