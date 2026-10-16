using Runic.Translations;
using SharedCatalog.Translations;

// A tray process stands in for any C# code outside the Views: menus, notifications and logs. It formats the
// frontend's catalog through the generated C# API, in the locale the application chose (here the first argument).
ITranslationManager manager = DashboardTextCatalog.CreateManager(args.Length > 0 ? args[0] : "en");
var text = new DashboardText(manager);

Print();

// When the user picks another language, switch the manager; every later call formats in the new locale.
if (args.Length > 1)
{
    await manager.SetLocaleAsync(args[1]);
    Print();
}

void Print()
{
    Console.WriteLine($"[{manager.Current.Locale}] {text.Messages.tray_open} | {text.Messages.tray_check} | {text.Messages.tray_quit}");
    Console.WriteLine($"[{manager.Current.Locale}] {text.Messages.tray_updates_ready(1)} {text.Messages.tray_updates_ready(3)}");
}
