import { describe, expect, it } from 'vitest';
import { isOverdue, militaryTime, shortDate, stamp, wallClock, weight, window } from './format';

/**
 * X12 carries no time zone, and element 623 `LT` says the sender meant local time at the
 * stop. Everything here follows from that: the strings are wall clock values and nothing
 * converts them.
 */
describe('format', () => {
  it('parses an unzoned timestamp as a wall clock value', () => {
    const date = wallClock('2026-08-18T07:00');

    expect(date?.getHours()).toBe(7);
    expect(date?.getDate()).toBe(18);
    expect(date?.getMonth()).toBe(7);
  });

  it('shows the same clock time whatever the machine time zone is', () => {
    // Treating a 0700 appointment as UTC moves it, and nobody notices until a truck is late.
    expect(militaryTime('2026-08-18T07:00')).toBe('0700');
    expect(shortDate('2026-08-18T07:00')).toBe('Tue 18 Aug');
    expect(stamp('2026-08-18T07:00')).toBe('Tue 18 Aug 0700');
  });

  it('writes a window as a pair, and an open-ended one as it was tendered', () => {
    // A single G62 with no partner is an open-ended request, not a defect, and inventing
    // the missing end would be inventing an appointment nobody agreed to.
    expect(window('2026-08-18T07:00', '2026-08-18T12:00')).toBe('0700-1200');
    expect(window('2026-08-18T07:00', null)).toBe('0700+');
    expect(window(null, '2026-08-18T12:00')).toBe('by 1200');
    expect(window('2026-08-18T07:00', '2026-08-18T07:00')).toBe('0700');
    expect(window(null, null)).toBe('');
  });

  it('is overdue only while there is still something to do about it', () => {
    expect(isOverdue('2020-01-01T08:00', false)).toBe(true);
    expect(isOverdue('2020-01-01T08:00', true)).toBe(false);
    expect(isOverdue('2999-01-01T08:00', false)).toBe(false);
    expect(isOverdue(null, false)).toBe(false);
  });

  it('separates thousands in a weight and leaves a missing one blank', () => {
    expect(weight(42150)).toBe('42,150');
    expect(weight(null)).toBe('');
  });

  it('returns nothing for a value it cannot read, rather than a wrong date', () => {
    expect(wallClock('')).toBeNull();
    expect(wallClock('not a date')).toBeNull();
    expect(militaryTime(undefined)).toBe('');
    expect(shortDate(null)).toBe('');
  });
});
