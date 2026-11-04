
using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using NILUBRAIN.WPF.Models;
using NILUBRAIN.WPF.Services;
using NILUBRAIN.WPF.Helpers;

namespace NILUBRAIN.WPF.Views;

public partial class LoginWindow : Window
{
    private readonly AuthService _authService;

    public LoginWindow()
    {
        InitializeComponent();

        _authService = App.Services
            .GetRequiredService<AuthService>();
    }

    // LOGIN BUTTON
    private async void btnLogin_Click(
        object sender, RoutedEventArgs e)
    {
        string email = txtEmail.Text.Trim();
        string password = txtPassword.Password;

        txtStatus.Text = "";

        // Email Validation
        if (string.IsNullOrWhiteSpace(email))
        {
            txtStatus.Text = "Please enter your email.";
            return;
        }

        // Password Validation
        if (string.IsNullOrWhiteSpace(password))
        {
            txtStatus.Text = "Please enter your password.";
            return;
        }

        try
        {
            btnLogin.IsEnabled = false;
            btnLogin.Content = "LOGGING IN...";

            // Create Login Request
            var request = new LoginRequest
            {
                Email = email,
                Password = password
            };

            // Call Login API
            var response = await _authService.LoginAsync(request);

            // Check API Response
            if (response == null || !response.Success)
            {
                txtStatus.Text =
                    response?.Message ?? "Invalid email or password.";

                return;
            }

            // Check JWT Token
            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                txtStatus.Text =
                    "JWT token not received from API.";

                return;
            }

            // Save JWT Token
            TokenStorage.SaveToken(
                response.AccessToken,
                response.ExpiresAt
            );

            // Open Dashboard Directly
            DashboardWindow dashboard = new DashboardWindow(
                response.FullName,
                response.Email
            );

            Application.Current.MainWindow = dashboard;

            dashboard.Show();

            // Close Login Window
            this.Close();
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Unable to connect to API.";

            MessageBox.Show(
                ex.Message,
                "Login API Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            btnLogin.IsEnabled = true;
            btnLogin.Content = "LOG IN";
        }
    }

    // SIGNUP NAVIGATION
    private void txtSignUpRedirect_MouseDown(
        object sender, MouseButtonEventArgs e)
    {
        SignupWindow signup = new SignupWindow();

        Application.Current.MainWindow = signup;

        signup.Show();

        this.Close();
    }

    // MINIMIZE
    private void btnMinimize_Click(
        object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    // CLOSE
    private void btnClose_Click(
        object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    // WINDOW DRAG
    private void Window_MouseDown(
        object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
