"""Deterministic UI and database integration; isolated PaperTradingChecks --serve only."""
from playwright.sync_api import sync_playwright,expect
base='http://localhost:5187'
with sync_playwright() as p:
    browser=p.chromium.launch(channel='msedge',headless=True)
    context=browser.new_context(viewport={'width':1440,'height':1000},locale='zh-TW')
    page=context.new_page();errors=[]
    page.on('pageerror',lambda e:errors.append(str(e)))
    response=page.goto(base+'/StockAnalysis/MarketRadar',wait_until='networkidle')
    assert response.headers.get('x-papertrading-fixture')=='isolated-sql'
    token=page.locator('#market-radar input[name="__RequestVerificationToken"]').input_value()
    assert page.request.post(base+'/StockAnalysis/SaveWatchlist',data={'remove':'2330'},headers={'RequestVerificationToken':token}).ok
    page.reload(wait_until='networkidle')
    expect(page.get_by_role('heading',name='市場熱門排行',exact=True)).to_be_visible()
    expect(page.locator('#radar-status')).to_contain_text('已完成',timeout=30000)
    expect(page.locator('.radar-card')).to_have_count(4)
    card=page.locator('.radar-card[data-symbol="2330"]')
    expect(card.locator('.radar-score')).to_contain_text('/ 100')
    assert card.get_by_role('link',name='建立虛擬單').get_attribute('href').endswith('symbol=2330')
    expect(card.get_by_role('button',name='加入自選')).to_be_enabled()
    assert page.request.post(base+'/StockAnalysis/SaveWatchlist',data={'symbols':['2330']}).status==400
    card.get_by_role('button',name='加入自選').click()
    expect(card.get_by_role('button',name='已加入自選')).to_be_disabled()
    assert any(x['symbol']=='2330' for x in page.request.get(base+'/StockAnalysis/Watchlist').json()['items'])
    page.reload(wait_until='networkidle')
    expect(card.get_by_role('button',name='已加入自選')).to_be_disabled(timeout=30000)
    card.locator('summary').click()
    expect(card.locator('.radar-score-details li')).to_have_count(6)
    page.screenshot(path='artifacts/radar-desktop.png',full_page=True)
    for tab,symbol in [('gainers','2330'),('losers','006208'),('volume','6488'),('average','6488')]:
        page.locator('#tab-'+tab).click()
        expect(page.locator('.radar-table tbody tr').first).to_have_attribute('data-symbol',symbol)
    expect(page.locator('.radar-table tbody tr')).to_have_count(4)
    page.locator('#radar-average-filter').select_option('unusual')
    expect(page.locator('.radar-table tbody tr')).to_have_count(3)
    page.locator('#tab-events').click()
    expect(page.locator('.radar-event')).to_have_count(2)
    assert page.locator('.radar-event').first.locator('img,script,a').count()==0
    expect(page.locator('.radar-event h2').first).to_contain_text('<img')
    page.locator('#radar-market').select_option('etf')
    expect(page.locator('#radar-status')).to_contain_text('已完成 2',timeout=30000)
    page.locator('#tab-volume').click()
    expect(page.locator('.radar-table tbody tr')).to_have_count(2)
    page.locator('#radar-market').select_option('tpex')
    expect(page.locator('#radar-status')).to_contain_text('已完成 1',timeout=30000)
    expect(page.locator('.radar-table tbody tr').first).to_have_attribute('data-symbol','6488')
    page.locator('#radar-market').select_option('all')
    expect(page.locator('#radar-status')).to_contain_text('已完成 4',timeout=30000)
    page.locator('#radar-date').select_option('2026-10-04')
    expect(page.locator('#radar-status')).to_contain_text('已完成 1',timeout=30000)
    expect(page.locator('.radar-table tbody tr').first).to_have_attribute('data-symbol','1234')
    page.locator('#radar-date').select_option('2026-10-05')
    expect(page.locator('#radar-status')).to_contain_text('已完成 4',timeout=30000)
    page.set_viewport_size({'width':390,'height':844})
    assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),'Mobile overflow in table'
    page.locator('#tab-recommend').click()
    assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),'Mobile overflow in cards'
    page.screenshot(path='artifacts/radar-mobile.png',full_page=True)
    card.get_by_role('link',name='建立虛擬單').click()
    expect(page.locator('#Order_StockId')).to_have_value('2330')
    expect(page.locator('#paper-cash')).to_have_text('1,000,000.00')
    assert page.request.get(base+'/StockAnalysis/MarketRadarData?market=invalid').status==400
    assert page.request.get(base+'/StockAnalysis/MarketRadarData?date=bad').status==400
    assert page.request.post(base+'/StockAnalysis/SaveWatchlist',data={'remove':'2330'},headers={'RequestVerificationToken':token}).ok
    assert not errors,errors
    browser.close()
print('PASS: radar rankings, means, scores, filters, dates, safe event text, database watchlist, CSRF, paper form prefill and mobile layout.')
