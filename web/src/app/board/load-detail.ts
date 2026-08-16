import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LoadDetail, StatusEventDto } from '../api/models';
import { EdiView } from '../ui/edi-view';
import { militaryTime, nowForInput, shortDate, stamp, weight } from '../format';

export interface AdvanceCommand {
  id: string;
  occurredAt: string | null;
  note: string | null;
}

/**
 * The four documents of a load's life, in the order they happen.
 *
 * `ack` and `invoice` are the two ends. Before them the console showed only the middle of
 * the lifecycle — the tender arriving and the statuses going back — which is the part that
 * is easiest to build and the part a trading partner asks about last.
 */
type Pane = 'tender' | 'ack' | 'status' | 'invoice';

/**
 * Everything about the selected load: the human view above, the wire format below.
 *
 * Both are visible at once on purpose. The point of the board is that a status a dispatcher
 * clicks and a 214 a partner receives are the same event seen from two sides, and putting
 * them on separate screens would let one drift from the other without anyone noticing.
 */
@Component({
  selector: 'app-load-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, EdiView],
  templateUrl: './load-detail.html',
  styleUrl: './load-detail.css',
})
export class LoadDetailPanel {
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly detail = input.required<LoadDetail | null>();
  readonly busy = input(false);

  readonly advance = output<AdvanceCommand>();
  readonly remove = output<string>();

  readonly pane = signal<Pane>('status');
  readonly selectedEventId = signal<string | null>(null);
  readonly occurredAt = signal(nowForInput());
  readonly note = signal('');

  readonly events = computed(() => [...(this.detail()?.events ?? [])].reverse());

  readonly selectedEvent = computed<StatusEventDto | null>(() => {
    const all = this.events();
    if (all.length === 0) {
      return null;
    }

    const chosen = all.find((e) => e.id === this.selectedEventId());
    return chosen ?? all[0];
  });

  readonly acknowledgment = computed(() => this.detail()?.acknowledgment ?? null);

  readonly invoice = computed(() => this.detail()?.invoice ?? null);

  readonly ediText = computed(() => {
    switch (this.pane()) {
      case 'tender':
        return this.detail()?.sourceEdi ?? '';
      case 'ack':
        return this.acknowledgment()?.edi ?? '';
      case 'invoice':
        return this.invoice()?.edi ?? '';
      default:
        return this.selectedEvent()?.edi214 ?? '';
    }
  });

  /**
   * The segment worth pointing at in each document — the one carrying the thing the pane is
   * about. `AK5` is the verdict, `L1` is the money, `AT7` is the status, `S5` is the stop.
   */
  readonly ediHighlight = computed(() => {
    switch (this.pane()) {
      case 'tender':
        return 'S5';
      case 'ack':
        return 'AK5';
      case 'invoice':
        return 'L1';
      default:
        return 'AT7';
    }
  });

  /**
   * What the inbound file declared about itself, read the way a parser reads it: the
   * element separator at ISA offset 3 and the segment terminator at offset 105. Showing
   * it is the point: the pipe-delimited sample reports a pipe and a newline here, and
   * parses to exactly the same load as the conventional one.
   */
  readonly tenderMeta = computed(() => {
    const edi = this.detail()?.sourceEdi ?? '';
    if (edi.length < 106) {
      return '';
    }

    const glyph = (c: string) =>
      c === '\n' ? '\\n' : c === '\r' ? '\\r' : c;
    const terminator = edi[105];
    const segments = edi.split(terminator).filter((s) => s.trim().length > 0).length;

    return `element '${glyph(edi[3])}' · terminator '${glyph(terminator)}' · ${segments} segments`;
  });

  constructor() {
    // A new load resets the panel: the newest status is the interesting one, and the
    // timestamp box goes back to "now" rather than keeping the last load's backdate.
    effect(() => {
      const detail = this.detail();
      this.selectedEventId.set(null);
      this.occurredAt.set(nowForInput());
      this.note.set('');

      // Land on the newest 214 when there is one. Sending a status message and then having
      // to click to see what went out defeats the point of showing both sides at once.
      this.pane.set((detail?.events.length ?? 0) > 0 ? 'status' : 'tender');
    });
  }

  /**
   * Switches the wire pane and brings the matching part of the human view with it.
   *
   * The two halves of this panel are the same event seen from two sides, so moving one and
   * leaving the other behind defeats the point — clicking "210 out" while the body is still
   * showing stops means reading the invoice off the raw segments.
   */
  show(pane: Pane): void {
    this.pane.set(pane);

    const anchor = pane === 'ack' ? '.ack' : pane === 'invoice' ? '.charges' : null;
    if (anchor === null) {
      return;
    }

    // After the pane signal has been applied, so the block being scrolled to exists.
    queueMicrotask(() =>
      (this.host.nativeElement as HTMLElement)
        .querySelector(anchor)
        ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' }),
    );
  }

  send(): void {
    const detail = this.detail();
    if (!detail?.summary.nextStatus) {
      return;
    }

    this.advance.emit({
      id: detail.summary.id,
      occurredAt: this.occurredAt() || null,
      note: this.note().trim() || null,
    });
  }

  date(value: string | null): string {
    return shortDate(value);
  }

  time(value: string | null): string {
    return militaryTime(value);
  }

  when(value: string | null): string {
    return stamp(value);
  }

  lbs(value: number | null): string {
    return weight(value);
  }

  /**
   * Money for reading, not for sending.
   *
   * The wire carries `265375` because L104 and B307 are N2 — an implied two decimal places
   * and no decimal point. This is the only place the two forms are allowed to meet, and the
   * EDI pane underneath shows what actually goes out.
   */
  money(value: number): string {
    return value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }
}
