
using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NILUBRAIN.WPF.Services;
using NILUBRAIN.WPF.Views;

namespace NILUBRAIN.WPF
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        private ApiHostService? _apiHost;

        private const string ApiUrl = "https://localhost:7143/";

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // 1. API PATH
                string apiPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "API",
                    "NILUBRAIN.API.exe"
                );

                // 2. START API
                _apiHost = new ApiHostService();

                bool apiStarted = await _apiHost.StartAsync(
                    apiPath,
                    ApiUrl
                );

                if (!apiStarted)
                {
                    MessageBox.Show(
                        "NILUBRAIN API failed to start. Please check API configuration.",
                        "NILUBRAIN API Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );

                    Shutdown();
                    return;
                }

                // 3. DEPENDENCY INJECTION
                var services = new ServiceCollection();

                // HTTP CLIENT
                services.AddSingleton(new HttpClient
                {
                    BaseAddress = new Uri(ApiUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                });

                // SERVICES
                services.AddSingleton<ApiService>();
                services.AddSingleton<AuthService>();

                // WINDOWS
                services.AddTransient<LoginWindow>();
                services.AddTransient<SignupWindow>();
                services.AddTransient<DashboardWindow>();

                // BUILD SERVICE PROVIDER
                Services = services.BuildServiceProvider();

                // 4. OPEN LOGIN WINDOW
                var login = Services.GetRequiredService<LoginWindow>();

                MainWindow = login;
                login.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.ToString(),
                    "NILUBRAIN Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Shutdown();
            }
        }

        // 5. STOP API WHEN APP CLOSES
        protected override void OnExit(ExitEventArgs e)
        {
            _apiHost?.Stop();

            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }
    }
}
