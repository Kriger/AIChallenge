#!/usr/bin/env python3
"""Ollama Chat Server with streaming support"""

import json
import os
import sys
import time
import threading
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.request import Request, urlopen
from urllib.error import URLError
from datetime import datetime

OLLAMA_URL = os.environ.get("OLLAMA_URL", "http://176.53.172.57:11434")
MODEL = os.environ.get("MODEL", "llama3.2:3b")
PORT = int(os.environ.get("PORT", "8080"))

# Rate limiting
request_counts = {}
RATE_LIMIT_RPM = 60
RATE_WINDOW = 60  # seconds

def check_rate_limit(client_ip: str) -> bool:
    now = time.time()
    if client_ip not in request_counts:
        request_counts[client_ip] = []
    # Clean old entries
    request_counts[client_ip] = [t for t in request_counts[client_ip] if now - t < RATE_WINDOW]
    if len(request_counts[client_ip]) >= RATE_LIMIT_RPM:
        return False
    request_counts[client_ip].append(now)
    return True


class ChatHandler(BaseHTTPRequestHandler):
    def _get_client_ip(self):
        return self.client_address[0]

    def _send_json(self, status, data):
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_body(self):
        length = int(self.headers.get("Content-Length", 0))
        return self.rfile.read(length)

    def do_GET(self):
        if self.path == "/health":
            self._send_json(200, {
                "status": "ok",
                "ollama": OLLAMA_URL,
                "model": MODEL,
                "timestamp": datetime.now().isoformat()
            })
        elif self.path == "/":
            self._serve_static()
        else:
            self._send_json(404, {"error": "Not found"})

    def do_POST(self):
        if self.path == "/api/chat":
            self._handle_chat()
        elif self.path == "/api/chat/stream":
            self._handle_chat_stream()
        else:
            self._send_json(404, {"error": "Not found"})

    def _handle_chat(self):
        client_ip = self._get_client_ip()

        # Rate limiting
        if not check_rate_limit(client_ip):
            self._send_json(429, {
                "error": "Rate limit exceeded",
                "retry_after": RATE_WINDOW
            })
            return

        try:
            body = json.loads(self._read_body())
            messages = body.get("messages", [])

            if not messages:
                self._send_json(400, {"error": "Messages array is required"})
                return

            # Forward to Ollama
            data = json.dumps({
                "model": MODEL,
                "messages": messages,
                "stream": False
            }).encode("utf-8")

            req = Request(
                f"{OLLAMA_URL}/api/chat",
                data=data,
                headers={"Content-Type": "application/json"}
            )

            with urlopen(req, timeout=120) as resp:
                result = json.loads(resp.read())

            self._send_json(200, result)

        except URLError as e:
            self._send_json(502, {
                "error": "Ollama connection failed",
                "details": str(e.reason)
            })
        except Exception as e:
            self._send_json(500, {
                "error": "Internal error",
                "details": str(e)
            })

    def _handle_chat_stream(self):
        client_ip = self._get_client_ip()

        if not check_rate_limit(client_ip):
            self._send_json(429, {
                "error": "Rate limit exceeded",
                "retry_after": RATE_WINDOW
            })
            return

        try:
            body = json.loads(self._read_body())
            messages = body.get("messages", [])

            if not messages:
                self._send_json(400, {"error": "Messages array is required"})
                return

            data = json.dumps({
                "model": MODEL,
                "messages": messages,
                "stream": True
            }).encode("utf-8")

            req = Request(
                f"{OLLAMA_URL}/api/chat",
                data=data,
                headers={"Content-Type": "application/json"}
            )

            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-cache")
            self.send_header("Connection", "keep-alive")
            self.end_headers()

            with urlopen(req, timeout=120) as resp:
                while True:
                    chunk = resp.read(4096)
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    self.wfile.flush()

            # Send done signal
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()

        except Exception as e:
            error_data = json.dumps({"error": str(e)}).encode()
            self.wfile.write(b"data: " + error_data + b"\n\n")
            self.wfile.flush()

    def _serve_static(self):
        html_path = os.path.join(os.path.dirname(__file__), "public", "index.html")
        if os.path.exists(html_path):
            with open(html_path, "r") as f:
                html = f.read()
            self._send_response_with_type(200, html, "text/html")
        else:
            self._send_json(404, {"error": "index.html not found"})

    def _send_response_with_type(self, status, body, content_type):
        if isinstance(body, str):
            body = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        ip = self._get_client_ip()
        print(f"[{ip}] {format % args}", flush=True)


def run():
    server = HTTPServer(("0.0.0.0", PORT), ChatHandler)
    print(f"\n{'='*60}")
    print(f"  Ollama Chat Server")
    print(f"{'='*60}")
    print(f"  Server:    http://localhost:{PORT}")
    print(f"  Ollama:    {OLLAMA_URL}")
    print(f"  Model:     {MODEL}")
    print(f"  Rate limit: {RATE_LIMIT_RPM} RPM per IP")
    print(f"{'='*60}\n")
    print(f"Press Ctrl+C to stop\n")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down...")
        server.server_close()


if __name__ == "__main__":
    run()
