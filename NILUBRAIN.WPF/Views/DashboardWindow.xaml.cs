
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using NILUBRAIN.WPF.Views.InterviewAI;

namespace NILUBRAIN.WPF.Views
{
    public partial class DashboardWindow : Window
    {
        private readonly string _fullName;
        private readonly string _email;

        private bool _isSidebarExpanded = true;
        private bool _isExpandedState = false;

        // Constructor 1
        public DashboardWindow()
        {
            InitializeComponent();

            _fullName = "Administrator";
            _email = string.Empty;

            SetupCompactStartup();
            ShowDashboard();
        }

        // Constructor 2
        public DashboardWindow(string? fullName, string? email)
        {
            InitializeComponent();

            _fullName = string.IsNullOrWhiteSpace(fullName)
                ? "User"
                : fullName;

            _email = email ?? string.Empty;

            SetupCompactStartup();
            ShowDashboard();
        }

        // Dashboard Startup
        private void SetupCompactStartup()
        {
            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Width = 960;
            Height = 620;

            Loaded += (s, e) =>
            {
                var area = SystemParameters.WorkArea;

                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Top + (area.Height - Height) / 2;
            };
        }

        // Show Dashboard
        private void ShowDashboard()
        {
            PageTitle.Text = "Dashboard Workspace";
            PageSubtitle.Text = "Real-time analytics and AI autopilot status";

            DashboardPanel.Visibility = Visibility.Visible;
            FeaturePanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

        // Dashboard Navigation
        private void Dashboard_Click(object sender, RoutedEventArgs e)
        {
            ShowDashboard();
        }

        // SINGLE NAVIGATION HANDLER
        private void NavFeature_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            string feature = btn.Tag?.ToString() ?? "";

            // INTERVIEW ASSISTANT OPEN
            if (feature == "AI Interview Assistant")
            {
                OpenInterviewAssistant();
                return;
            }

            ConfigureFeature(feature);
        }

        // OPEN INTERVIEW ASSISTANT
        private void OpenInterviewAssistant()
        {
            try
            {
                InterviewAssistantWindow interviewWindow =
                    new InterviewAssistantWindow();

                // Dashboard hide
                this.Hide();

                // Interview window settings
                interviewWindow.Owner = this;

                // Interview close event
                interviewWindow.Closed += (s, e) =>
                {
                    // Dashboard पुन्हा Open
                    this.Show();

                    this.WindowState = WindowState.Normal;
                    this.Activate();
                };

                interviewWindow.Show();
            }
            catch (Exception ex)
            {
                this.Show();

                MessageBox.Show(
                    "Unable to open Interview Assistant.\n\n" + ex.Message,
                    "NILUBRAIN AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // Feature Configuration
        private void ConfigureFeature(string featureName)
        {
            PageTitle.Text = featureName;
            PageSubtitle.Text = $"Active AI Module: {featureName}";

            DashboardPanel.Visibility = Visibility.Collapsed;
            FeaturePanel.Visibility = Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Collapsed;

            FeatureTitle.Text = featureName;
            FeatureResult.Text = "";
            FeatureInput.Clear();

            switch (featureName)
            {
                case "Naukri Autopilot":

                    FeatureDescription.Text =
                        "Automates job applications on Naukri.";

                    FeatureInstruction.Text =
                        "ENTER AUTOPILOT PREFERENCES:";

                    FeatureInput.Text =
                        "Target Roles: Senior Full Stack Developer";

                    FeatureActionButton.Content =
                        "Start Naukri Autopilot";

                    break;

                case "Resume ATS Score":

                    FeatureDescription.Text =
                        "Analyze your resume and calculate ATS compatibility.";

                    FeatureInstruction.Text =
                        "UPLOAD OR ENTER RESUME DETAILS:";

                    FeatureActionButton.Content =
                        "Scan ATS Score";

                    break;

                default:

                    FeatureDescription.Text =
                        $"Supercharge your career workflows with {featureName}.";

                    FeatureInstruction.Text =
                        "ENTER PARAMETERS:";

                    FeatureInput.Text =
                        "Enter details here...";

                    FeatureActionButton.Content =
                        "Execute AI Command";

                    break;
            }
        }

        // Metric Card
        private void MetricCard_Click(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag != null)
            {
                MessageBox.Show(
                    border.Tag.ToString(),
                    "NILUBRAIN AI Analytics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        // Sidebar Toggle
        private void ToggleSidebar_Click(
            object sender,
            RoutedEventArgs e)
        {
            _isSidebarExpanded = !_isSidebarExpanded;

            SidebarColumn.Width = _isSidebarExpanded
                ? new GridLength(270)
                : new GridLength(70);

            BrandTextPanel.Visibility = _isSidebarExpanded
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Settings
        private void Settings_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageTitle.Text = "Platform Settings";
            PageSubtitle.Text = "Configure preferences and integration keys";

            DashboardPanel.Visibility = Visibility.Collapsed;
            FeaturePanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        // Feature Action
        private void FeatureAction_Click(
            object sender,
            RoutedEventArgs e)
        {
            string input = FeatureInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show("Please enter required information.");
                return;
            }

            FeatureResult.Text =
                $"Successfully processed {FeatureTitle.Text} request.";
        }

        // Logout
        private void Logout_Click(
            object sender,
            RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to log out?",
                "Logout Session",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                LoginWindow loginWin = new LoginWindow();

                Application.Current.MainWindow = loginWin;

                loginWin.Show();

                Close();
            }
        }

        // Window Drag
        private void ControlBar_MouseDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        // Close
        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        // Maximize / Restore
        private void Maximize_Click(
            object sender,
            RoutedEventArgs e)
        {
            var area = SystemParameters.WorkArea;

            if (!_isExpandedState)
            {
                Width = Math.Min(1300, area.Width);
                Height = Math.Min(780, area.Height);

                _isExpandedState = true;
            }
            else
            {
                Width = 960;
                Height = 620;

                _isExpandedState = false;
            }

            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
        }

        // Minimize
        private void Minimize_Click(
            object sender,
            RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
    }
}
