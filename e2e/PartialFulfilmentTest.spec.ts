import { test, expect } from '@playwright/test';
import { addProductToCart, emptyCart } from './cart-helpers';
import { getRulesAdminToken, publishRule, restoreDefault } from './rules-admin-helpers';

const ruleTypeId = 'ORD-013';

test.afterAll(async ({ browser, playwright }) => {
  const request = await playwright.request.newContext();
  try {
    await restoreDefault(request, await getRulesAdminToken(browser), ruleTypeId);
  } finally {
    await request.dispose();
  }
});

test('dropped item is shown on orders', async ({ page, browser, playwright }) => {
  test.setTimeout(8 * 60_000);

  const request = await playwright.request.newContext();
  try {
    await publishRule(request, await getRulesAdminToken(browser), ruleTypeId, { policy: 'PartialFulfilment' });
  } finally {
    await request.dispose();
  }

  // Ordering.API reads the policy from its rules snapshot, which propagates asynchronously.
  await page.waitForTimeout(70_000);

  await emptyCart(page);
  await addProductToCart(page, 'Summit Pro Harness');
  await addProductToCart(page, 'Adventurer GPS Watch');

  // The seeded stock is 100 units, so 101 cannot be fulfilled.
  await page.goto('/cart');
  const watchQuantity = page.getByLabel('Quantity of Adventurer GPS Watch');
  await watchQuantity.fill('101');
  const updateRequest = page.waitForRequest(r => r.method() === 'POST' && new URL(r.url()).pathname === '/cart');
  await Promise.all([updateRequest, watchQuantity.press('Enter')]);
  await expect(watchQuantity).toHaveValue('101', { timeout: 15_000 });

  await page.getByRole('link', { name: 'Check out' }).click();
  await expect(page.getByRole('heading', { name: 'Checkout' })).toBeVisible();
  await page.getByRole('button', { name: 'Place order' }).click();
  await expect(page).toHaveURL(/\/user\/orders$/);

  await expect.poll(async () => {
    await page.reload();
    return await page.getByText('Removed (out of stock): Adventurer GPS Watch').count();
  }, { timeout: 4 * 60_000, intervals: [5_000] }).toBeGreaterThan(0);
});
