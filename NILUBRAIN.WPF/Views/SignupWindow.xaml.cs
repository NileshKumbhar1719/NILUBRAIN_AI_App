
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace NILUBRAIN.WPF.Views;

public partial class SignupWindow : Window
{
    public SignupWindow()
    {
        InitializeComponent();
    }

    private void btnSignUp_Click(
        object sender, RoutedEventArgs e)
    {
        string fullName = txtFullName.Text.Trim();
        string username = txtUser.Text.Trim();
        string email = txtEmail.Text.Trim();

        string password = txtPassword.Password;
        string confirmPassword = txtConfirmPassword.Password;

        txtStatus.Text = "";

        // Full Name Validation

        if (string.IsNullOrWhiteSpace(fullName))
        {
            txtStatus.Text = "Please enter your full name.";
            return;
        }

        // Username Validation

        if (string.IsNullOrWhiteSpace(username))
        {
            txtStatus.Text = "Please enter your username.";
            return;
        }

        // Email Validation

        if (string.IsNullOrWhiteSpace(email))
        {
            txtStatus.Text = "Please enter your email.";
            return;
        }

        if (!Regex.IsMatch(
            email,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            txtStatus.Text = "Please enter a valid email.";
            return;
        }

        // Password Validation

        if (string.IsNullOrWhiteSpace(password))
        {
            txtStatus.Text = "Please enter your password.";
            return;
        }

        if (password.Length < 8)
        {
            txtStatus.Text =
                "Password must contain at least 8 characters.";
            return;
        }

        if (!password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) ||
            !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            txtStatus.Text =
                "Password must contain uppercase, lowercase, number and special character.";
            return;
        }

        // Confirm Password

        if (password != confirmPassword)
        {
            txtStatus.Text = "Passwords do not match.";
            return;
        }

        // API integration will be added next.

        MessageBox.Show(
            "All registration details are valid!",
            "NILUBRAIN AI",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // Login Navigation

    private void txtLoginRedirect_MouseDown(
        object sender, MouseButtonEventArgs e)
    {
        LoginWindow loginWindow = new LoginWindow();

        Application.Current.MainWindow = loginWindow;

        loginWindow.Show();

        Close();
    }

    // Minimize

    private void btnMinimize_Click(
        object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    // Close

    private void btnClose_Click(
        object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    // Drag Window

    private void Window_MouseDown(
        object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
