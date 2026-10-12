"""Run against InvestmentInsightsChecks --serve (isolated SQL, synthetic prices)."""
from playwright.sync_api import sync_playwright, expect
import json
from pathlib import Path

base='http://localhost:5186'
checks=[]
def passed(label): checks.append(label)
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 context=browser.new_context(extra_http_headers={'X-Test-User':'alice'})
 page=context.new_page(); errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.goto(base+'/StockAnalysis/PaperTrading',wait_until='networkidle')
 expect(page.locator('h1')).to_have_text('我的虛擬投資組合')
 expect(page.locator('#portfolio-chart svg')).to_have_count(1)
 # Last capture drops from 1.4M to 1,000,100; chart uses all daily observations.
 expect(page.locator('#portfolio-drawdown')).to_have_text('-28.56%')
 expect(page.locator('#portfolio-panel-positions')).to_be_visible()
 expect(page.locator('#portfolio-panel-orders')).to_be_hidden()
 for name in ['cash','positions','orders','trades','pnl']:
  page.locator('#portfolio-tab-'+name).click()
  expect(page.locator('#portfolio-panel-'+name)).to_be_visible()
  assert page.locator('.portfolio-tab-panel:visible').count()==1
 passed('five tabs display one panel; complete-history drawdown')
 page.locator('[data-portfolio-open=orders]').click()
 expect(page.locator('#paper-order-form')).to_be_visible()
 page.locator('#portfolio-tab-orders').focus();page.keyboard.press('ArrowRight')
 expect(page.locator('#portfolio-tab-trades')).to_be_focused()
 expect(page.locator('#portfolio-panel-trades')).to_be_visible()
 passed('create button and keyboard tabs')
 svg=page.locator('#portfolio-chart svg');svg.focus();before=page.locator('#portfolio-point').inner_text();page.keyboard.press('ArrowLeft')
 assert page.locator('#portfolio-point').inner_text()!=before
 for value in ['1','3','6','12','all']:
  page.locator('#portfolio-range').select_option(value);expect(svg).to_have_count(1)
 passed('equity ranges and keyboard inspection')
 page.screenshot(path='artifacts/portfolio142-desktop.png',full_page=True)
 page.set_viewport_size({'width':390,'height':844})
 for name in ['cash','positions','orders','trades','pnl']:
  page.locator('#portfolio-tab-'+name).click()
  assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'),name
 page.locator('#portfolio-tab-positions').click();page.screenshot(path='artifacts/portfolio142-mobile.png',full_page=True)
 passed('all portfolio tabs fit mobile viewport')
 alice=context.request.get(base+'/StockAnalysis/PortfolioHistory').json()
 bob=browser.new_context(extra_http_headers={'X-Test-User':'bob'})
 assert len(alice['points'])==5
 assert bob.request.get(base+'/StockAnalysis/PortfolioHistory').json()['points']==[]
 bp=bob.new_page();bp.goto(base+'/StockAnalysis/PaperTrading',wait_until='networkidle');expect(bp.locator('#portfolio-chart')).to_contain_text('尚待累積')
 passed('personal API isolation and genuine empty state')
 # Mutations retain existing antiforgery protection.
 assert context.request.post(base+'/StockAnalysis/PlacePaperOrder',form={'Order.StockId':'2330','Order.Quantity':'1'}).status==400
 passed('order CSRF protection')
 page.goto(base+'/StockAnalysis/Index',wait_until='networkidle')
 expect(page.locator('.pulse-status')).to_contain_text('2026-10-12')
 expect(page.locator('.pulse-heat')).to_contain_text('/ 100')
 expect(page.locator('.pulse-breadth')).to_contain_text('上漲 140')
 page.locator('.pulse-market').select_option('twse');expect(page.locator('.pulse-breadth')).to_contain_text('上漲 70')
 page.locator('.pulse-industries summary').first.click();expect(page.locator('.pulse-industries a').first).to_be_visible()
 assert 'symbol=2330' in page.locator('.pulse-industries a').first.get_attribute('href')
 assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
 page.screenshot(path='artifacts/market142-mobile.png',full_page=True)
 page.set_viewport_size({'width':1440,'height':1000});page.screenshot(path='artifacts/market142-desktop.png',full_page=True)
 passed('official-market views, scoped breadth, constituent links, mobile layout')
 page.route('**/MarketPulse?*',lambda r:r.fulfill(status=503,json={'message':'unavailable'}))
 page.locator('.pulse-retry').click();expect(page.locator('.pulse-status')).to_contain_text('暫時無法取得')
 assert page.locator('.pulse-indices > *').count()==0
 page.unroute('**/MarketPulse?*');page.locator('.pulse-retry').click();expect(page.locator('.pulse-status')).to_contain_text('2026-10-12')
 passed('source outage clears stale statistics and retry recovers')
 page.goto(base+'/StockAnalysis/PaperTrading?symbol=2330&side=Sell',wait_until='networkidle')
 expect(page.locator('#portfolio-panel-orders')).to_be_visible();expect(page.locator('#Order_StockId')).to_have_value('2330')
 expect(page.locator('#Order_Side')).to_have_value('Sell')
 passed('existing stock action opens prefilled order tab')
 assert not errors,errors
 passed('no JavaScript errors')
 Path('artifacts/142-browser-results.json').write_text(json.dumps({'passed':checks,'errors':errors},ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({'passed':len(checks),'errors':errors}))
 browser.close()
