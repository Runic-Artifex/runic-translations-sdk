export type FileChangeType = 1 | 2 | 3;

export interface DisposableLike { dispose(): void }

export interface WatcherLike<Uri> {
  onDidCreate(listener: (uri: Uri) => void): DisposableLike;
  onDidChange(listener: (uri: Uri) => void): DisposableLike;
  onDidDelete(listener: (uri: Uri) => void): DisposableLike;
  dispose(): void;
}

/** Owns the manifest watcher and replaceable configured-source watchers. */
export class ForwardedWatchers<Uri> {
  private sourceWatchers: WatcherLike<Uri>[] = [];
  private sourceSubscriptions: DisposableLike[] = [];
  private readonly manifestSubscriptions: DisposableLike[];

  public constructor(
    private readonly manifestWatcher: WatcherLike<Uri>,
    private readonly roots: () => readonly string[],
    private readonly createWatcher: (root: string) => WatcherLike<Uri>,
    private readonly forward: (uri: Uri, type: FileChangeType) => void,
  ) {
    this.manifestSubscriptions = [
      manifestWatcher.onDidChange(uri => { this.forward(uri, 2); this.refresh(); }),
      manifestWatcher.onDidCreate(uri => { this.forward(uri, 1); this.refresh(); }),
      manifestWatcher.onDidDelete(uri => { this.forward(uri, 3); this.refresh(); }),
    ];
    this.refresh();
  }

  public refresh(): void {
    for (const subscription of this.sourceSubscriptions) subscription.dispose();
    this.sourceSubscriptions = [];
    for (const watcher of this.sourceWatchers) watcher.dispose();
    this.sourceWatchers = [];
    for (const root of this.roots()) {
      const watcher = this.createWatcher(root);
      this.sourceWatchers.push(watcher);
      this.sourceSubscriptions.push(
        watcher.onDidCreate(uri => this.forward(uri, 1)),
        watcher.onDidChange(uri => this.forward(uri, 2)),
        watcher.onDidDelete(uri => this.forward(uri, 3)),
      );
    }
  }

  public dispose(): void {
    for (const subscription of this.manifestSubscriptions) subscription.dispose();
    for (const subscription of this.sourceSubscriptions) subscription.dispose();
    for (const watcher of this.sourceWatchers) watcher.dispose();
    this.manifestWatcher.dispose();
    this.sourceSubscriptions = [];
    this.sourceWatchers = [];
  }
}
