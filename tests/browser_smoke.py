"""Independent smoke test for the real Node provider; does not exercise the .NET implementation."""
import json
import os
import shutil
import subprocess
import time
import urllib.request
import urllib.error
from pathlib import Path
from playwright.sync_api import sync_playwright
ROOT = Path(__file__).resolve().parents[1]
BASE = 'http://127.0.0.1:5071'

def post(path, payload, headers=None):
    req = urllib.request.Request(BASE+path, json.dumps(payload).encode(), {'Content-Type':'application/json', **(headers or {})}, method='POST')
    try:
        with urllib.request.urlopen(req, timeout=4) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as e:
        return e.code, json.load(e)

def main():
    # Don't accidentally use a different server on this port.
    try:
        urllib.request.urlopen(BASE+'/api/state', timeout=.5).close()
    except (OSError, urllib.error.URLError):
        pass
    else:
        raise RuntimeError('Port 5071 already in use; stop the running demo before this test.')
    process = subprocess.Popen(['node', str(ROOT/'demo-provider/server.mjs')], stdout=subprocess.DEVNULL)
    try:
        for _ in range(50):
            if process.poll() is not None: raise RuntimeError('Provider failed to start')
            try:
                with urllib.request.urlopen(BASE+'/api/state', timeout=.5) as r:
                    if json.load(r)['version'] == 1: break
            except OSError: time.sleep(.1)
        else: raise RuntimeError('Provider startup timed out')
        with sync_playwright() as p:
            chromium = os.environ.get('BROWSER_EXECUTABLE') or shutil.which('chromium')
            browser = p.chromium.launch(headless=True, **({'executable_path':chromium} if chromium else {}))
            def capture():
                page = browser.new_page()
                try:
                    page.goto(BASE)
                    page.locator('#property').fill('HOTEL-101')
                    with page.expect_response(lambda r:r.request.method=='POST' and r.request.resource_type in ('fetch','xhr')) as pending:
                        page.locator('#search').click()
                    r = pending.value
                    return {'url':r.url,'headers':r.request.all_headers(),'body':r.request.post_data_json,'status':r.status,'response':r.json()}
                finally: page.close()
            old = capture()
            assert old['status']==200 and old['url'].endswith('/api/hotel/search')
            assert old['body']['operationName']=='HotelSearchV1' and old['headers']['x-demo-contract']=='1'
            assert len(old['response']['data']['rates'])==2
            assert post('/admin/version',{'version':2})[0]==200
            assert post('/api/hotel/search',old['body'],{'x-demo-contract':'1'})[0]==404
            new = capture()
            assert new['status']==200 and new['url'].endswith('/api/v2/availability')
            assert new['headers']['x-demo-contract']=='2' and new['body']['operationName']=='PropertyAvailability'
            assert 'propertyId' in new['body']['variables'] and 'hotelId' not in new['body']['variables']
            assert new['response']['data']['rates'][0]['translatedName'] is None
            assert len(new['response']['data']['rates'])==2
            assert post('/admin/version',{'version':3})[0]==400
            browser.close()
        print('PASS: real browser captured v1 and v2; old HTTP request rejected; endpoint, operation, header, payload and nullability drift verified.')
    finally:
        process.terminate();process.wait(timeout=5)
if __name__=='__main__': main()
