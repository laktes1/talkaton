import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DelegationPerson, User } from '../../../core/api/models';

/**
 * Делегирование (Этап 7.6): «этот человек может создавать и править встречи в моём
 * календаре» — как секретарь и руководитель. Выдаёт/забирает право, список уже выданных
 * прав виден тут же.
 */
@Component({
  selector: 'app-delegation-manager',
  imports: [FormsModule],
  templateUrl: './delegation-manager.html',
  styleUrl: './delegation-manager.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DelegationManager {
  readonly people = input.required<readonly User[]>();
  readonly myDelegates = input.required<readonly DelegationPerson[]>();
  readonly saving = input(false);
  readonly serverError = input<string | null>(null);

  readonly granted = output<string>();
  readonly revoked = output<string>();
  readonly closed = output<void>();

  protected readonly query = signal('');

  protected readonly delegateIds = computed(() => new Set(this.myDelegates().map((x) => x.userId)));

  protected readonly candidates = computed(() => {
    const chosen = this.delegateIds();
    const query = this.query().trim().toLocaleLowerCase('ru');
    return this.people()
      .filter((person) => !chosen.has(person.id))
      .filter((person) => query.length === 0 || person.displayName.toLocaleLowerCase('ru').includes(query));
  });

  protected initials(name: string): string {
    return name
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part.charAt(0).toUpperCase())
      .join('');
  }
}
