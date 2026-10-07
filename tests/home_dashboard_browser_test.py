from pathlib import Path
from playwright.sync_api import sync_playwright, expect
base='http://localhost:5191'
out=Path(__file__).resolve().parents[1]/'artifacts'
with sync_playwright() as p:
 browser=p.chromium.launch(channel='msedge',headless=True)
 context=browser.new_context(viewport={'width':1440,'height':1000},locale='zh-TW',extra_http_headers={'X-Test-User':'alice'})
 page=context.new_page(); errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 response=page.goto(base,wait_until='networkidle')
 assert response.status==200
 expect(page.locator('#home-market')).to_have_attribute('aria-busy','false')
 expect(page.locator('#home-calendar')).to_have_attribute('aria-busy','false')
 expect(page.locator('.radar-top li')).to_have_count(5)
 expect(page.locator('#today-calendar-count')).to_have_text('2')
 assert page.locator('main script:not([src]), main img[onerror]').count()==0
 assert 'BOB_PRIVATE' not in page.locator('main').inner_text()
 page.screenshot(path=str(out/'home131-desktop.png'),full_page=True)
 # Keyboard dropdown opens, arrows focus items, Escape closes.
 page.locator('#nav-group-1').focus();page.keyboard.press('Enter')
 expect(page.locator('#nav-group-1')).to_have_attribute('aria-expanded','true')
 page.keyboard.press('ArrowDown')
 assert page.locator(':focus').evaluate('(el)=>el.classList.contains("dropdown-item")')
 page.keyboard.press('Escape')
 expect(page.locator('#nav-group-1')).to_have_attribute('aria-expanded','false')
 for width in [1024,768,390,320]:
  page.set_viewport_size({'width':width,'height':844})
  assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'),f'overflow {width}'
  toggle=page.get_by_role('button',name='切換導覽選單')
  toggle.click();expect(toggle).to_have_attribute('aria-expanded','true')
  page.wait_for_function("!document.querySelector('#main-navigation').classList.contains('collapsing')")
  page.locator('#nav-group-2').click()
  expect(page.locator('#nav-group-2')).to_have_attribute('aria-expanded','true')
  assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'),f'dropdown overflow {width}'
  if width==390:page.screenshot(path=str(out/'home131-mobile-menu.png'),full_page=True)
  page.locator('#nav-group-2').click()
  toggle.click();expect(toggle).to_have_attribute('aria-expanded','false')
  page.wait_for_timeout(400)
  if width==390:page.screenshot(path=str(out/'home131-mobile.png'),full_page=True)
 # Lost widget network leaves navigation and personal summaries usable.
 page.route('**/Home/MarketSummary',lambda route:route.abort())
 page.route('**/Home/CalendarSummary',lambda route:route.abort())
 page.reload(wait_until='networkidle')
 expect(page.locator('#home-market')).to_contain_text('市場資料暫時無法取得')
 expect(page.locator('#home-calendar')).to_contain_text('行程資料暫時無法取得')
 expect(page.locator('h1')).to_contain_text('alice')
 assert not errors,errors
 anonymous=browser.new_context(viewport={'width':390,'height':844})
 anon=anonymous.new_page();anon.goto(base,wait_until='networkidle')
 assert 'BOB_PRIVATE' not in anon.locator('main').inner_text()
 expect(anon.get_by_role('heading',name='工作有紀錄，投資有方向')).to_be_visible()
 assert anon.locator('#home-calendar').count()==0
 anon.screenshot(path=str(out/'home131-anonymous.png'),full_page=True)
 browser.close()
print('PASS: home owner isolation, encoded titles, lazy widgets, keyboard dropdown, 320/390/768/1024/1440 layouts, provider failure, anonymous view, no JS errors.')
