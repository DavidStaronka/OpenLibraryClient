import { useRef, useState, type FormEvent } from 'react';
import { searchBooks } from './api';
import type { BookSearchResponse, RankedResultDto } from './types';
import './App.css';

const OPEN_LIBRARY_BASE_URL = 'https://openlibrary.org';
const OPEN_LIBRARY_COVERS_BASE_URL = 'https://covers.openlibrary.org/b/id';

/** Open Library's own page for this work/edition, e.g. https://openlibrary.org/works/OL27448W. */
function openLibraryUrl(key: string): string {
  return `${OPEN_LIBRARY_BASE_URL}${key}`;
}

/**
 * Cover image URL per the Open Library Covers API (https://openlibrary.org/dev/docs/api/covers):
 * `https://covers.openlibrary.org/b/id/{coverId}-{size}.jpg`. `?default=false` requests a 404
 * instead of a blank placeholder image when no cover exists, so we can detect and hide it.
 */
function coverImageUrl(coverId: number, size: 'S' | 'M' | 'L' = 'M'): string {
  return `${OPEN_LIBRARY_COVERS_BASE_URL}/${coverId}-${size}.jpg?default=false`;
}

function BookCover({ coverId, title }: { coverId: number | null; title: string }) {
  const [failed, setFailed] = useState(false);

  if (coverId === null || failed) {
    return (
      <div className="book-cover book-cover--placeholder" aria-hidden="true">
        No cover
      </div>
    );
  }

  return (
    <img
      className="book-cover"
      src={coverImageUrl(coverId, 'S')}
      alt={`Cover of ${title}`}
      loading="lazy"
      onError={() => setFailed(true)}
    />
  );
}

function BookListItem({ book }: { book: RankedResultDto }) {
  const olUrl = openLibraryUrl(book.key);

  return (
    <li className="book-item">
      <BookCover coverId={book.coverId} title={book.title} />

      <div className="book-details">
        <a className="book-title" href={olUrl} target="_blank" rel="noreferrer">
          {book.title}
        </a>

        {book.authors.length > 0 && (
          <p className="book-authors">by {book.authors.join(', ')}</p>
        )}

        <p className="book-meta">
          {book.firstPublishYear && <span className="book-year">First published {book.firstPublishYear}</span>}
          <span className="book-identifier">
            {book.firstPublishYear ? ' · ' : ''}
            Open Library:{' '}
            <a href={olUrl} target="_blank" rel="noreferrer">
              {book.key}
            </a>
          </span>
        </p>

        <p className="book-rank-explanation">{book.explanation}</p>
      </div>
    </li>
  );
}

function SearchInstructions() {
  return (
    <section className="instructions">
      <p>
        Enter whatever you know about a book. It's parsed instantly if it follows one of these
        common patterns:
      </p>
      <ul>
        <li>
          <code>"Title" by Author</code> — e.g. <em>"Dune" by Frank Herbert</em>
        </li>
        <li>
          <code>Title by Author</code> — e.g. <em>Dune by Frank Herbert</em>
        </li>
        <li>
          <code>Title (Author)</code> — e.g. <em>Dune (Frank Herbert)</em>
        </li>
        <li>
          <code>Title - Author</code> — e.g. <em>Dune - Frank Herbert</em>
        </li>
      </ul>
      <p>
        Anything else — a partial title, a misremembered author, or just a vague description like
        "that sci-fi book about a desert planet with sandworms" — still works. It just takes an
        extra moment while an AI assistant helps make sense of it.
      </p>
    </section>
  );
}

function App() {
  const [bookInfo, setBookInfo] = useState('');
  const [result, setResult] = useState<BookSearchResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  // Tracks the in-flight request so a slow, stale search can't overwrite the result of a
  // newer one when the user re-submits before the first request finishes.
  const activeRequestRef = useRef<AbortController | null>(null);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    const trimmed = bookInfo.trim();
    if (!trimmed) {
      return;
    }

    activeRequestRef.current?.abort();
    const controller = new AbortController();
    activeRequestRef.current = controller;

    setIsLoading(true);
    setError(null);

    try {
      const response = await searchBooks(trimmed, controller.signal);
      setResult(response);
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        // Superseded by a newer search; ignore rather than surfacing an error.
        return;
      }
      setResult(null);
      setError(err instanceof Error ? err.message : 'Something went wrong.');
    } finally {
      if (activeRequestRef.current === controller) {
        setIsLoading(false);
      }
    }
  }

  return (
    <main className="app">
      <h1>Book Search</h1>

      <form onSubmit={handleSubmit} className="search-form">
        <input
          type="text"
          value={bookInfo}
          onChange={(event) => setBookInfo(event.target.value)}
          placeholder="e.g. dune by frnk herbert, sandworms desert planet"
          aria-label="Book info"
        />
        <button type="submit" disabled={isLoading}>
          {isLoading ? 'Searching...' : 'Search'}
        </button>
      </form>

      {error && <p className="error">{error}</p>}

      {result ? (
        <section className="results">
          {result.explanation && <p className="explanation">{result.explanation}</p>}

          {result.results.length === 0 ? (
            <p>No results found.</p>
          ) : (
            <ol className="book-list">
              {result.results.map((book) => (
                <BookListItem key={book.key} book={book} />
              ))}
            </ol>
          )}
        </section>
      ) : (
        <SearchInstructions />
      )}
    </main>
  );
}

export default App;
