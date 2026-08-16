import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { BoardGrid } from './board-grid';
import { LoadSummary } from '../api/models';
import { loadSummary } from '../testing/fixtures';

/**
 * The grid is the screen a dispatcher actually reads, and every column on it is a decision.
 * These assert the three that are easy to get wrong: which row is late, which stop the
 * truck is on, and what the action button does to the row underneath it.
 */
describe('BoardGrid', () => {
  let fixture: ComponentFixture<BoardGrid>;

  function render(loads: LoadSummary[]): void {
    fixture = TestBed.createComponent(BoardGrid);
    fixture.componentRef.setInput('loads', loads);
    fixture.detectChanges();
  }

  function html(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [BoardGrid] }));

  it('puts one row on the board per load', () => {
    render([
      loadSummary({ id: 'a', shipmentId: 'LD10041872' }),
      loadSummary({ id: 'b', shipmentId: 'LD10042190' }),
    ]);

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.row');

    expect(rows.length).toBe(2);
    expect(html()).toContain('LD10041872');
    expect(html()).toContain('LD10042190');
  });

  it('says which stop the truck is on rather than how many there are', () => {
    // A multi-stop row says 2/4. The count of stops is not the question a dispatcher is
    // asking; which one the truck is at is.
    render([
      loadSummary({
        isMultiStop: true,
        stopCount: 4,
        stopProgress: '2/4',
        currentStopOrdinal: 2,
        currentStopName: 'SIERRA FRESH MARKETS DC 4',
        currentStopCityState: 'RENO, NV',
      }),
    ]);

    const cell = (fixture.nativeElement as HTMLElement).querySelector('.progress');

    expect(cell?.textContent?.trim()).toBe('2/4');
    expect(cell?.getAttribute('title')).toBe('Stop 2 of 4 — SIERRA FRESH MARKETS DC 4, RENO, NV');
  });

  it('flags a window that closed on a load that has not reached the state closing it', () => {
    const past = '2020-01-01T08:00';

    render([
      loadSummary({ originLatest: past, statusOrder: 1 }),
      loadSummary({ id: 'loaded', originLatest: past, statusOrder: 3 }),
    ]);

    const late = (fixture.nativeElement as HTMLElement).querySelectorAll('td.when.late');

    // One row, not two: the second one is already loaded, so its pickup window closing is
    // not a thing anybody needs to chase.
    expect(late.length).toBe(1);
  });

  it('separates thousands in the weight, because a five-digit weight is misread without it', () => {
    render([loadSummary({ totalWeight: 42150 })]);

    expect(html()).toContain('42,150');
  });

  it('emits the advance without also selecting the row the button sits in', () => {
    const advanced: LoadSummary[] = [];
    const selected: string[] = [];

    render([loadSummary({ id: 'a', nextStatus: 'AtConsignee', nextActionLabel: 'At consignee' })]);
    fixture.componentInstance.advanceLoad.subscribe((load) => advanced.push(load));
    fixture.componentInstance.selectLoad.subscribe((id) => selected.push(id));

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.tiny')?.click();

    expect(advanced.map((l) => l.id)).toEqual(['a']);
    expect(selected).toEqual([]);
  });

  it('marks a load whose tender the 997 refused, and keeps the row', () => {
    render([loadSummary({ tenderRejected: true })]);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.row').length).toBe(1);
    expect(html()).toContain('997 R');
  });

  it('says what to do with an empty board instead of showing an empty table', () => {
    render([]);

    expect(html()).toContain('No loads on the board.');
  });
});
