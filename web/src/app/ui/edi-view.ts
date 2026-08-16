import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

interface RenderedSegment {
  position: number;
  id: string;
  elements: string[];
  highlighted: boolean;
}

/**
 * Renders an interchange one segment per line, with the segment identifiers picked out.
 *
 * The delimiters are read out of the ISA rather than assumed, exactly as the parser does:
 * offset 3 is the element separator and offset 105 is the segment terminator. That is what
 * lets this component render a pipe-delimited, newline-terminated file identically to a
 * conventional one, which is the whole point of the sample that uses them.
 */
@Component({
  selector: 'app-edi-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="edi">
      @if (segments().length === 0) {
        <div class="empty">Nothing to show.</div>
      } @else {
        @for (segment of segments(); track segment.position) {
          <div class="line" [class.hl]="segment.highlighted">
            <span class="no">{{ segment.position.toString().padStart(2, '0') }}</span>
            <span class="id">{{ segment.id }}</span>
            @for (element of segment.elements; track $index) {
              <span class="sep">{{ separator() }}</span><span class="el">{{ element }}</span>
            }
            <span class="sep">{{ terminatorGlyph() }}</span>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .edi {
      font-family: var(--mono);
      font-size: 11px;
      line-height: 1.55;
      white-space: pre;
      overflow: auto;
      padding: 6px 8px;
      background: #080b0f;
      border: 1px solid var(--line);
      border-radius: var(--radius);
      height: 100%;
    }

    .empty {
      color: var(--muted-2);
      font-style: italic;
    }

    .line {
      padding: 0 3px;
      border-radius: 2px;
    }

    .line.hl {
      background: rgba(88, 166, 255, 0.11);
      box-shadow: inset 2px 0 0 var(--accent);
    }

    .no {
      color: #33414f;
      margin-right: 9px;
      user-select: none;
    }

    .id {
      color: #7cc4ff;
      font-weight: 600;
    }

    .sep {
      color: #3d4c5c;
    }

    .el {
      color: #b9c7d6;
    }

    .line.hl .el {
      color: #e6eef7;
    }
  `,
})
export class EdiView {
  /** The raw interchange text. */
  readonly edi = input.required<string>();

  /** Segment identifier to pick out, e.g. `AT7` — the one the reader is being pointed at. */
  readonly highlight = input<string | null>(null);

  private readonly parsed = computed(() => parse(this.edi()));

  readonly separator = computed(() => this.parsed().element);

  readonly terminatorGlyph = computed(() => {
    const terminator = this.parsed().terminator;
    return terminator === '\n' ? '↵' : terminator;
  });

  readonly segments = computed(() => {
    const target = this.highlight();

    return this.parsed().segments.map((segment) => ({
      ...segment,
      highlighted: target !== null && segment.id === target,
    }));
  });
}

function parse(text: string): {
  element: string;
  terminator: string;
  segments: RenderedSegment[];
} {
  const trimmed = text.replace(/^\uFEFF/, '').replace(/^\s+/, '');

  // 106 characters is the length of a compliant ISA including its terminator. Below that
  // there is nothing to read the delimiters out of.
  if (trimmed.length < 106) {
    return { element: '*', terminator: '~', segments: [] };
  }

  const element = trimmed[3];
  const terminator = trimmed[105];

  const segments = trimmed
    .split(terminator)
    .map((raw) => raw.replace(/[\r\n\t ]+$/, '').replace(/^[\r\n]+/, ''))
    .filter((raw) => raw.length > 0)
    .map((raw, index) => {
      const parts = raw.split(element);
      return {
        position: index + 1,
        id: parts[0],
        elements: parts.slice(1),
        highlighted: false,
      };
    });

  return { element, terminator, segments };
}
