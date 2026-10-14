#!/usr/bin/env python3
"""Local JSON light source simulator. Standard library only; no device access."""
import argparse, json, math, time
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

def load_pattern(path):
    data = json.loads(Path(path).read_text(encoding='utf-8'))
    period = data['periodSeconds']; points = data['points']
    if not isinstance(period, (int, float)) or not math.isfinite(period) or not 1 <= period <= 86400 or not 2 <= len(points) <= 64:
        raise ValueError('Invalid period or point count')
    last = -1
    for p in points:
        x, lux = p['seconds'], p['lux']
        if not all(isinstance(v, (float,int)) and math.isfinite(v) for v in [x,lux]) or not 0 <= x <= period or x <= last or not 0 <= lux <= 200000:
            raise ValueError('Invalid points')
        last = x
    if points[0]['seconds'] != 0 or points[-1]['seconds'] != period or points[0]['lux'] != points[-1]['lux']:
        raise ValueError('Loop endpoints must match')
    return data

def evaluate(data, elapsed):
    x = elapsed % data['periodSeconds']
    for a,b in zip(data['points'], data['points'][1:]):
        if x <= b['seconds']:
            return a['lux'] + (b['lux']-a['lux'])*(x-a['seconds'])/(b['seconds']-a['seconds'])
    return data['points'][-1]['lux']

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pattern', default=str(Path(__file__).with_name('pattern.json')))
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args(); data = load_pattern(args.pattern); started = time.monotonic()
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path != '/light.json': self.send_error(404); return
            sample = {'format':'fluentcontrol.light-sample','version':1,'sourceId':'fc-local-simulator',
                      'timestamp':datetime.now(timezone.utc).isoformat(), 'lux':round(evaluate(data,time.monotonic()-started),3),
                      'valid':True,'simulated':True}
            body = json.dumps(sample).encode('utf-8')
            self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
            self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
        def log_message(self, *_): pass
    print(f'Subscribe in FC: http://127.0.0.1:{args.port}/light.json', flush=True)
    try: ThreadingHTTPServer(('127.0.0.1',args.port), Handler).serve_forever()
    except KeyboardInterrupt: pass
if __name__ == '__main__': main()
