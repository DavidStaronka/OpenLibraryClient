# OpenLibraryClient

A small full-stack app for searching [Open Library](https://openlibrary.org) with messy,
free-text book descriptions (e.g. `"dune - frank herbert"` or `"Project Hail Mary" by Andy Weir`).

- **Backend** (`src/OpenLibraryClient.Api`): .NET 10 minimal API. Parses the query
  deterministically via regex where possible, falls back to an LLM (Gemini) when the
  deterministic parse is missing, low-confidence, or doesn't validate against real Open Library
  results, then ranks and explains candidates with fuzzy title/author/keyword matching.
- **Frontend** (`frontend/`): React + TypeScript + Vite. Shows cover art, title, author(s),
  first publish year, Open Library links, and a plain-language explanation of each result's
  ranking.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org) 20+ and npm
- (Optional, for LLM fallback) A free [Google AI Studio](https://aistudio.google.com/) Gemini API key
- (Optional, for viewing metrics) [Docker](https://www.docker.com/)

## Running locally

### 1. Backend

```powershell
cd src\OpenLibraryClient.Api
dotnet run
```

The API listens on `http://localhost:5103` by default (see `Properties/launchSettings.json`).

**Gemini API key** (optional but recommended — without it, LLM-fallback extraction/validation
will fail and the app falls back to deterministic-only results):

```powershell
cd src\OpenLibraryClient.Api
dotnet user-secrets set "Gemini:ApiKey" "<your-key>"
```

or set the `GEMINI_API_KEY` environment variable instead. If neither is set, the app still
starts (a warning is logged), and LLM calls simply fail gracefully at request time.

**Rate limiting**: `GET /api/books/search` is rate-limited per client IP (fixed window, 20
requests/60s by default) since each search can trigger a billable Gemini call. Requests beyond
the limit get a `429 Too Many Requests` response. Configurable via the `RateLimiting` section in
`appsettings.json` (`PermitLimit`, `WindowSeconds`, `QueueLimit`).

### 2. Frontend

```powershell
cd frontend
npm install
npm run dev
```

Open the URL Vite prints (typically `http://localhost:5173`). The frontend is preconfigured with
CORS to talk to the backend at `http://localhost:5103`; override this via a `.env` file with
`VITE_API_BASE_URL=http://localhost:<port>` if you run the API elsewhere.

### 3. (Optional) Viewing OpenTelemetry metrics locally

The API emits metrics (ASP.NET Core, HttpClient, .NET runtime, plus custom
`OpenLibraryClient.BookSearch` search-pipeline metrics — request outcomes, duration by extraction
source, LLM fallback reasons, retry depth, and result counts) via OTLP. The easiest way to view
them locally is the standalone [.NET Aspire Dashboard](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard/standalone) container:

```powershell
docker run --rm -it `
  -p 18888:18888 -p 4317:18889 --name aspire-dashboard `
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Then open `http://localhost:18888` (the container prints a login URL with a token on first run —
use that link). With the container running, start (or restart) the API — it exports metrics to
`http://localhost:4317` by default. Metrics appear under the dashboard's **Metrics** tab for the
`OpenLibraryClient.Api` service.

To point the exporter elsewhere (e.g. a different collector), set the standard
`OTEL_EXPORTER_OTLP_ENDPOINT` environment variable before running the API:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
dotnet run
```

## Running tests

```powershell
dotnet test tests\OpenLibraryClient.Tests\OpenLibraryClient.Tests.csproj
```

```powershell
cd frontend
npm test         # vitest run
npm run build    # tsc -b && vite build
npm run lint     # oxlint
```
