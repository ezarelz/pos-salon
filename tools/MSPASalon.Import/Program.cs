// MSPA Salon - legacy master-data import (command line).
//
//   dotnet run --project tools/MSPASalon.Import -- "C:\path\to\old\salon.sqlite"
//   dotnet run --project tools/MSPASalon.Import -- "C:\path\to\old\salon.sqlite" "D:\data\mspasalon.db"
//
// The first argument is the legacy database (opened READ-ONLY, never modified).
// The optional second argument is the target database; default = the application's database
// (%LOCALAPPDATA%\MSPASalon\mspasalon.db or the MSPASALON_DB environment variable).
// The same import is also available inside the application: Pengaturan → Import Data Aplikasi Lama.

using MSPASalon.Core.Domain;
using MSPASalon.Infrastructure;
using MSPASalon.Presentation.ViewModels;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Pemakaian: MSPASalon.Import <salon.sqlite lama> [database tujuan]");
    return 2;
}

var legacy = Path.GetFullPath(args[0]);
var target = Path.GetFullPath(args.Length == 2 ? args[1] : AppServices.DefaultDatabasePath());
Console.WriteLine($"Sumber (read-only) : {legacy}");
Console.WriteLine($"Database tujuan    : {target}");

try
{
    var app = new AppServices(target);
    var result = app.LegacyImport.Import(legacy);
    Console.WriteLine();
    Console.WriteLine(ShellViewModel.FormatImportResult(result));
    return 0;
}
catch (BusinessException ex)
{
    Console.Error.WriteLine($"Import gagal: {ex.Message}");
    return 1;
}
