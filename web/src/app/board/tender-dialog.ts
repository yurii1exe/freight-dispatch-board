import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SampleTender } from '../api/models';

/**
 * Where a 204 gets into the board: paste it, drop a file on it, or take one of the bundled
 * samples.
 *
 * All three exist because all three happen. Partners deliver by AS2 or SFTP as files; a
 * support ticket arrives as text pasted into an email; and a reviewer opening this for the
 * first time has neither.
 */
@Component({
  selector: 'app-tender-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './tender-dialog.html',
  styleUrl: './tender-dialog.css',
})
export class TenderDialog {
  readonly samples = input.required<SampleTender[]>();
  readonly busy = input(false);
  readonly error = input<string | null>(null);

  readonly submit = output<string>();
  readonly pick = output<string>();
  readonly close = output<void>();

  readonly text = signal('');
  readonly dragging = signal(false);

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);

    const file = event.dataTransfer?.files?.[0];
    if (file) {
      void this.readFile(file);
    }
  }

  onFileInput(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      void this.readFile(file);
    }
  }

  send(): void {
    const edi = this.text().trim();
    if (edi.length > 0) {
      this.submit.emit(edi);
    }
  }

  private async readFile(file: File): Promise<void> {
    this.text.set(await file.text());
  }
}
