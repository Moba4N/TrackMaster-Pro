// Webversion starten: lädt die Seite, wartet auf das Menüband und meldet Fehler aus der Konsole.
// Aufruf: node web-smoke.js http://localhost:8099/
const { chromium } = require('playwright');
(async () => {
  const url = process.argv[2];
  const browser = await chromium.launch();
  const page = await browser.newPage({ locale: 'de-DE', viewport: { width: 1400, height: 900 } });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(url);
  await page.waitForSelector('.boot', { state: 'detached', timeout: 120000 });
  const tabs = await page.$$eval('[role="tab"], .tab', els => els.map(e => e.textContent.trim()).filter(Boolean));
  console.log('Register:', tabs.join(' | '));
  await browser.close();
  if (errors.length) { console.error('Fehler:\n' + errors.join('\n')); process.exit(1); }
  if (!tabs.some(t => t.includes('Ausstattung'))) { console.error('Register „Ausstattung“ fehlt'); process.exit(1); }
  console.log('Webversion läuft.');
})().catch(e => { console.error(e); process.exit(1); });
