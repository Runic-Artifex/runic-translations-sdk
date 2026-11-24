- Runic.Translations.Build works with Visual Studio's MSBuild, which runs on .NET Framework
  ([runic-sdk#1299](https://github.com/Runic-Artifex/runic-sdk/issues/1299)). Its targets computed paths with
  `Path.GetFullPath(string, string)`, `Path.GetRelativePath` and `Path.TrimEndingDirectorySeparator`, which only .NET
  has, so a build in Visual Studio failed with MSB4186, for example with `RunicTranslationsCldrSubset` set. They now use
  MSBuild's `NormalizePath`, `NormalizeDirectory` and `MakeRelative`, which give the same paths in Visual Studio and
  `dotnet build`.
