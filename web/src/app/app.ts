import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BoardService } from './api/board.service';
import { LoadSummary, StatusKey } from './api/models';
import { BoardGrid } from './board/board-grid';
import { AdvanceCommand, LoadDetailPanel } from './board/load-detail';
import { TenderDialog } from './board/tender-dialog';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, BoardGrid, LoadDetailPanel, TenderDialog],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly board = inject(BoardService);

  readonly statusFilter = signal<StatusKey | null>(null);
  readonly query = signal('');
  readonly tenderOpen = signal(false);

  /**
   * Counts per status, for the rail. Computed from the unfiltered list on purpose — a
   * count that changes when you click it tells you nothing.
   */
  readonly counts = computed(() => {
    const counts = new Map<StatusKey, number>();
    for (const load of this.board.loads()) {
      counts.set(load.status, (counts.get(load.status) ?? 0) + 1);
    }
    return counts;
  });

  readonly visible = computed(() => {
    const status = this.statusFilter();
    const needle = this.query().trim().toLowerCase();

    return this.board.loads().filter((load) => {
      if (status !== null && load.status !== status) {
        return false;
      }

      if (needle.length === 0) {
        return true;
      }

      // Searching a dispatch board means searching every number anybody might quote at
      // you, plus the city names, because half of what comes in over the phone is "the
      // Memphis load".
      return [
        load.shipmentId,
        load.billOfLading,
        load.primaryReference,
        load.scac,
        load.trailerNumber,
        load.originName,
        load.originCityState,
        load.destinationName,
        load.destinationCityState,
      ]
        .join(' ')
        .toLowerCase()
        .includes(needle);
    });
  });

  readonly inMotion = computed(
    () => this.board.loads().filter((l) => l.statusOrder > 0 && l.statusOrder < 6).length,
  );

  readonly needingCover = computed(
    () => this.board.loads().filter((l) => l.statusOrder === 0).length,
  );

  readonly messagesSent = computed(() =>
    this.board.loads().reduce((total, load) => total + load.eventCount, 0),
  );

  constructor() {
    void this.board.init();
  }

  toggleStatus(status: StatusKey): void {
    this.statusFilter.update((current) => (current === status ? null : status));
  }

  async advanceFromGrid(load: LoadSummary): Promise<void> {
    if (!load.nextStatus) {
      return;
    }

    await this.board.select(load.id);
    await this.board.advance(load.id, load.nextStatus, null, null);
  }

  async advanceFromDetail(command: AdvanceCommand): Promise<void> {
    const next = this.board.detail()?.summary.nextStatus;
    if (!next) {
      return;
    }

    await this.board.advance(command.id, next, command.occurredAt, command.note);
  }

  async tender(edi: string): Promise<void> {
    const loads = await this.board.tender(edi);
    if (loads && loads.length > 0) {
      this.tenderOpen.set(false);
    }
  }

  async tenderSample(name: string): Promise<void> {
    const loads = await this.board.tenderSample(name);
    if (loads && loads.length > 0) {
      this.tenderOpen.set(false);
    }
  }
}
