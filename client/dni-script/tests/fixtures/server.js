// Minimal static file server so Playwright tests can load fixture HTML pages that
// import the real client/dni-script source via <script type="module">, matching how a
// deploying customer's site would load it — no bundler, no mocking of the module system.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..", ".."); // client/dni-script

const MIME_TYPES = {
  ".html": "text/html",
  ".js": "text/javascript",
};

const server = http.createServer((req, res) => {
  const urlPath = decodeURIComponent(req.url.split("?")[0]);
  const filePath = path.join(root, urlPath);

  fs.readFile(filePath, (err, data) => {
    if (err) {
      res.writeHead(404);
      res.end("Not found");
      return;
    }
    const ext = path.extname(filePath);
    res.writeHead(200, { "Content-Type": MIME_TYPES[ext] || "application/octet-stream" });
    res.end(data);
  });
});

server.listen(4173, () => {
  // eslint-disable-next-line no-console
  console.log("fixture server listening on http://localhost:4173");
});
