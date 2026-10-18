from pathlib import Path
import argparse,base64,datetime,http.server,json,os,subprocess,sys,threading,time,urllib.request
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'browser-test-deps'))
import websocket
STATE=ROOT/'chrome-simulation-state.json'
class Cdp:
 def __init__(self,url):self.ws=websocket.create_connection(url,timeout=8,suppress_origin=True);self.serial=0
 def call(self,method,params=None):
  self.serial+=1;n=self.serial;self.ws.send(json.dumps({'id':n,'method':method,'params':params or {}}))
  while True:
   reply=json.loads(self.ws.recv())
   if reply.get('id')==n:
    if 'error' in reply:raise RuntimeError(str(reply['error']))
    return reply.get('result',{})
 def evaluate(self,expression):
  reply=self.call('Runtime.evaluate',{'expression':expression,'returnByValue':True,'awaitPromise':True})
  if 'exceptionDetails' in reply:raise RuntimeError(str(reply['exceptionDetails']))
  return reply['result'].get('value')
 def close(self):self.ws.close()
def serve():
 class Handler(http.server.SimpleHTTPRequestHandler):
  def __init__(self,*a,**kw):super().__init__(*a,directory=str(ROOT),**kw)
  def log_message(self,*a):pass
  def do_GET(self):
   if self.path.split('?')[0]!='/chrome-input-test.html':self.send_error(404);return
   super().do_GET()
 server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler)
 url=f'http://127.0.0.1:{server.server_port}/chrome-input-test.html'
 profile=ROOT/'chrome-simulation-profile';profile.mkdir(exist_ok=True)
 chrome=Path(r'C:\Program Files\Google\Chrome\Application\chrome.exe')
 process=subprocess.Popen([str(chrome),'--user-data-dir='+str(profile),'--no-first-run','--no-default-browser-check','--disable-background-networking','--disable-component-update','--remote-debugging-port=0','--window-size=1100,750','--window-position=80,80',url])
 STATE.write_text(json.dumps({'fixture':url,'profile':str(profile),'browserPid':process.pid,'serverPid':os.getpid()}))
 server.serve_forever()
def connect():
 state=json.loads(STATE.read_text());port=int((Path(state['profile'])/'DevToolsActivePort').read_text().splitlines()[0])
 targets=json.load(urllib.request.urlopen(f'http://127.0.0.1:{port}/json/list',timeout=3))
 target=next(t for t in targets if t.get('url')==state['fixture'])
 return Cdp(target['webSocketDebuggerUrl']),target
def phase(name,report,side=1):
 phase_started=time.time()
 cdp,target=connect()
 try:
  cdp.call('Page.enable');cdp.call('Runtime.enable')
  window=cdp.call('Browser.getWindowForTarget',{'targetId':target['id']})['windowId']
  # Only this dedicated test window is positioned; user's browser profile is untouched.
  cdp.call('Browser.setWindowBounds',{'windowId':window,'bounds':{'windowState':'normal'}})
  bounds={'left':80,'top':80,'width':1100,'height':750} if side==1 else {'left':1940,'top':20,'width':560,'height':950}
  cdp.call('Browser.setWindowBounds',{'windowId':window,'bounds':bounds})
  cdp.call('Page.bringToFront');cdp.evaluate('resetMetrics('+json.dumps(name)+')');time.sleep(.2)
  checks=[];roundtrips=[]
  for element,expected in [('arrow','default'),('hand','pointer'),('wait','wait'),('progress','progress'),('resize','ew-resize'),('field','text')]:
   box=cdp.evaluate("(()=>{let e=document.getElementById("+json.dumps(element)+");let r=e.getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2,cursor:getComputedStyle(e).cursor}})()")
   x,y=box['x'],box['y'];cdp.call('Input.dispatchMouseEvent',{'type':'mouseMoved','x':x,'y':y});time.sleep(.04 if name.startswith('split') else .08)
   before=time.perf_counter();cdp.call('Input.dispatchMouseEvent',{'type':'mousePressed','x':x,'y':y,'button':'left','clickCount':1});cdp.call('Input.dispatchMouseEvent',{'type':'mouseReleased','x':x,'y':y,'button':'left','clickCount':1})
   delivered=cdp.evaluate('metrics.clicks.at(-1)?.id');roundtrips.append((time.perf_counter()-before)*1000)
   checks.append({'target':element,'clickDelivered':delivered==element,'cssCursor':box['cursor'],'cssCursorCorrect':box['cursor']==expected})
  cdp.call('Input.insertText',{'text':'SplitDeck browser smoke'})
  cdp.call('Input.dispatchMouseEvent',{'type':'mouseWheel','x':min(400,bounds['width']-50),'y':550,'deltaX':0,'deltaY':250})
  time.sleep(.8 if name.startswith('split') else 2)
  result=cdp.evaluate('results()');result.update({'checks':checks,'browserInputRoundTripMs':roundtrips,'browser':cdp.call('Browser.getVersion')['product'],'nativeDesktopMouseTested':False,'scope':'Chrome DevTools input delivery, CSS cursor styles and browser animation; not OS cursor latency or compositor frame delivery.'})
  result['pass']=all(c['clickDelivered'] and c['cssCursorCorrect'] for c in checks) and result['typed']=='SplitDeck browser smoke' and result['scrollY']>0
  result['windowBounds']=cdp.call('Browser.getWindowBounds',{'windowId':window})['bounds'];result['startedEpoch']=phase_started;result['finishedEpoch']=time.time()
  cdp.evaluate('scrollTo(0,0)');screenshot=cdp.call('Page.captureScreenshot',{'format':'png','captureBeyondViewport':False})['data'];Path(str(report)+'.png').write_bytes(base64.b64decode(screenshot))
  Path(report).write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));return 0 if result['pass'] else 1
 finally:cdp.close()
