// End-to-end: a customer sends money, then an admin approves an application and posts cash.
// Runs against the app on BASE_URL (default http://localhost:5090). Fails on any browser error.
import { chromium } from 'playwright';

const BASE = process.env.BASE_URL || 'http://localhost:5090';
const errors = [];
const step = (name) => console.log(`▶ ${name}`);
const ok = (name) => console.log(`✅ ${name}`);

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
page.on('console', (m) => { if (m.type() === 'error') errors.push(`console: ${m.text()}`); });
page.on('pageerror', (e) => errors.push(`pageerror: ${e.message}`));
page.on('dialog', (d) => d.accept()); // confirm() prompts

async function expectText(text) {
  await page.getByText(text, { exact: false }).first().waitFor({ timeout: 10000 });
}

async function signIn(email, password) {
  await page.goto(`${BASE}/Account/Login`);
  await page.fill('#Email', email);
  await page.fill('#Password', password);
  await page.click('form .btn-primary[type=submit]');
}

try {
  step('Landing page');
  await page.goto(BASE);
  await expectText('Your bank account, online.');
  await page.screenshot({ path: 'e2e-home.png', fullPage: true });
  ok('Landing page');

  step('Customer signs in');
  await signIn('customer@onlinebank.demo', 'Customer@12345');
  await page.waitForURL(/\/Dashboard/);
  await expectText('Aarav');
  const before = await page.locator('.acct-balance').first().innerText();
  console.log('URL:', page.url(), 'savings balance', before);
  await page.screenshot({ path: 'e2e-dashboard.png', fullPage: true });
  ok('Dashboard shows accounts');

  step('Transfer with payee check');
  await page.click('text=Send money');
  await page.waitForURL(/\/Transfer/);
  await page.fill('#ToAccountNumber', '501000987654');
  await page.locator('#payee.ok').waitFor({ timeout: 10000 });
  console.log('Payee:', await page.locator('#payee').innerText());
  await page.fill('#Amount', '1250.50');
  await page.fill('#Remarks', 'E2E test');
  await page.fill('#Pin', '2580');
  await page.click('form .btn-primary[type=submit]');
  await page.waitForURL(/\/Transfer\/Receipt\//);
  await expectText('Money sent');
  await expectText('₹1,250.50');
  await page.screenshot({ path: 'e2e-receipt.png', fullPage: true });
  ok('Transfer receipt');

  step('Wrong PIN is refused');
  await page.goto(`${BASE}/Transfer`);
  await page.fill('#ToAccountNumber', '501000987654');
  await page.fill('#Amount', '10');
  await page.fill('#Pin', '1111');
  await page.click('form .btn-primary[type=submit]');
  await expectText('Wrong PIN. 2 attempts left.');
  ok('Wrong PIN message');

  step('Statement and filters');
  await page.goto(`${BASE}/Dashboard`);
  await page.locator('.acct').first().click();
  await page.waitForURL(/\/Accounts\/Details\//);
  await expectText('E2E test');
  await page.selectOption('#Type', 'credit');
  await page.click('.filters .btn-primary');
  await expectText('Salary credit');
  const csv = await page.request.get(await page.locator('text=Download CSV').getAttribute('href').then((h) => new URL(h, BASE).href));
  if (!(await csv.text()).includes('Date (IST),Reference')) throw new Error('CSV download missing header');
  await page.screenshot({ path: 'e2e-statement.png', fullPage: true });
  ok('Statement filters and CSV');

  step('Mobile layout has no horizontal scroll');
  await page.setViewportSize({ width: 375, height: 800 });
  for (const path of ['/Dashboard', '/Transfer', '/Applications/Create']) {
    await page.goto(BASE + path);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (overflow > 1) throw new Error(`${path} overflows by ${overflow}px on mobile`);
  }
  await page.screenshot({ path: 'e2e-mobile.png', fullPage: true });
  await page.setViewportSize({ width: 1280, height: 900 });
  ok('Mobile layout');

  step('Sign out');
  await page.goto(`${BASE}/Dashboard`);
  await page.click('text=Sign out');
  await expectText("You've signed out securely.");
  ok('Signed out');

  step('Admin approves an application');
  await signIn('admin@onlinebank.demo', 'Admin@12345');
  await page.waitForURL(/\/Admin/);
  await expectText('Bank overview');
  await page.goto(`${BASE}/Admin/Applications`);
  await expectText('Neha');
  await page.click('text=Review');
  await page.fill('#openingDeposit', '2500');
  await page.click('text=Approve and open account');
  await expectText('Approved. Account');
  ok('Application approved');

  step('Admin posts a cash deposit');
  await page.fill('#Cash_Amount', '1000');
  await page.click('button:has-text("Deposit")');
  await expectText('Deposited ₹1,000.00. New balance ₹3,500.00');
  await page.screenshot({ path: 'e2e-admin-account.png', fullPage: true });
  ok('Cash deposit');

  step('Admin messages');
  await page.goto(`${BASE}/Admin/Messages`);
  await expectText('Debit card delivery');
  ok('Messages');
} catch (e) {
  errors.push(`❌ ${e.message}`);
  await page.screenshot({ path: 'e2e-failure.png', fullPage: true }).catch(() => {});
  console.log('URL:', page.url());
} finally {
  await browser.close();
}

if (errors.length) {
  console.log('Browser errors:');
  for (const e of errors) console.log(' ', e);
  process.exit(1);
}
console.log('✅ All end-to-end checks passed');
