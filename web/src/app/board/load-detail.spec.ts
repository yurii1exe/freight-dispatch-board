import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AdvanceCommand, LoadDetailPanel } from './load-detail';
import { LoadDetail } from '../api/models';
import {
  acknowledgment,
  invoice,
  loadDetail,
  loadSummary,
  statusEvent,
  PIPE_EDI,
} from '../testing/fixtures';

/**
 * The panel shows a load two ways at once — the human view above, the wire underneath —
 * and the pair only means anything if the two halves stay pointed at the same document.
 * These assert that they do.
 */
describe('LoadDetailPanel', () => {
  let fixture: ComponentFixture<LoadDetailPanel>;

  function render(detail: LoadDetail | null): LoadDetailPanel {
    fixture = TestBed.createComponent(LoadDetailPanel);
    fixture.componentRef.setInput('detail', detail);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function tab(label: string): HTMLButtonElement {
    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button.tab'),
    );

    const found = buttons.find((b) => (b.textContent ?? '').includes(label));
    expect(found, `no tab for ${label}`).toBeDefined();
    return found!;
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [LoadDetailPanel] }));

  it('lists the status messages newest first and lands on the newest 214', () => {
    const older = statusEvent({ id: 'a', statusCode: 'CP', edi214: 'CP FILE' });
    const newest = statusEvent({ id: 'b', statusCode: 'AF', edi214: 'AF FILE' });

    const panel = render(loadDetail({ events: [older, newest] }));

    expect(panel.events().map((e) => e.id)).toEqual(['b', 'a']);
    expect(panel.pane()).toBe('status');
    expect(panel.selectedEvent()?.id).toBe('b');
    expect(panel.ediText()).toBe('AF FILE');
  });

  it('shows the tender when nothing has been sent yet', () => {
    const panel = render(loadDetail({ events: [], sourceEdi: 'THE 204' }));

    expect(panel.pane()).toBe('tender');
    expect(panel.ediText()).toBe('THE 204');
    expect(tab('214 out').disabled).toBe(true);
  });

  it('points the wire pane at the document the tab names, and at the segment that matters', () => {
    // AK5 is the verdict, AT7 is the status, L1 is the money, S5 is the stop. Landing on
    // the right file with the wrong segment picked out is half an answer.
    const panel = render(
      loadDetail({
        sourceEdi: 'THE 204',
        acknowledgment: acknowledgment({ edi: 'THE 997' }),
        invoice: invoice({ edi: 'THE 210' }),
        events: [statusEvent({ edi214: 'THE 214' })],
      }),
    );

    const cases: [string, string, string][] = [
      ['204 in', 'THE 204', 'S5'],
      ['997 out', 'THE 997', 'AK5'],
      ['214 out', 'THE 214', 'AT7'],
      ['210 out', 'THE 210', 'L1'],
    ];

    for (const [label, edi, highlight] of cases) {
      tab(label).click();
      fixture.detectChanges();

      expect(panel.ediText(), label).toBe(edi);
      expect(panel.ediHighlight(), label).toBe(highlight);
    }
  });

  it('disables the 210 tab until the freight has moved', () => {
    render(loadDetail({ invoice: null }));

    const button = tab('210 out');

    expect(button.disabled).toBe(true);
    expect(button.getAttribute('title')).toContain('Raised on delivery');
  });

  it('reads the inbound delimiters out of the ISA rather than assuming them', () => {
    // The pipe-delimited sample declares '|' and a newline, both legal and both fatal to a
    // reader that starts with text.split('~').
    const panel = render(loadDetail({ events: [], sourceEdi: PIPE_EDI }));

    expect(panel.tenderMeta()).toBe("element '|' · terminator '\\n' · 11 segments");
  });

  it('emits the advance with the timestamp and note the dispatcher typed', () => {
    const sent: AdvanceCommand[] = [];
    const panel = render(loadDetail({ summary: loadSummary({ id: 'load-1' }) }));
    panel.advance.subscribe((command) => sent.push(command));

    panel.occurredAt.set('2026-08-18T07:15');
    panel.note.set('  driver called from the dock  ');
    panel.send();

    expect(sent).toEqual([
      { id: 'load-1', occurredAt: '2026-08-18T07:15', note: 'driver called from the dock' },
    ]);
  });

  it('sends nothing once the load is delivered', () => {
    const sent: AdvanceCommand[] = [];
    const panel = render(
      loadDetail({
        summary: loadSummary({ status: 'Delivered', nextStatus: null, nextActionLabel: '' }),
      }),
    );
    panel.advance.subscribe((command) => sent.push(command));

    panel.send();

    expect(sent).toEqual([]);
  });

  it('asks for a load when there is none selected', () => {
    render(null);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Select a load.');
  });
});
