import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { LoadSummary } from '../api/models';
import { isOverdue, militaryTime, shortDate, since, weight, window } from '../format';

/**
 * The board itself: one row per load, everything a dispatcher scans for on one line.
 *
 * Column choice is the whole design. A dispatcher looking at this is answering one of four
 * questions — what needs covering, what is running late, where is that load, and what is
 * the reference number the customer just quoted at me. Everything else belongs in the
 * detail panel.
 */
@Component({
  selector: 'app-board-grid',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './board-grid.html',
  styleUrl: './board-grid.css',
})
export class BoardGrid {
  readonly loads = input.required<LoadSummary[]>();
  readonly selectedId = input<string | null>(null);

  readonly selectLoad = output<string>();
  readonly advanceLoad = output<LoadSummary>();

  readonly rows = computed(() =>
    this.loads().map((load) => ({
      load,
      pickupDate: shortDate(load.originEarliest),
      pickupWindow: window(load.originEarliest, load.originLatest),
      deliveryDate: shortDate(load.destinationEarliest),
      deliveryWindow: window(load.destinationEarliest, load.destinationLatest),
      weight: weight(load.totalWeight),
      age: since(load.receivedAt),
      // A pickup window that closed while the load is still short of "loaded" is the row a
      // dispatcher needs to see first; same for a delivery window on a load that has not
      // been delivered.
      pickupLate: isOverdue(load.originLatest, load.statusOrder >= 3),
      deliveryLate: isOverdue(load.destinationLatest, load.statusOrder >= 6),
      equipment: [load.equipmentLabel, load.equipmentLength ? load.equipmentLength + "'" : '']
        .filter(Boolean)
        .join(' '),
    })),
  );

  time(value: string | null): string {
    return militaryTime(value);
  }
}
