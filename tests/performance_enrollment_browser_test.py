from urllib.parse import urlparse,parse_qs
from playwright.sync_api import sync_playwright,expect
base='http://localhost:5189'
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 context=browser.new_context(viewport={'width':1440,'height':1000},locale='zh-TW')
 page=context.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(base+'/StockAnalysis/Swing',wait_until='networkidle')
 token=page.locator('#swing-app > input[name="__RequestVerificationToken"]').input_value()
 assert context.request.post(base+'/StockAnalysis/AddPerformanceValidation',form={'symbol':'2330','origin':'swing'}).status==400
 saved=context.request.post(base+'/StockAnalysis/SaveWatchlist',data={'symbols':['2330']},headers={'RequestVerificationToken':token})
 assert saved.ok
 page.reload(wait_until='networkidle')
 card=page.locator('.swing-card[data-symbol="2330"]')
 expect(card.get_by_role('button',name='加入績效模型驗證')).to_be_visible()
 page.screenshot(path='artifacts/validation-swing-desktop.png',full_page=True)
 card.get_by_role('button',name='加入績效模型驗證').click()
 page.wait_for_url('**/RadarPerformance?*')
 query=parse_qs(urlparse(page.url).query)
 assert query['Mine']==['True'] or query['Mine']==['true']
 assert query['Symbol']==['2330'] and query['Basis']==['open']
 assert urlparse(page.url).fragment=='model-validation'
 expect(page.locator('.alert-success')).to_contain_text('已加入個人績效模型驗證')
 expect(page.locator('#model-validation')).to_be_visible()
 # Existing paper orders and positions each expose a CSRF-protected enrollment option.
 page.goto(base+'/StockAnalysis/PaperTrading',wait_until='networkidle')
 page.locator('#Order_StockId').fill('2330');page.locator('#Order_Quantity').fill('1')
 page.get_by_role('button',name='送出虛擬委託',exact=True).click()
 expect(page.locator('#paper-orders .performance-validation-form')).to_have_count(1)
 expect(page.locator('#paper-positions .performance-validation-form')).to_have_count(1)
 expect(page.locator('#paper-orders tbody tr').first).to_have_attribute('data-status','Filled')
 page.screenshot(path='artifacts/validation-paper-desktop.png',full_page=True)
 page.locator('#paper-orders .performance-validation-form button').first.click()
 page.wait_for_url('**/RadarPerformance?*')
 expect(page.locator('.alert-success')).to_contain_text('已加入個人績效模型驗證')
 # An authenticated different owner cannot enroll Alice's order or watchlist.
 bob=browser.new_context(extra_http_headers={'X-Test-User':'bob'})
 b=bob.new_page();b.goto(base+'/StockAnalysis/Swing')
 bt=b.locator('#swing-app > input[name="__RequestVerificationToken"]').input_value()
 for origin in ['swing','paper']:
  r=bob.request.post(base+'/StockAnalysis/AddPerformanceValidation',form={'symbol':'2330','origin':origin,'__RequestVerificationToken':bt},max_redirects=0)
  assert r.status==302 and 'RadarPerformance' not in r.headers['location']
 anon=browser.new_context(extra_http_headers={'X-Test-User':'anonymous'})
 assert anon.request.post(base+'/StockAnalysis/AddPerformanceValidation',form={'symbol':'2330','origin':'paper'},max_redirects=0).status in [302,401]
 for path in ['/StockAnalysis/Swing','/StockAnalysis/PaperTrading']:
  page.set_viewport_size({'width':390,'height':844})
  page.goto(base+path,wait_until='networkidle')
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
  expect(page.locator('.performance-validation-form').first).to_be_visible()
  page.screenshot(path='artifacts/validation-'+('swing' if 'Swing' in path else 'paper')+'-mobile.png',full_page=True)
 assert not errors,errors
 browser.close()
print('PASS: swing/paper enrollment buttons, personal model redirect, duplicate enrollment, protected owner/CSRF, mobile and no JS errors.')
