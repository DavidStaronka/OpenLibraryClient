import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

// Testing Library's automatic cleanup registration relies on Vitest's `globals` mode; since this
// project doesn't enable globals (to keep `tsc -b` happy without extra ambient type config),
// unmount rendered components after each test explicitly instead.
afterEach(cleanup);
