"""Phase4M presentation fixtures only; never production authentication."""
from pathlib import Path
import json
from playwright.sync_api import sync_playwright
r=Path(__file__).resolve().parents[1]
def camel(v):
 if isinstance(v,dict):return {k[0].lower()+k[1:]:camel(x) for k,x in v.items()}
 if isinstance(v,list):return [camel(x) for x in v]
 return v
report=camel(json.loads((r/'docs/phase4m/phase4m-state-3.2.2.json').read_text(encoding='utf-8-sig')))
script=(r/'src/WorkJournal.Web/wwwroot/js/research-readiness.js').read_text(encoding='utf-8-sig')
with sync_playwright() as pw:
 browser=pw.chromium.launch(channel='msedge',headless=True)
 page=browser.new_page(viewport={'width':390,'height':844});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 def route(h):
  if h.request.url.endswith('/fixture'):return h.fulfill(content_type='text/html',body='<section id="phase4m-readiness" data-admin="true"></section>')
  if '/authority/workbench' in h.request.url:return h.fulfill(json={'items':report['reviewQueue'],'totalCount':len(report['reviewQueue'])})
  return h.fulfill(json=report)
 page.route('http://phase4m.test/**',route);page.goto('http://phase4m.test/fixture');page.add_script_tag(content=script)
 page.get_by_role('heading',name='資料與研究準備度',exact=True).wait_for()
 assert 'NOT_RUN' in page.locator('#phase4m-readiness').inner_text()
 assert 'Disabled' in page.locator('#phase4m-readiness').inner_text()
 page.get_by_role('button',name='載入證據審查佇列').click()
 page.get_by_role('heading',name='審查工作（寫入使用受控操作流程）').wait_for()
 assert len(page.locator('section').last.locator('li').all())==43
 assert page.evaluate('document.documentElement.scrollWidth <= window.innerWidth')
 page.screenshot(path=str(r/'artifacts/phase4m-mobile.png'),full_page=True)
 page.unroute('http://phase4m.test/**');page.route('http://phase4m.test/**',lambda h:h.fulfill(status=401,json={}))
 page.add_script_tag(content=script);page.get_by_text('請重新登入。',exact=True).wait_for()
 assert 'UNASSIGNED' not in page.locator('#phase4m-readiness').inner_text()
 assert not errors,errors
 browser.close()
print('PASS: Phase4M six-stage readiness, disabled scheduler, 43 admin review tasks, mobile wrapping, expired-session clearing. Fixture only.')
