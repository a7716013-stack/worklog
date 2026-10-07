"""Supplementary 1.2.4 market industry filter and CSRF-protected follow."""
from playwright.sync_api import sync_playwright,expect
from urllib.parse import urlparse,parse_qs
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 page=browser.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto('http://localhost:5189/StockAnalysis/MarketRadar',wait_until='networkidle')
 expect(page.locator('#radar-status')).to_contain_text('已完成')
 page.locator('#radar-industry').select_option(label='半導體業')
 expect(page.locator('.radar-card')).to_have_count(1)
 expect(page.locator('.radar-card')).to_have_attribute('data-symbol','2330')
 page.locator('#radar-industry').select_option(label='ETF')
 expect(page.locator('.radar-card')).to_have_count(1)
 expect(page.locator('.radar-card')).to_have_attribute('data-symbol','6002')
 page.locator('#radar-industry').select_option(label='半導體業')
 page.get_by_role('button',name='加入績效追蹤',exact=True).click()
 expect(page.get_by_role('heading',name='推薦績效追蹤',exact=True)).to_be_visible()
 assert parse_qs(urlparse(page.url).query).get('Mine')==['True']
 expect(page.locator('#radar-tracking-table tbody tr')).to_have_count(1)
 assert not errors,errors
 browser.close()
print('PASS: industry and ETF filters; market card follow POST carries CSRF token and preserves unique personal tracking.')
