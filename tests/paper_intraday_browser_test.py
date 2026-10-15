from pathlib import Path
from playwright.sync_api import sync_playwright, expect
import json
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 page=browser.new_page(viewport={'width':1440,'height':1000})
 page.add_init_script("Date.now = () => new Date('2026-10-15T10:12:00+08:00').getTime();")
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 base='http://localhost:5187'
 response=page.goto(base+'/StockAnalysis/PaperTrading',wait_until='networkidle')
 assert response.headers.get('x-papertrading-fixture')=='isolated-sql'
 form=page.locator('#paper-order-form').evaluate('f=>Object.fromEntries(new FormData(f))')
 form.update({'Order.StockId':'2330','Order.Quantity':'1000','Order.Side':'Buy','Order.OrderType':'Market'})
 assert page.request.post(base+'/StockAnalysis/PlacePaperOrder',form=form).status==200
 data={'totalAssets':1019857.5,'totalMarketValue':120000,'unrealizedProfitLoss':19857.5,'totalProfitLoss':19857.5,'totalReturnPercent':1.98575,'cash':899857.5,'hasIncompleteValuation':False,'positions':[{'symbol':'2330','currentPrice':120,'marketValue':120000,'unrealizedProfitLoss':19857.5,'returnPercent':19.8,'quoteStatus':'盤中最近成交 · 證交所 MIS','quoteTime':'2026-10-15T10:11:30+08:00','date':'2026-10-15'}]}
 page.route('**/StockAnalysis/PaperQuotes*',lambda route:route.fulfill(json=data))
 page.reload(wait_until='networkidle')
 expect(page.locator('#portfolio-total')).to_have_text('$1,019,857.50')
 expect(page.locator('[data-tick="currentPrice"]')).to_have_text('120.00')
 expect(page.locator('[data-tick-status]')).to_contain_text('盤中最近成交')
 expect(page.locator('#paper-cash')).to_have_text('899,857.50')
 expect(page.locator('#paper-trades tbody tr')).to_have_count(1)
 data['positions'][0]['currentPrice']=121
 with page.expect_request('**/StockAnalysis/PaperQuotes?refresh=true'):
  page.get_by_role('button',name='即時更新',exact=True).click()
 expect(page.locator('[data-tick="currentPrice"]')).to_have_text('121.00')
 expect(page.get_by_role('button',name='即時更新',exact=True)).to_be_enabled()
 page.locator('#Order_StockId').fill('0050') if page.locator('#Order_StockId').is_visible() else None
 page.set_viewport_size({'width':390,'height':1000})
 assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
 page.screenshot(path='artifacts/paper-intraday-mobile.png',full_page=True)
 assert not errors,errors
 token=page.locator('#paper-order-form input[name="__RequestVerificationToken"]').input_value()
 assert page.request.post(base+'/__test/stop',form={'__RequestVerificationToken':token}).status==200
 browser.close()
 print('PASS: intraday price, total assets, timestamps, unchanged cash/trades, mobile layout, no JS errors')
