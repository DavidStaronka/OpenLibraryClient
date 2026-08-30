import type { BookSearchResponse } from './types';

// Defaults to the ASP.NET Core "http" launch profile port; override with a .env file
// (VITE_API_BASE_URL=...) if the backend runs elsewhere.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5103';

export async function searchBooks(bookInfo: string, signal?: AbortSignal): Promise<BookSearchResponse> {
  const url = `${API_BASE_URL}/api/books/search?bookInfo=${encodeURIComponent(bookInfo)}`;
  const response = await fetch(url, { signal });

  if (!response.ok) {
    const body = await response.json().catch(() => null);
    const message = body?.error ?? `Search failed with status ${response.status}`;
    throw new Error(message);
  }

  return response.json() as Promise<BookSearchResponse>;
}
