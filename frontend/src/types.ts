/** Mirrors OpenLibraryClient.Api.Endpoints.BookSearchResponse (camelCase JSON from ASP.NET Core). */
export interface BookSearchResponse {
  query: string;
  extraction: ExtractionSummary;
  explanation: string | null;
  results: RankedResultDto[];
  queriesAttempted: string[];
}

export interface ExtractionSummary {
  title: string | null;
  author: string | null;
  keywords: string[];
  source: string;
}

export interface RankedResultDto {
  key: string;
  title: string;
  authors: string[];
  firstPublishYear: number | null;
  editionCount: number;
  coverId: number | null;
  score: number;
  breakdown: ScoreBreakdown;

  /** Deterministically-generated explanation of why this candidate ranked where it did. */
  explanation: string;
}

/** Mirrors OpenLibraryClient.Core.Models.ScoreBreakdown. */
export interface ScoreBreakdown {
  titleSimilarity: number;
  authorSimilarity: number;
  keywordOverlap: number;
  popularityNorm: number;
}
