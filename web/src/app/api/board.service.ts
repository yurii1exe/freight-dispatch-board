import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  LoadDetail,
  LoadSummary,
  SampleTender,
  StatusEventDto,
  StatusKey,
  StatusOption,
  TenderResult,
} from './models';

/**
 * All board state, in one place.
 *
 * The grid and the detail panel read the same signals; nothing is passed down more than one
 * level and nothing is duplicated. Loads are refetched after every mutation rather than
 * patched in place, because the server owns the status machine and a client that guesses at
 * the next state is a client that will eventually disagree with the 214 that was sent.
 */
@Injectable({ providedIn: 'root' })
export class BoardService {
  private readonly http = inject(HttpClient);

  readonly loads = signal<LoadSummary[]>([]);
  readonly statuses = signal<StatusOption[]>([]);
  readonly samples = signal<SampleTender[]>([]);
  readonly detail = signal<LoadDetail | null>(null);
  readonly selectedId = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly lastTender = signal<TenderResult | null>(null);

  async init(): Promise<void> {
    const [statuses, samples] = await Promise.all([
      firstValueFrom(this.http.get<StatusOption[]>('/api/statuses')),
      firstValueFrom(this.http.get<SampleTender[]>('/api/samples')),
    ]);

    this.statuses.set(statuses);
    this.samples.set(samples);
    await this.refresh();

    const first = this.loads()[0];
    if (first) {
      await this.select(first.id);
    }
  }

  async refresh(): Promise<void> {
    this.loads.set(await firstValueFrom(this.http.get<LoadSummary[]>('/api/loads')));
  }

  async select(id: string | null): Promise<void> {
    this.selectedId.set(id);

    if (id === null) {
      this.detail.set(null);
      return;
    }

    this.detail.set(await firstValueFrom(this.http.get<LoadDetail>(`/api/loads/${id}`)));
  }

  /** Ingests a pasted or uploaded interchange. Returns the loads it produced. */
  async tender(edi: string): Promise<LoadSummary[] | null> {
    return this.run(async () => {
      const result = await firstValueFrom(
        this.http.post<TenderResult>('/api/loads/tender', edi, {
          headers: { 'Content-Type': 'text/plain' },
        }),
      );

      this.lastTender.set(result);
      await this.refresh();
      if (result.loads.length > 0) {
        await this.select(result.loads[0].id);
      }

      return result.loads;
    });
  }

  async tenderSample(name: string): Promise<LoadSummary[] | null> {
    return this.run(async () => {
      const result = await firstValueFrom(
        this.http.post<TenderResult>(`/api/samples/${name}/tender`, null),
      );

      this.lastTender.set(result);
      await this.refresh();
      if (result.loads.length > 0) {
        await this.select(result.loads[0].id);
      }

      return result.loads;
    });
  }

  /**
   * Moves a load to its next status.
   *
   * Returns a list because leaving an intermediate stop is two status messages — the work
   * finishing and the truck departing. One click, two 214s.
   */
  async advance(
    id: string,
    status: StatusKey,
    occurredAt: string | null,
    note: string | null,
  ): Promise<StatusEventDto[] | null> {
    return this.run(async () => {
      const events = await firstValueFrom(
        this.http.post<StatusEventDto[]>(`/api/loads/${id}/status`, {
          status,
          occurredAt,
          reasonCode: 'NS',
          city: null,
          state: null,
          note,
        }),
      );

      await this.refresh();
      await this.select(id);
      return events;
    });
  }

  async remove(id: string): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.http.delete(`/api/loads/${id}`));
      await this.refresh();

      if (this.selectedId() === id) {
        await this.select(this.loads()[0]?.id ?? null);
      }

      return null;
    });
  }

  async reset(): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.http.post<LoadSummary[]>('/api/board/reset', null));
      await this.refresh();
      await this.select(this.loads()[0]?.id ?? null);
      return null;
    });
  }

  async clear(): Promise<void> {
    await this.run(async () => {
      await firstValueFrom(this.http.delete('/api/board'));
      await this.refresh();
      await this.select(null);
      return null;
    });
  }

  /**
   * Runs a mutation with the busy flag set and turns a failure into a readable message.
   *
   * The API returns RFC 7807 problem details, and the useful part is `detail` — "SE01
   * declares 21 segments, transaction set 204 0001 contains 22" rather than "400 Bad
   * Request". Throwing that away is throwing away the only thing the user can act on.
   */
  private async run<T>(work: () => Promise<T>): Promise<T | null> {
    this.busy.set(true);
    this.error.set(null);

    try {
      return await work();
    } catch (failure) {
      this.error.set(describe(failure));
      return null;
    } finally {
      this.busy.set(false);
    }
  }
}

function describe(failure: unknown): string {
  if (failure instanceof HttpErrorResponse) {
    const problem = failure.error as { title?: string; detail?: string } | string | null;

    if (problem && typeof problem === 'object' && problem.detail) {
      return problem.title ? `${problem.title} — ${problem.detail}` : problem.detail;
    }

    if (typeof problem === 'string' && problem.length > 0) {
      return problem;
    }

    return failure.status === 0
      ? 'The API is not reachable. Is FreightDispatch.Api running?'
      : `${failure.status} ${failure.statusText}`;
  }

  return failure instanceof Error ? failure.message : String(failure);
}
