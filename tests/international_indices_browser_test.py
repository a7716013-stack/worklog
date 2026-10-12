from playwright.sync_api import sync_playwright,expect
from pathlib import Path
import json
base='http://localhost:5191'
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 context=browser.new_context(viewport={'width':1440,'height':1050})
 page=context.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.route('**/StockAnalysis/MarketPulse?*',lambda r:r.fulfill(status=503,json={}))
 page.goto(base,wait_until='networkidle')
 expect(page.locator('.international-card')).to_have_count(7)
 assert page.locator('.international-card').evaluate_all('(cards)=>cards.map(c=>c.dataset.symbol)')==['^DJI','^GSPC','^SOX','^IXIC','^RUT','^N225','^HSI']
 expect(page.locator('.international-list')).to_have_attribute('aria-busy','false')
 expect(page.locator('[data-symbol="^DJI"] .international-value')).to_have_text('41,000.00')
 expect(page.locator('[data-symbol="^SOX"] .international-change')).to_contain_text('▼')
 expect(page.locator('[data-symbol="^GSPC"] .international-change')).to_contain_text('▲')
 left=page.locator('.international-sidebar').bounding_box();right=page.locator('.home-primary').bounding_box()
 assert right['x']+right['width']<left['x']
 assert page.locator('.international-name').first.get_attribute('href')=='https://finance.yahoo.com/quote/%5EDJI/'
 page.screenshot(path='artifacts/international-desktop.png',full_page=True)
 for width in [1280,1024,768,390]:
  page.set_viewport_size({'width':width,'height':844})
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),width
  if width<=1100:
   side=page.locator('.international-sidebar').bounding_box();main=page.locator('.home-primary').bounding_box();assert side['y']+side['height']<=main['y']
 page.screenshot(path='artifacts/international-mobile.png',full_page=True)
 page.route('**/Home/InternationalIndices',lambda r:r.fulfill(status=503,json={}))
 page.locator('#international-refresh').click();expect(page.locator('#international-status')).to_contain_text('更新失敗')
 expect(page.locator('[data-symbol="^DJI"] .international-value')).to_have_text('41,000.00')
 expect(page.locator('[data-symbol="^DJI"] .international-state')).to_have_text('更新失敗')
 page.unroute('**/Home/InternationalIndices');page.locator('#international-refresh').click()
 expect(page.locator('#international-status')).to_contain_text('7 項指數')
 assert not errors,errors
 Path('artifacts/international-browser-results.json').write_text(json.dumps({'desktopRightSidebar':True,'sevenIndices':True,'responsiveWidths':[1280,1024,768,390],'failureRetainsTimestampAndRetryRecovers':True,'jsErrors':errors}),encoding='utf-8')
 print('PASS: seven index cards, right sidebar, 4 responsive widths, safe source links, failure/retry; no JS errors.')
 context.request.post(base+'/__fixture/stop')
 browser.close()
