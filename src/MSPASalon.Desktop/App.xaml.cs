using System.Globalization;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using MSPASalon.Core.Entities;
using MSPASalon.Desktop.Services;
using MSPASalon.Desktop.Views;
using MSPASalon.Infrastructure;
using MSPASalon.Presentation.Services;
using MSPASalon.Presentation.ViewModels;
using MSPASalon.Presentation.ViewModels.Editors;

namespace MSPASalon.Desktop;

/// <summary>
/// Composition root and application flow: database init → login → (forced password change) → main window.
/// Logout returns to the login window. Command line: <c>--db "D:\path\mspasalon.db"</c> overrides the database location.
/// </summary>
public partial class App : Application
{
    private AppServices? _app;
    private PresentationServices? _services;
    private readonly DialogService _dialogs = new();
    private bool _loggingOut;

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MSPASalon", "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Indonesian formatting everywhere (dates, numbers) including WPF bindings.
        var culture = CultureInfo.GetCultureInfo("id-ID");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Before any window: the saved light/dark choice decides the palette of the login window too.
        var theme = ThemeService.LoadAndApply();

        var dbPath = DatabasePathFromArgs(e.Args) ?? AppServices.DefaultDatabasePath();
        try
        {
            _app = new AppServices(dbPath);
        }
        catch (Exception ex)
        {
            Log(ex);
            MessageBox.Show($"Database tidak bisa dibuka:{Environment.NewLine}{dbPath}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "POS Salon", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _services = new PresentationServices(
            _app.Customers, _app.Suppliers, _app.Categories, _app.Employees, _app.Products, _app.Services,
            _app.PaymentMethods, _app.Roles, _app.Users, _app.Auth, _app.Branch, _app.Backup, _app.Dashboard,
            _app.LegacyImport, _app.Clock,
            _app.Registrations, _app.Treatments, _app.Sales, _app.Purchases, _app.Adjustments, _app.StockCards,
            new PrintService(), theme, _app.Reports, _app.ReportExporter);

        ShowLogin();
    }

    private static string? DatabasePathFromArgs(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], "--db", StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private void ShowLogin()
    {
        while (true)
        {
            var login = new LoginViewModel(_app!.Auth, _app.Branch.Get().Name);
            var window = new LoginWindow(login);
            MainWindow = window;
            if (window.ShowDialog() != true || login.Session is null)
            {
                Shutdown();
                return;
            }

            var session = login.Session;
            if (session.User.MustChangePassword)
            {
                var changed = _dialogs.ShowEditor(new ChangePasswordViewModel(_app.Auth, session.User.Id, forced: true));
                if (!changed) continue;                       // back to login
                session = _app.Auth.Refresh(session);
            }
            ShowMain(session);
            return;
        }
    }

    private void ShowMain(UserSession session)
    {
        var shell = new ShellViewModel(_services!, _dialogs, session);
        var main = new Views.MainWindow(shell);
        MainWindow = main;
        shell.LogoutRequested += (_, _) =>
        {
            _loggingOut = true;
            main.Close();
        };
        main.Closed += (_, _) =>
        {
            if (_loggingOut)
            {
                _loggingOut = false;
                ShowLogin();
            }
            else
            {
                Shutdown();
            }
        };
        main.Show();
        main.Activate();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var file = Log(e.Exception);
        MessageBox.Show(
            $"Terjadi kesalahan yang tidak terduga:{Environment.NewLine}{e.Exception.Message}{Environment.NewLine}{Environment.NewLine}" +
            $"Detail tersimpan di:{Environment.NewLine}{file}",
            "POS Salon", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;   // keep the application running; every database operation is transactional
    }

    private static string Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var file = Path.Combine(LogDirectory, $"error-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
            return file;
        }
        catch (IOException)
        {
            return "(log tidak bisa ditulis)";
        }
        catch (UnauthorizedAccessException)
        {
            return "(log tidak bisa ditulis)";
        }
    }
}
