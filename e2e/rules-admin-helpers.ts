import { APIRequestContext, Browser, expect } from '@playwright/test';

const identityUrl = process.env.IDENTITY_URL ?? 'http://localhost:5223';
const businessRulesUrl = process.env.BUSINESSRULES_URL ?? 'http://localhost:5230';
const rulesApi = `${businessRulesUrl}/api/rules`;

/** Gets an admin token through the Swagger UI implicit client, logged in as the seeded rules publisher. */
export async function getRulesAdminToken(browser: Browser): Promise<string> {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    const authorize = new URLSearchParams({
      client_id: 'businessrulesswaggerui',
      response_type: 'token',
      scope: 'businessrules.admin',
      redirect_uri: `${businessRulesUrl}/swagger/oauth2-redirect.html`,
      state: 'e2e',
      nonce: 'e2e',
    });
    await page.goto(`${identityUrl}/connect/authorize?${authorize}`);

    await page.getByPlaceholder('Username').fill(process.env.USERNAME1!);
    await page.getByPlaceholder('Password').fill(process.env.PASSWORD!);
    await page.getByRole('button', { name: 'Login' }).click();

    await page.waitForURL(/access_token=/);
    const token = new URLSearchParams(new URL(page.url()).hash.slice(1)).get('access_token');
    expect(token, 'access_token in the redirect').toBeTruthy();
    return token!;
  } finally {
    await context.close();
  }
}

/** Creates a draft of the rule in the default scope, effective now, and publishes it. */
export async function publishRule(request: APIRequestContext, token: string, ruleTypeId: string, parameters: unknown): Promise<void> {
  const headers = { Authorization: `Bearer ${token}` };

  const draft = await request.post(`${rulesApi}/versions?api-version=1.0`, {
    headers,
    data: { ruleTypeId, scope: 'default', parameters, effectiveFrom: new Date().toISOString() },
  });
  expect(draft.ok(), `create ${ruleTypeId} draft: ${draft.status()} ${await draft.text()}`).toBeTruthy();
  const { id } = await draft.json();

  const publish = await request.post(`${rulesApi}/versions/${id}/publish?api-version=1.0`, {
    headers,
    data: { guardOverrideReason: 'e2e' },
  });
  expect(publish.ok(), `publish ${ruleTypeId}: ${publish.status()} ${await publish.text()}`).toBeTruthy();
}

/** Publishes the rule type's built-in default so later runs see today's behaviour. */
export async function restoreDefault(request: APIRequestContext, token: string, ruleTypeId: string): Promise<void> {
  const types = await request.get(`${rulesApi}/types?api-version=1.0`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(types.ok(), `list rule types: ${types.status()}`).toBeTruthy();

  const ruleType = (await types.json()).find((type: { id: string }) => type.id === ruleTypeId);
  expect(ruleType, `rule type ${ruleTypeId}`).toBeTruthy();

  await publishRule(request, token, ruleTypeId, ruleType.default);
}