def utc_epoch(value):return datetime.datetime.fromisoformat(value.replace('Z','+00:00')).timestamp()

def live_sample(journal,now=None):
 now=time.time() if now is None else now
 try:
  state=json.loads(journal.read_text(encoding='utf-8-sig'));heartbeat=Path(str(journal)+'.heartbeat');stats=json.loads(Path(str(journal)+'.performance').read_text(encoding='utf-8-sig'))
  if state['Complete'] or not 0<=now-heartbeat.stat().st_mtime<=2 or not 0<=now-utc_epoch(stats['UpdatedUtc'])<=2 or stats['Frames']<=0:return None
  return {'helperId':state['HelperId'],'frames':stats['Frames'],'heartbeat':heartbeat.stat().st_mtime}
 except (OSError,ValueError,KeyError):return None

def accepted_split(summary,benchmark):
 try:
  start=utc_epoch(benchmark['ActiveStartedUtc']);end=utc_epoch(benchmark['ActiveEndedUtc'])
  return bool(summary['liveChecksPassed'] and benchmark['HelperId']==summary['helperId'] and len(benchmark['Active'])>=6 and benchmark['Restored'] and benchmark['HelperExited'] and not benchmark['Error'] and not benchmark['Abort'] and all(p['pass'] and start<=p['startedEpoch']<=p['finishedEpoch']<=end for p in (summary['primary'],summary['portrait'])))
 except (TypeError,ValueError,KeyError,AttributeError):return False

def watch_split(report,benchmark_report):
 if not benchmark_report:raise ValueError('--watch-split requires --benchmark-report; cleanup telemetry alone never proves ACTIVE.')
 started=time.time();folder=Path(r'C:\ProgramData\SplitDeck\Driver\sessions');deadline=time.monotonic()+120
 while time.monotonic()<deadline:
  candidates=sorted(folder.glob('*.json.performance'),key=lambda p:p.stat().st_mtime,reverse=True)
  for file in candidates[:2]:
   if file.stat().st_mtime<started:continue
   journal=Path(str(file)[:-len('.performance')]);before=live_sample(journal)
   if before is None:continue
   time.sleep(1.1);ready=live_sample(journal)
   if ready is None or ready['frames']<=before['frames']:continue
   phase('split-primary',str(report)+'.primary.json',1)
   middle=live_sample(journal)
   if middle is None:return 1
   phase('split-portrait',str(report)+'.portrait.json',2);after=live_sample(journal)
   summary={'journal':str(journal),'helperId':before['helperId'],'liveChecksPassed':bool(after and before['helperId']==middle['helperId']==after['helperId'] and before['frames']<middle['frames']<after['frames']),'primary':json.loads(Path(str(report)+'.primary.json').read_text()),'portrait':json.loads(Path(str(report)+'.portrait.json').read_text()),'nativeDesktopMouseTested':False}
   until=time.monotonic()+60;benchmark=None
   while time.monotonic()<until:
    try:
     candidate=json.loads(Path(benchmark_report).read_text(encoding='utf-8-sig'))
     if candidate.get('HelperId')==summary['helperId']:benchmark=candidate;break
    except (OSError,ValueError):pass
    time.sleep(.25)
   summary['acceptedActiveSplitPass']=accepted_split(summary,benchmark or {});summary['benchmark']=str(benchmark_report)
   Path(report).write_text(json.dumps(summary,indent=2));print(json.dumps({'acceptedActiveSplitPass':summary['acceptedActiveSplitPass']}));return 0 if summary['acceptedActiveSplitPass'] else 1
  time.sleep(.15)
 Path(report).write_text(json.dumps({'outcome':'NotRun','reason':'No new active bounded split was observed before timeout.'}));return 2
if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('--serve',action='store_true');parser.add_argument('--phase');parser.add_argument('--watch-split',action='store_true');parser.add_argument('--report');parser.add_argument('--benchmark-report');args=parser.parse_args()
 if args.serve:serve()
 elif args.watch_split:sys.exit(watch_split(args.report,args.benchmark_report))
 elif args.phase:sys.exit(phase(args.phase,args.report))
