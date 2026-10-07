using Runic.Translations.Runtime.Tests;

var runner = new TestRunner();
Rmf2RuntimeV5Tests.Register(runner);
TypedSlotBindingsTests.Register(runner);
return await runner.RunAsync().ConfigureAwait(false);
