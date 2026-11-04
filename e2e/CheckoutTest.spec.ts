import { test, expect } from '@playwright/test';
import { addProductToCart, emptyCart } from './cart-helpers';

test('place an order through checkout', async ({ page }) => {
  await emptyCart(page);

  await page.goto('/user/orders');
  await expect(page.getByRole('heading', { name: 'Orders', exact: true })).toBeVisible();
  const existingOrderCount = await page.locator('.order-number').count();

  await addProductToCart(page, 'Adventurer GPS Watch');
  await page.goto('/cart');
  await expect(page.getByRole('heading', { name: 'Shopping bag', exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Check out' }).click();

  await expect(page.getByRole('heading', { name: 'Checkout' })).toBeVisible();
  await expect(page.getByLabel('Address')).toHaveValue('15703 NE 61st Ct');
  await expect(page.getByLabel('City')).toHaveValue('Redmond');
  const checkoutTotal = (await page.locator('.checkout-total').innerText()).trim();
  expect(checkoutTotal).toMatch(/^\$[\d,]+\.\d{2}$/);
  await page.getByRole('button', { name: 'Place order' }).click();

  await expect(page).toHaveURL(/\/user\/orders$/);
  await expect(page.getByRole('heading', { name: 'Orders', exact: true })).toBeVisible();
  await expect(page.locator('.order-number')).toHaveCount(existingOrderCount + 1);

  // The newest order (highest number) must be charged exactly the total shown at checkout.
  const orders = await page.locator('.orders-item:not(.orders-header)').evaluateAll(rows =>
    rows.map(row => ({
      number: Number(row.querySelector('.order-number')?.textContent?.trim()),
      total: row.querySelector('.order-total')?.textContent?.trim() ?? '',
    })));
  const newest = orders.reduce((latest, order) => (order.number > latest.number ? order : latest));
  expect(newest.total).toBe(checkoutTotal);
});