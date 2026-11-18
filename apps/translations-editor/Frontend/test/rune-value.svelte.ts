// A reactive value for tests of rune classes: a `$derived` that reads it through a getter follows its changes, as
// one that reads a page's `$state` does.
export class RuneValue<T> {
  value = $state.raw<T>() as T;

  constructor(value: T) {
    this.value = value;
  }
}
