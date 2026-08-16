import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LoadDetail, StatusEventDto } from '../api/models';
import { EdiView } from '../ui/edi-view';
import { militaryTime, nowForInput, shortDate, stamp, weight } from '../format';

export interface AdvanceCommand {
  id: string;
  occurredAt: string | null;
  note: string | null;
}

type Pane = 'tender' | 'status';

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

  readonly ediText = computed(() => {
    if (this.pane() === 'tender') {
      return this.detail()?.sourceEdi ?? '';
    }

    return this.selectedEvent()?.edi214 ?? '';
  });

  readonly ediHighlight = computed(() => (this.pane() === 'status' ? 'AT7' : 'S5'));

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
}
