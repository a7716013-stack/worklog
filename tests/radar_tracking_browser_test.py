"""1.2.4 deterministic UI checks; dedicated 5189 fixture only."""
from playwright.sync_api import sync_playwright,expect
import html
base='http://localhost:5189'
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 ctx=browser.new_context(viewport={'width':1440,'height':1000},locale='zh-TW')
 page=ctx.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 response=page.goto(base+'/StockAnalysis/RadarTracking',wait_until='networkidle');assert response.ok
 expect(page.locator('#radar-tracking-table tbody tr')).to_have_count(10)
 assert page.request.post(base+'/StockAnalysis/CaptureMarketRadar',form={}).status==400
 page.locator('select[name="Mine"]').select_option('true');page.get_by_role('button',name='套用篩選').click()
 expect(page.locator('#radar-tracking-table tbody tr')).to_have_count(1)
 page.get_by_role('button',name='停止追蹤').click();expect(page.get_by_role('button',name='恢復追蹤')).to_be_visible()
 page.get_by_role('button',name='恢復追蹤').click();expect(page.get_by_role('button',name='停止追蹤')).to_be_visible()
 page.locator('#radar-tracking-table a[href*="RadarSnapshot"]').click()
 assert page.locator('img[src="x"]').count()==0 and page.locator('a[href^="javascript:"]').count()==0
 expect(page.get_by_text('影響待確認',exact=False)).to_be_visible()
 expect(page.get_by_role('heading',name='後續績效明細')).to_be_visible()
 page.get_by_role('link',name='建立虛擬單',exact=True).click()
 expect(page.locator('#Order_StockId')).to_have_value('2330')
 rec=page.locator('#RecommendationId').input_value();assert int(rec)>0
 token=page.locator('input[name="__RequestVerificationToken"]').first.input_value()
 generation=page.locator('#Order_AccountGeneration').input_value()
 requestid=page.locator('#Order_ClientRequestId').input_value()
 forged={'RecommendationId':rec,'Order.StockId':'0050','Order.Quantity':'10','Order.Side':'Buy','Order.OrderType':'Market','Order.AccountGeneration':generation,'Order.ClientRequestId':requestid,'__RequestVerificationToken':token}
 mismatch=page.request.post(base+'/StockAnalysis/PlacePaperOrder',form=forged)
 assert mismatch.ok and '股票代號不一致' in html.unescape(mismatch.text())
 page.locator('#Order_Quantity').fill('10');page.get_by_role('button',name='送出虛擬委託',exact=True).click()
 expect(page.locator('#paper-orders tbody tr')).to_have_count(1)
 page.goto(base+'/StockAnalysis/RadarPaperComparison',wait_until='networkidle')
 expect(page.get_by_role('heading',name='雷達與我的虛擬單對照')).to_be_visible()
 assert '2330' in page.locator('tbody').inner_text()
 page.goto(base+'/StockAnalysis/RadarPerformance',wait_until='networkidle')
 expect(page.get_by_role('heading',name='分數分桶驗證')).to_be_visible()
 assert '20.00%' in page.locator('body').inner_text()
 page.screenshot(path='artifacts/radar-performance124-desktop.png',full_page=True)
 for name in ['RadarTracking','RadarPerformance','RadarPaperComparison']:
  page.set_viewport_size({'width':390,'height':844});page.goto(base+'/StockAnalysis/'+name,wait_until='networkidle')
  assert page.evaluate('document.documentElement.scrollWidth <= innerWidth+1'),name+' overflow'
 page.screenshot(path='artifacts/radar-tracking124-mobile.png',full_page=True)
 bob=browser.new_context(extra_http_headers={'X-Test-User':'bob'})
 other=bob.new_page();other.goto(base+'/StockAnalysis/RadarPaperComparison',wait_until='networkidle')
 assert other.locator('tbody tr').count()==0
 anon=browser.new_context(extra_http_headers={'X-Test-User':'anonymous'});assert anon.request.get(base+'/StockAnalysis/RadarTracking').status==401
 assert not errors,errors
 print('PASS: tracking filters, stop/rejoin, snapshot XSS protection, CSRF, paper source validation/submit, statistics, mobile, user isolation and anonymous access; 0 JS errors')
 browser.close()
