from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path in ("/healthz", "/alive"):
            self.send_json({"status": "ok"})
            return

        if self.path == "/configuration":
            self.send_json({
                "message": os.environ.get("Sample__Message", ""),
                "hasApiKey": bool(os.environ.get("Sample__ApiKey")),
                "resourceId": os.environ.get("CLOUDSHELL_RESOURCE_ID", ""),
                "resourceName": os.environ.get("CLOUDSHELL_RESOURCE_NAME", "")
            })
            return

        self.send_json({
            "name": "python-api",
            "message": os.environ.get("Sample__Message", "Hello from Python"),
            "configuration": "/configuration",
            "health": "/healthz"
        })

    def send_json(self, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        print(format % args, flush=True)


def main():
    port = int(os.environ.get("PORT", "5188"))
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print(f"Python sample listening on http://127.0.0.1:{port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
