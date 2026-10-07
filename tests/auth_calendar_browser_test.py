"""UI checks on the synthetic OAuth + isolated SQL fixture only; never contacts Google."""
from pathlib import Path
import sys
from playwright.sync_api import sync_playwright, expect

base = sys.argv[1] if len(sys.argv) > 1 else 'http://localhost:5188'
out = Path(__file__).resolve().parents[1] / 'artifacts'
with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', headless=True)
    page = browser.new_page(viewport={'width':1440,'height':1000}, locale='zh-TW')
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    response = page.goto(base + '/GoogleCalendar', wait_until='networkidle')
    assert response.headers.get('x-authcalendar-fixture') == 'isolated-sql', 'Only run against synthetic fixture'
    logout = page.get_by_role('button',name='登出',exact=True)
    if logout.count():
        page.locator('#nav-account').click()
        logout.click()
    page.goto(base + '/Account/Login', wait_until='networkidle')
    expect(page.get_by_role('heading',name='登入工作日誌')).to_be_visible()
    page.screenshot(path=str(out/'auth-login-desktop.png'),full_page=True)
    page.get_by_role('button',name='使用 Google 登入',exact=True).click()
    page.wait_for_url(base + '/WorkLogs')
    expect(page.locator('.account-name')).to_contain_text('alice@example.test')
    page.get_by_role('link',name='管理 Google 行事曆連結',exact=True).click()
    page.get_by_role('button',name='連結 Google 行事曆',exact=True).click()
    expect(page.locator('main')).to_contain_text('Connected')
    expect(page.get_by_role('button',name='立即雙向同步',exact=True)).to_be_visible()
    expect(page.locator('#sync-month')).to_be_visible()
    expect(page.locator('main')).to_contain_text('同步不是背景即時執行')
    page.screenshot(path=str(out/'auth-calendar-connected.png'),full_page=True)
    page.get_by_role('link',name='新增 Google 事件',exact=True).click()
    page.locator('#Title').fill('瀏覽器測試事件')
    page.locator('#Start').fill('2026-10-05T09:00')
    page.locator('#End').fill('2026-10-05T10:00')
    page.get_by_role('button',name='儲存到 Google',exact=True).click()
    expect(page.locator('.alert-success')).to_be_visible()
    page.goto(base + '/WorkLogs?CalendarMonth=2026-10-01',wait_until='networkidle')
    expect(page.locator('#calendar .google-event')).to_have_count(1)
    page.locator('#calendar .google-event').click()
    expect(page.locator('#Title')).to_have_value('Google fixture')
    assert page.locator('#Start').input_value().startswith('2026-10-02T07:00')
    page.set_viewport_size({'width':390,'height':844})
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Event editor overflows mobile'
    page.screenshot(path=str(out/'auth-event-mobile.png'),full_page=True)
    page.get_by_role('button',name='儲存到 Google',exact=True).click()
    page.goto(base + '/GoogleCalendar/Delete/event1')
    page.get_by_role('button',name='確認刪除',exact=True).click()
    expect(page.locator('.alert-success')).to_be_visible()
    page.goto(base + '/GoogleCalendar')
    assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'), 'Connection page overflows mobile'
    expect(page.get_by_role('button',name='立即雙向同步',exact=True)).to_be_visible()
    page.locator('#sync-month').fill('2040-01-01')
    page.get_by_role('button',name='立即雙向同步',exact=True).click()
    expect(page.locator('.alert-success')).to_contain_text('同步完成')
    page.get_by_role('button',name='中斷連結',exact=True).click()
    expect(page.locator('main')).to_contain_text('尚未連結')
    page.get_by_role('button',name='切換導覽選單').click()
    page.locator('#nav-account').click()
    page.get_by_role('button',name='登出',exact=True).click()
    expect(page.get_by_role('heading',name='登入工作日誌')).to_be_visible()
    assert not errors, errors
    browser.close()
print('PASS: synthetic Google login, connect, event CRUD, disconnect, logout, mobile layouts, no JS errors.')
