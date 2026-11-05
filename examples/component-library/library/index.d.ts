/** The storage meter's heading. */
export declare function storageTitle(): string;

/** The number of files and the space they use (in gigabytes), or a hint while there are none. */
export declare function describeStorage(usage: { files: number; used: number | string; total: number | string }): string;
