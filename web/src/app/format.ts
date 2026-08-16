/**
 * Formatting for a dispatch grid.
 *
 * Times on a load tender have no time zone. X12 does not carry one and element 623 says
 * what the sender meant — `LT` is local time at the stop. So these are parsed as wall clock
 * values and never converted; treating them as UTC would move a 07:00 Pacific appointment
 * to midnight and nobody would notice until a truck was late.
 */

const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** Parses an unzoned ISO string as a wall clock value. */
export function wallClock(value: string | null | undefined): Date | null {
  if (!value) {
    return null;
  }

  const match = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})/.exec(value);
  if (!match) {
    return null;
  }

  return new Date(
    Number(match[1]),
    Number(match[2]) - 1,
    Number(match[3]),
    Number(match[4]),
    Number(match[5]),
  );
}

/** `Tue 18 Aug` — the line a dispatcher scans down. */
export function shortDate(value: string | null | undefined): string {
  const date = wallClock(value);
  return date ? `${DAYS[date.getDay()]} ${date.getDate()} ${MONTHS[date.getMonth()]}` : '';
}

/** `0700` — four digits, the way an appointment is written on a rate confirmation. */
export function militaryTime(value: string | null | undefined): string {
  const date = wallClock(value);
  if (!date) {
    return '';
  }

  return `${String(date.getHours()).padStart(2, '0')}${String(date.getMinutes()).padStart(2, '0')}`;
}

/** `0700-1200`, or `0700+` when the tender left the window open at one end. */
export function window(earliest: string | null, latest: string | null): string {
  const open = militaryTime(earliest);
  const close = militaryTime(latest);

  if (open && close) {
    return open === close ? open : `${open}-${close}`;
  }

  if (open) {
    return `${open}+`;
  }

  return close ? `by ${close}` : '';
}

/** `Tue 18 Aug 1410` — for a status event that has already happened. */
export function stamp(value: string | null | undefined): string {
  const date = shortDate(value);
  return date ? `${date} ${militaryTime(value)}` : '';
}

/** `42,150` — thousands separated, because a five-digit weight is misread without it. */
export function weight(value: number | null | undefined): string {
  return value === null || value === undefined ? '' : Math.round(value).toLocaleString('en-US');
}

/** `3h ago`, `2d ago` — relative, for the tender timestamp column. */
export function since(value: string | null | undefined): string {
  if (!value) {
    return '';
  }

  const then = Date.parse(value);
  if (Number.isNaN(then)) {
    return '';
  }

  const minutes = Math.max(0, Math.round((Date.now() - then) / 60000));
  if (minutes < 60) {
    return `${minutes}m`;
  }

  const hours = Math.round(minutes / 60);
  return hours < 48 ? `${hours}h` : `${Math.round(hours / 24)}d`;
}

/**
 * True when a window has closed and the load has not reached the state that would have
 * closed it. This is the flag a dispatcher is actually scanning for — the row that needed
 * something done an hour ago.
 */
export function isOverdue(latest: string | null, done: boolean): boolean {
  if (done) {
    return false;
  }

  const date = wallClock(latest);
  return date !== null && date.getTime() < Date.now();
}

/** A local datetime-local input value for "now", used to default the status timestamp. */
export function nowForInput(): string {
  const now = new Date();
  const pad = (n: number) => String(n).padStart(2, '0');

  return (
    `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}` +
    `T${pad(now.getHours())}:${pad(now.getMinutes())}`
  );
}
