const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');

// Run only against a disposable development database: the opt-in write check creates an order.
const base = process.env.TRACKER_TEST_URL || 'https://localhost:7027';
const output = path.resolve('artifacts');
fs.mkdirSync(output, { recursive: true });
(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  async function visit(route) {
    const response = await page.goto(base + route);
    assert.equal(response.status(), 200, route);
  }
  try {
    await visit('/Orders');
    assert.match(page.url(), /Account\/Login/);
    await page.getByLabel('Email address').fill(process.env.TRACKER_TEST_EMAIL);
    await page.getByLabel('Password', { exact: true }).fill(process.env.TRACKER_TEST_PASSWORD);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await page.waitForURL('**/Orders');
    await page.getByRole('heading', { name: 'Order tracker', exact: true }).waitFor();
    const count = await page.locator('.sc-order-table tbody tr').count();
    assert.ok(count >= 31, 'Workbook seed orders are present');
    assert.equal(await page.locator('.sc-financial-column').first().isVisible(), false);
    await page.screenshot({ path: path.join(output, 'order-tracker-desktop.png') });
    await page.getByRole('button', { name: 'Show payment columns' }).click();
    assert.equal(await page.locator('.sc-financial-column').first().isVisible(), true);
    assert.equal(await page.locator('#scPaymentToggle').getAttribute('aria-pressed'), 'true');
    await page.getByRole('button', { name: 'Hide payment columns' }).click();

    await page.getByLabel('Search orders').fill('Keorh');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.waitForURL('**/*Search=Keorh*');
    assert.equal(await page.locator('.sc-order-table tbody tr').count(), 1);
    assert.match(await page.locator('.sc-order-table').innerText(), /Smart Toilets/);
    await page.getByLabel('Search orders').fill('no-matching-order-9381');
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.getByRole('heading', { name: 'No matching orders' }).waitFor();
    await page.getByRole('link', { name: 'Clear filters', exact: true }).first().click();
    await page.waitForURL('**/Orders');
    await page.locator('summary').click();
    await page.getByLabel('Delivery', { exact: true }).selectOption('Shipping');
    await page.getByRole('button', { name: 'Apply filters' }).click();
    await page.waitForURL('**/*Fulfilment=Shipping*');
    assert.ok(await page.locator('.sc-order-table tbody tr').count() > 0);
    for (const label of await page.locator('.sc-delivery').allTextContents()) assert.equal(label.trim(), 'Shipping');
    await page.getByRole('link', { name: 'View Keorh order: Smart Toilets', exact: true }).click();
    await page.locator('.sc-progress').waitFor();
    assert.match(await page.locator('[aria-current="step"]').innerText(), /Shipping/);

    // Counted per route rather than as one number for all of them. The flat `=== 3` here had
    // already gone stale when Suppliers gained its origin tile, and silently so, because every
    // register is listed on one line.
    for (const [route, kpis] of [['/Orders/Outstanding', 3], ['/Suppliers', 4], ['/Projects', 4], ['/Clients', 4]]) {
      await visit(route);
      assert.equal(await page.locator('.sc-kpi').count(), kpis, `${route} KPI tiles`);
      await page.screenshot({ path: path.join(output, route.split('/').pop().toLowerCase() + '.png') });
    }
    await page.locator('.sc-table tbody a[href*="ProjectId"]').first().click();
    await page.waitForURL('**/*ProjectId=*');
    assert.ok(await page.locator('.sc-order-table tbody tr').count() > 0);
    await visit('/Suppliers');
    await page.getByRole('link', { name: 'Service providers', exact: true }).click();
    await page.waitForURL('**/*type=ServiceProvider*');
    assert.match(await page.locator('tbody').innerText(), /UniBest/);
    await page.locator('.sc-table tbody a[href*="SupplierId"]').first().click();
    await page.waitForURL('**/*SupplierId=*');
    assert.match(await page.locator('.sc-order-table').innerText(), /Freight Service/);

    await visit('/Orders');
    await page.setViewportSize({ width: 390, height: 844 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true, 'No page-level overflow on mobile');
    await page.getByRole('button', { name: 'Menu', exact: true }).click();
    assert.equal(await page.locator('#scNavToggle').getAttribute('aria-expanded'), 'true');
    await page.getByRole('link', { name: 'Suppliers', exact: true }).click();
    await page.waitForURL('**/Suppliers');
    await visit('/Orders');
    await page.screenshot({ path: path.join(output, 'order-tracker-mobile.png'), fullPage: true });

    if (process.env.TRACKER_TEST_WRITES === '1') {
      await page.setViewportSize({ width: 1440, height: 1000 });
      await page.getByRole('link', { name: 'New order', exact: false }).click();
      await page.getByLabel('Supplier', { exact: true }).selectOption({ label: 'Suo Young Lighting' });
      await page.getByLabel('Product', { exact: true }).fill('Smoke test pendant');
      await page.getByLabel('Fairmont', { exact: true }).check();
      await page.getByLabel('Cargo readiness', { exact: true }).fill('2026-10-20');
      await page.getByLabel('Invoice ref', { exact: true }).fill('SMOKE-' + Date.now());
      await page.getByLabel('Exchange rate (ZAR per unit)').fill('2.5');
      await page.getByLabel('Invoice value (foreign)').fill('100');
      assert.equal(await page.getByLabel('Invoice value (ZAR)').inputValue(), '250.00');
      await page.getByRole('button', { name: 'Create order', exact: true }).click();
      await page.waitForURL('**/Orders/Details/*');
      await page.getByRole('link', { name: 'Edit', exact: true }).click();
      await page.getByLabel('Delivery status', { exact: true }).selectOption('Shipping');
      await page.getByRole('button', { name: 'Save changes', exact: true }).click();
      await page.waitForURL('**/Orders/Details/*');
      assert.match(await page.locator('[aria-current="step"]').innerText(), /Shipping/);
      await page.getByRole('link', { name: 'Record payment', exact: true }).click();
      await page.getByLabel('Amount (foreign)', { exact: true }).fill('50');
      await page.getByRole('button', { name: 'Record payment', exact: true }).click();
      await page.waitForURL('**/Orders/Details/*');
      assert.match(await page.locator('.sc-money').innerText(), /Part paid/);
    }
    assert.deepEqual(errors, [], 'No browser JavaScript errors');
    console.log('PASS: login, workbook rows, payment columns, search, empty results, delivery filters, progress, supplier/project links, mobile navigation' + (process.env.TRACKER_TEST_WRITES === '1' ? ', create/edit order and record payment' : '') + '.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
