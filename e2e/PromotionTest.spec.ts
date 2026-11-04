import { test, expect } from '@playwright/test';
import { addProductToCart, emptyCart } from './cart-helpers';
import { getRulesAdminToken, publishRule, restoreDefault } from './rules-admin-helpers';

const ruleTypeId = 'PRM-001';

test.afterAll(async ({ browser, playwright }) => {
  const request = await playwright.request.newContext();
  try {
    await restoreDefault(request, await getRulesAdminToken(browser), ruleTypeId);
  } finally {
    await request.dispose();
  }
});

test('published promotion is shown and charged', async ({ page, browser, playwright }) => {
  await emptyCart(page);

  await page.goto('/');
  await page.getByRole('link', { name: 'Adventurer GPS Watch' }).click();
  await expect(page).toHaveURL(/\/item\/\d+/);
  const itemId = Number(new URL(page.url()).pathname.split('/').pop());

  const request = await playwright.request.newContext();
  try {
    await publishRule(request, await getRulesAdminToken(browser), ruleTypeId, {
      linePromotions: [{ target: { kind: 'Item', id: itemId }, kind: 'PercentOff', value: 10, label: 'E2E 10% off' }],
      orderPromotions: [],
    });
  } finally {
    await request.dispose();
  }

  // Services pick up the new snapshot asynchronously.
  await expect.poll(async () => {
    await page.reload();
    return (await page.locator('.add-to-cart .price').innerText()).trim();
  }, { timeout: 70_000 }).toBe('$179.99');

  await addProductToCart(page, 'Adventurer GPS Watch');
  await page.goto('/cart');
  await expect(page.locator('.cart-item-promotion')).toHaveText('E2E 10% off');
  await expect(page.locator('.cart-item .catalog-item-total')).toContainText('$179.99');

  await page.getByRole('link', { name: 'Check out' }).click();
  await expect(page.getByRole('heading', { name: 'Checkout' })).toBeVisible();
  await page.getByRole('button', { name: 'Place order' }).click();

  await expect(page).toHaveURL(/\/user\/orders$/);
  const orders = await page.locator('.orders-item:not(.orders-header)').evaluateAll(rows =>
    rows.map(row => ({
      number: Number(row.querySelector('.order-number')?.textContent?.trim()),
      total: row.querySelector('.order-total')?.textContent?.trim() ?? '',
    })));
  const newest = orders.reduce((latest, order) => (order.number > latest.number ? order : latest));
  expect(newest.total).toBe('$179.99');
});
