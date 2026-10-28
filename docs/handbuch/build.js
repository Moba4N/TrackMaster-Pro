// Handbuch als PDF: erst ohne Seitenzahlen rendern, Seiten der Überschriften ermitteln, dann mit Inhaltsverzeichnis neu rendern
const { chromium } = require('playwright');
const { execFileSync } = require('child_process');
const path = require('path');
const fs = require('fs');
const html = 'file://' + path.resolve(__dirname, 'handbuch.html');
const out = process.argv[2] || path.resolve(__dirname, 'Handbuch.pdf');
const footer = `<div style="width:100%;font-family:'Segoe UI','Noto Sans','DejaVu Sans',sans-serif;font-size:7.5pt;color:#5B6470;padding:0 20mm;display:flex;justify-content:space-between">
  <span>TrackMaster Pro 1.4.0 · Handbuch</span><span><span class="pageNumber"></span></span></div>`;
async function render(page, pages, file) {
  await page.addInitScript(p => { window.PAGES = p; }, pages);
  await page.goto(html, { waitUntil: 'networkidle' });
  await page.pdf({ path: file, format: 'A4', printBackground: true, displayHeaderFooter: true,
    headerTemplate: '<div></div>', footerTemplate: footer, margin: { top: '22mm', bottom: '22mm', left: '20mm', right: '20mm' } });
}
(async () => {
  const browser = await chromium.launch();
  let pages = {};
  for (let round = 0; round < 3; round++) {
    const page = await browser.newPage();
    const ids = await (async () => { await page.goto(html); return page.$$eval('section:not(.cover):not(.toc) h1[id], section:not(.cover):not(.toc) h2[id]', hs => hs.map(h => [h.id, [...h.childNodes].filter(n => !(n.classList && (n.classList.contains('no') || n.classList.contains('badge')))).map(n => n.textContent).join('').trim()])); })();
    await page.close();
    const p2 = await browser.newPage();
    const tmp = out + '.tmp.pdf';
    await render(p2, pages, tmp);
    await p2.close();
    const found = JSON.parse(execFileSync('python3', ['-I', path.resolve(__dirname, 'pages.py'), tmp, JSON.stringify(ids)]).toString());
    const same = JSON.stringify(found) === JSON.stringify(pages);
    pages = found;
    if (same) { fs.renameSync(tmp, out); break; }
    if (round === 2) fs.renameSync(tmp, out);
  }
  await browser.close();
  console.log(JSON.stringify(pages));
})();
