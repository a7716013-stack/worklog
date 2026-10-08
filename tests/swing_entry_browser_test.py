from playwright.sync_api import sync_playwright,expect
base='http://localhost:5189'
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 c=browser.new_context();page=c.new_page()
 page.goto(base+'/StockAnalysis/PaperTrading',wait_until='networkidle')
 page.locator('#Order_StockId').fill('2330');page.locator('#Order_Quantity').fill('1')
 page.get_by_role('button',name='送出虛擬委託',exact=True).click()
 assert page.locator('#paper-orders .add-to-swing-form').count() >= 1
 expect(page.locator('#paper-positions .add-to-swing-form')).to_have_count(1)
 assert c.request.post(base+'/StockAnalysis/AddToSwing',form={'symbol':'2330'}).status==400
 page.locator('#paper-orders .add-to-swing-form button').first.click();page.wait_for_url('**/Swing')
 expect(page.locator('.alert-success')).to_contain_text('2330')
 items=c.request.get(base+'/StockAnalysis/Watchlist').json()['items'];assert sum(x['symbol']=='2330' for x in items)==1
 page.goto(base+'/StockAnalysis/RadarPerformance?Symbol=2330',wait_until='networkidle')
 expect(page.locator('#performance-swing-targets .add-to-swing-form')).to_have_count(1)
 page.locator('#performance-swing-targets button').click();page.wait_for_url('**/Swing')
 expect(page.locator('.alert-success')).to_contain_text('已在')
 assert len(c.request.get(base+'/StockAnalysis/Watchlist').json()['items'])==len(items)
 bob=browser.new_context(extra_http_headers={'X-Test-User':'bob'})
 assert not any(x['symbol']=='2330' for x in bob.request.get(base+'/StockAnalysis/Watchlist').json()['items'])
 b=bob.new_page();b.goto(base+'/StockAnalysis/RadarPerformance?Mine=true&Symbol=2330')
 # Fixture explicitly follows 2330 for Bob; his watchlist remains separate.
 expect(b.locator('#performance-swing-targets .add-to-swing-form')).to_have_count(1)
 anon=browser.new_context(extra_http_headers={'X-Test-User':'anonymous'})
 assert anon.request.post(base+'/StockAnalysis/AddToSwing',form={'symbol':'2330'},max_redirects=0).status in [302,401]
 for route in ['PaperTrading','RadarPerformance?Symbol=2330','RadarTracking']:
  page.set_viewport_size({'width':390,'height':844});page.goto(base+'/StockAnalysis/'+route,wait_until='networkidle')
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
  expect(page.locator('.add-to-swing-form').first).to_be_visible()
 token=page.locator('input[name="__RequestVerificationToken"]').first.input_value()
 assert c.request.post(base+'/StockAnalysis/AddToSwing',form={'symbol':'bad!','__RequestVerificationToken':token}).status==400
 c.request.post(base+'/__test/stop',form={'__RequestVerificationToken':token})
 browser.close()
print('PASS: paper positions/orders, filtered performance targets, persistence, duplicate, owner isolation, auth/CSRF, mobile.')
