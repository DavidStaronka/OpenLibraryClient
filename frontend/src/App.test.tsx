import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import App from './App';
import { searchBooks } from './api';
import type { BookSearchResponse } from './types';

// The whole api module is mocked so tests exercise App's own request/abort/error-handling
// logic without making real network calls.
vi.mock('./api', () => ({
  searchBooks: vi.fn(),
}));

const mockedSearchBooks = vi.mocked(searchBooks);

function makeResponse(overrides: Partial<BookSearchResponse> = {}): BookSearchResponse {
  return {
    query: 'dune by frank herbert',
    extraction: {
      title: 'Dune',
      author: 'Frank Herbert',
      keywords: [],
      source: 'Deterministic',
    },
    explanation: 'input clear enough for deterministic match',
    results: [
      {
        key: '/works/OL1',
        title: 'Dune',
        authors: ['Frank Herbert'],
        firstPublishYear: 1965,
        editionCount: 100,
        coverId: 123,
        score: 0.95,
        breakdown: {
          titleSimilarity: 1,
          authorSimilarity: 1,
          keywordOverlap: 0,
          popularityNorm: 1,
        },
        explanation: 'Title is an exact match; author is an exact match.',
      },
    ],
    queriesAttempted: ['Dune Frank Herbert'],
    ...overrides,
  };
}

/** Resolves with `response` after `delayMs`, or rejects with an AbortError if the signal fires first - mirroring `fetch`'s real abort behavior. */
function respondAfter(response: BookSearchResponse, delayMs: number) {
  return (_bookInfo: string, signal?: AbortSignal) =>
    new Promise<BookSearchResponse>((resolve, reject) => {
      const timer = setTimeout(() => resolve(response), delayMs);
      signal?.addEventListener('abort', () => {
        clearTimeout(timer);
        reject(new DOMException('The operation was aborted.', 'AbortError'));
      });
    });
}

async function submitSearch(query: string) {
  const input = screen.getByLabelText('Book info');
  fireEvent.change(input, { target: { value: query } });
  fireEvent.submit(input.closest('form')!);
}

describe('App', () => {
  beforeEach(() => {
    mockedSearchBooks.mockReset();
  });

  it('renders ranked results after a successful search', async () => {
    mockedSearchBooks.mockImplementationOnce(respondAfter(makeResponse(), 0));
    render(<App />);

    await submitSearch('dune by frank herbert');

    await waitFor(() => expect(screen.getByText('Dune')).toBeInTheDocument());
    expect(screen.getByText(/exact match/i)).toBeInTheDocument();
  });

  it('shows an error message when the search fails with a non-abort error', async () => {
    mockedSearchBooks.mockRejectedValueOnce(new Error('Search failed with status 502'));
    render(<App />);

    await submitSearch('dune by frank herbert');

    await waitFor(() => expect(screen.getByText('Search failed with status 502')).toBeInTheDocument());
  });

  it('ignores a superseded (aborted) request and shows only the latest result', async () => {
    const staleResponse = makeResponse({ query: 'stale query' });
    const latestResponse = makeResponse({
      query: 'latest query',
      results: [
        {
          ...makeResponse().results[0],
          key: '/works/OL2',
          title: 'Latest Result',
        },
      ],
    });

    mockedSearchBooks
      .mockImplementationOnce(respondAfter(staleResponse, 50))
      .mockImplementationOnce(respondAfter(latestResponse, 0));

    render(<App />);

    // First submission starts a slow request...
    await submitSearch('first query');
    // ...then a second submission supersedes it before the first resolves.
    await submitSearch('second query');

    await waitFor(() => expect(screen.getByText('Latest Result')).toBeInTheDocument());
    expect(screen.queryByText('Dune')).not.toBeInTheDocument();
    expect(screen.queryByText(/something went wrong/i)).not.toBeInTheDocument();
    expect(mockedSearchBooks).toHaveBeenCalledTimes(2);
  });

  it('falls back to a placeholder when a cover image fails to load', async () => {
    mockedSearchBooks.mockImplementationOnce(respondAfter(makeResponse(), 0));
    render(<App />);

    await submitSearch('dune by frank herbert');

    const cover = await screen.findByAltText('Cover of Dune');
    fireEvent.error(cover);

    await waitFor(() => expect(screen.getByText('No cover')).toBeInTheDocument());
    expect(screen.queryByAltText('Cover of Dune')).not.toBeInTheDocument();
  });
});
