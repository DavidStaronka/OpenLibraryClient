# OpenLibraryClient

A small full-stack app for searching [Open Library](https://openlibrary.org) with messy,
free-text book descriptions (e.g. `"dune - frank herbert"` or `"Project Hail Mary" by Andy Weir`).

- **Backend** (`src/OpenLibraryClient.Api`): .NET 10 minimal API. Parses the query
  deterministically via regex where possible, falls back to an LLM (Gemini) when the
  deterministic parse can't identify a title/author or doesn't validate against real Open Library
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

## Implementation Strategy

An overview of how the backend turns a messy `bookInfo` string into a ranked, explained list of
books, from `GET /api/books/search` down to the response (see `BookSearchService.SearchAsync` for
the orchestration and `Program.cs` for how each stage is wired up):

1. **Extraction (`IBookInfoExtractor` → `BookInfoExtractor`)**: The raw query is first run through
   `DeterministicParser`, which tries a fixed set of unambiguous regex separators
   (`"Title" by Author`, `Title by Author`, `Title (Author)`, `Title - Author`) and, failing that,
   treats the whole string as an unstructured keyword bag. There's no partial-confidence middle
   ground: if the deterministic parser recognized one of those separators, its title/author is
   trusted outright and used as-is - no LLM call, no extra latency/cost. Otherwise (no
   recognizable separator at all), the query is handed to `LlmBookInfoExtractor`, which asks
   Gemini (via `IChatClient`) for structured JSON (title/author/keywords/explanation), with
   retry/timeout resilience and a graceful empty-result degradation if the LLM call ultimately
   fails or isn't configured.

2. **Query construction (`OpenLibraryQueryBuilder`)**: The extraction's structured fields are
   turned into an Open Library search string - title+author if both are present, title-only if
   just that, otherwise the extracted keywords, falling back to the raw query text as a last
   resort.

3. **Query + progressively relaxed retry chain (`BookSearchService`)**: `SearchAsync` queries Open
   Library with the full title+author query first. If that returns zero results, it retries with
   just the title. For a **deterministic** extraction specifically, the top-ranked candidate must
   then be validated - an exact title *and* author match against real Open Library data - since a
   regex split can look right but still be wrong (e.g. "dune - frank h"). An unvalidated or
   zero-result deterministic attempt triggers an LLM "second opinion": the query is re-extracted
   with Gemini and re-queried, and its results are preferred whenever it finds anything. If
   everything so far still has zero candidates, the chain relaxes further to a keywords-only query
   and finally the raw, unprocessed query string (letting Open Library's own tokenization have a
   shot). Each distinct query string is only attempted once per request, and the chain stops as
   soon as an attempt returns at least one candidate.

4. **Ranking (`IRelevanceRanker` → `RelevanceRanker`)**: Whatever candidates were found are scored
   with a weighted blend of title similarity, author similarity, keyword-to-subject overlap, and
   (log-scaled, normalized) popularity. The weighting differs by extraction source: deterministic
   extractions trust title/author similarity more heavily, while LLM-sourced extractions (a guess
   to begin with) lean more on keyword overlap and popularity as corroborating signal. Similarity
   itself comes from `SimilarityScorer` (FuzzySharp's token-sort ratio), which tolerates word
   reordering and minor misspellings/omissions.

5. **Explanation (`IRankingExplainer` → `RankingExplainer`)**: For each ranked candidate, a
   rule-based (non-LLM) explainer picks out whichever scored signals clear a "worth mentioning"
   threshold - exact/close title or author match, matched keywords, most-popular-edition-in-results
   - and composes them into a short, human-readable sentence, most significant signal first.

6. **Response assembly (`BookSearchEndpoint`)**: The final extraction summary, per-candidate scores/
   breakdowns/explanations, and the list of Open Library queries actually attempted are serialized
   into `BookSearchResponse` and returned as JSON; pipeline failures are translated into a clean
   `502 Bad Gateway` rather than leaking exception details.

Throughout, `BookSearchMetrics` records outcome/duration/extraction-source/LLM-fallback-reason/
retry-depth/result-count metrics (see [Viewing OpenTelemetry metrics locally](#3-optional-viewing-opentelemetry-metrics-locally)
above), and `CachingOpenLibraryClient`/`CachingLlmBookInfoExtractor` cache Open Library and Gemini
results in-process to avoid redundant outbound calls for repeated or popular queries.

## Notes

### Priorities

When in doubt about what to implement, I focused on resiliency over features. Features can always
be added later, and having the metrics and resiliency from the beginning makes it easier to keep
up with as you add more of them. To that end, I added OTel metrics, retry logic, caching, and
tests first. In a real production app, I'd make a few changes to these:

- Upgrade the caching scheme to something like redis, but as a proof-of-concept, an in memory
  cache will get the job done.
- Add alerts connected to the OTel metrics through some service like grafana, so that we don't
  have to find out everything's going sideways by checking it manually
- Circuit breaker trips are currently just a blind value, I'd scale it to actual production data
  when that exists

### Testing Strategy

I erred on the side of overtesting rather than undertesting, pretty much every item got its test
suite (happy paths, common errors, standard unit testing work) as soon as I added it. This came
in handy when I removed confidence scoring late in my time (explained in the next note) and the 
tests pointed out every single spot that change broke.

### Deterministic/LLM split

I initially attempted to handle much more of the parsing deterministically, but kept handing more
of it off to the llm as time wound on and I kept finding bunk results when the title or author were
off even a little, I eventually required a title and author match to not go through the llm at some 
point, as a result, I tailored the OTel metrics around determining how much the (now quite limited)
deterministic approach actually saves in terms of calls to the gemini API. This also led to the
removal of an (at that point) unused confidence scoring scheme in favor of a binary
deterministic/llm switch, there's at least one or two artifacts of this that I'd trim given more
time, but I decided to focus on resilience rather than code cleanliness at the end of my time.

### Prompt Engineering

The prompt could use some more love and attention, I chose to focus on resilience and production 
considerations over prompt work, but given infinite time, I would likely work on this next. 

### Parsing explanation on the frontend

I originally added the explanation to the frontend (under the searchbar, after searching) just for 
testing, but I ended up liking it, so I left it in the final version

## Thank you for your consideration
