from playwright.sync_api import sync_playwright,expect
base='http://localhost:5189'
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 c=browser.new_context(viewport={'width':1440,'height':1100},locale='zh-TW');page=c.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(base+'/StockAnalysis/Index?symbol=台積電',wait_until='networkidle');assert 'symbol=2330' in page.url
 expect(page.locator('footer')).to_contain_text('1.4.1');expect(page.locator('[role=tab]')).to_have_count(8)
 expect(page.locator('[data-chart=line] svg')).to_have_count(1)
 expect(page.locator('#volume-summary')).to_contain_text('1 倍')
 expect(page.locator('#flow-detail')).to_contain_text('自營商')
 expect(page.locator('#radar-headline')).not_to_contain_text('載入中')
 for tab in ['candles','technical','flows','fundamental','events','strategy','performance','overview']:
  page.locator('#tab-'+tab).click();expect(page.locator('#panel-'+tab)).to_be_visible()
  assert page.locator('[role=tabpanel]:visible').count()==1
 page.locator('[data-chart=line] select').select_option('12');page.locator('[data-chart=line] svg').focus();page.keyboard.press('ArrowLeft')
 expect(page.locator('[data-chart=line] .chart-tooltip')).to_contain_text('MA60 100')
 page.locator('[data-chart=line] input').first.uncheck()
 page.screenshot(path='artifacts/research141-desktop.png',full_page=True)
 # Create independent favorite then swing, remove favorite and ensure swing remains.
 page.locator('[data-membership-button=favorite]').click();page.wait_for_url('**/Favorites')
 row=page.locator('tr[data-symbol="2330"]');expect(row).to_have_count(1)
 row.locator('.add-to-swing-form button').click();page.wait_for_url('**/Swing')
 page.goto(base+'/StockAnalysis/Favorites');page.locator('tr[data-symbol="2330"]').get_by_role('button',name='移除自選').click()
 expect(page.locator('tr[data-symbol="2330"]')).to_have_count(0)
 assert any(x['symbol']=='2330' for x in c.request.get(base+'/StockAnalysis/Watchlist').json()['items'])
 page.goto(base+'/StockAnalysis/Index?symbol=2330',wait_until='networkidle')
 expect(page.get_by_role('link',name='↗ 查看波段分析')).to_be_visible()
 assert c.request.post(base+'/StockAnalysis/ChangeFavorite',form={'symbol':'2330'}).status==400
 bob=browser.new_context(extra_http_headers={'X-Test-User':'bob'})
 m=bob.request.get(base+'/StockAnalysis/StockMembership?symbol=2330').json();assert m['favorite']==False and m['swing']==False
 anon=browser.new_context(extra_http_headers={'X-Test-User':'anonymous'})
 for endpoint in ['Favorites','StockMembership?symbol=2330','StockPerformance?symbol=2330']:
  assert anon.request.get(base+'/StockAnalysis/'+endpoint,max_redirects=0).status in [302,401]
 assert c.request.get(base+'/StockAnalysis/ResearchData?symbol=invalid').status==400
 assert c.request.get(base+'/StockAnalysis/StockRadar?symbol=invalid').status==400
 page.set_viewport_size({'width':390,'height':844});page.goto(base+'/StockAnalysis/Index?symbol=2330',wait_until='networkidle')
 for tab in ['overview','candles','technical','flows','fundamental','events','strategy','performance']:
  page.locator('#tab-'+tab).click();assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),tab
 page.locator('#tab-overview').click();page.screenshot(path='artifacts/research141-mobile.png',full_page=True)
 # Independent sections survive an unavailable chart source.
 page.route('**/ResearchData?*',lambda route:route.fulfill(status=503,json={'message':'fixture outage'}))
 page.reload(wait_until='networkidle');expect(page.locator('#trend-summary')).to_contain_text('暫時無法取得');expect(page.locator('#stock-quote-heading')).to_contain_text('台積電')
 page.unroute('**/ResearchData?*');page.locator('#retry-history').click();expect(page.locator('[data-chart=line] svg')).to_have_count(1)
 # Search ambiguous ETF names and missing symbols; no unsafe markup.
 page.goto(base+'/StockAnalysis/Index?symbol=50',wait_until='networkidle');expect(page.locator('.research-search-results a')).to_have_count(2)
 page.goto(base+'/StockAnalysis/Index?symbol=0050',wait_until='networkidle');page.locator('#tab-fundamental').click();expect(page.locator('#panel-fundamental')).to_contain_text('ETF')
 assert not errors,errors
 browser.close()
print('PASS: stock name search, 8 tabs, charts/ranges/keyboard, ETF, independent collections, account isolation, CSRF/auth, provider recovery, mobile, JS.')
