"""Simple mock SMS provider HTTP server.
POST any path -> returns {"status": "success", "id": "..."} on port 9099.
Query param ?fail=1 causes 500.
"""
import json, sys, threading, time
from http.server import BaseHTTPRequestHandler, HTTPServer

REQUESTS = []

class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        length = int(self.headers.get('Content-Length', '0'))
        body = self.rfile.read(length).decode('utf-8', errors='replace') if length else ''
        REQUESTS.append({'path': self.path, 'body': body, 'headers': dict(self.headers)})
        if 'fail=1' in self.path:
            self.send_response(500)
            self.end_headers()
            self.wfile.write(b'{"status":"error"}')
            return
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(json.dumps({"status": "success", "id": f"MOCK-{len(REQUESTS)}"}).encode())
    def do_GET(self):
        self.send_response(200); self.end_headers(); self.wfile.write(b'{"status":"success"}')
    def log_message(self, *a, **k): pass

def run(port=9099):
    srv = HTTPServer(('0.0.0.0', port), Handler)
    print(f"Mock SMS server listening on :{port}", flush=True)
    srv.serve_forever()

if __name__ == '__main__':
    run(int(sys.argv[1]) if len(sys.argv) > 1 else 9099)
