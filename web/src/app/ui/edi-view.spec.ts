import { ComponentFixture, TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { EdiView } from './edi-view';
import { PIPE_EDI, STAR_EDI } from '../testing/fixtures';

/**
 * The viewer reads the delimiters the way a parser does — element separator at ISA offset
 * 3, segment terminator at offset 105 — which is what lets a pipe-delimited file render
 * identically to a conventional one.
 */
describe('EdiView', () => {
  let fixture: ComponentFixture<EdiView>;

  function render(edi: string, highlight: string | null = null): EdiView {
    fixture = TestBed.createComponent(EdiView);
    fixture.componentRef.setInput('edi', edi);
    fixture.componentRef.setInput('highlight', highlight);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  function ids(): string[] {
    return Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.line .id'),
    ).map((el) => el.textContent ?? '');
  }

  beforeEach(() => TestBed.configureTestingModule({ imports: [EdiView] }));

  it('renders one line per segment, numbered from one', () => {
    render(STAR_EDI);

    expect(ids()).toEqual(['ISA', 'GS', 'ST', 'B10', 'LX', 'AT7', 'MS1', 'SE', 'GE', 'IEA']);
    expect(fixture.componentInstance.separator()).toBe('*');
    expect(fixture.componentInstance.terminatorGlyph()).toBe('~');
  });

  it('renders a pipe-delimited, newline-terminated file the same way', () => {
    render(PIPE_EDI);

    expect(ids()).toEqual(['ISA', 'GS', 'ST', 'B2', 'S5', 'N1', 'S5', 'N1', 'SE', 'GE', 'IEA']);
    expect(fixture.componentInstance.separator()).toBe('|');

    // A newline terminator has nothing to print, so it gets a glyph rather than a blank.
    expect(fixture.componentInstance.terminatorGlyph()).toBe('↵');
  });

  it('splits elements on the declared separator, not on a star', () => {
    render(PIPE_EDI);

    const b2 = fixture.componentInstance.segments().find((s) => s.id === 'B2');

    expect(b2?.elements).toEqual(['', 'TEST', '', 'LD10042311', '', 'PP']);
  });

  it('picks out the segment the reader is being pointed at', () => {
    render(STAR_EDI, 'AT7');

    const highlighted = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.line.hl .id'),
    ).map((el) => el.textContent);

    expect(highlighted).toEqual(['AT7']);
  });

  it('says so rather than guessing when there is no ISA to read delimiters out of', () => {
    render('ST*214*4005~');

    expect(ids()).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nothing to show.');
  });
});
