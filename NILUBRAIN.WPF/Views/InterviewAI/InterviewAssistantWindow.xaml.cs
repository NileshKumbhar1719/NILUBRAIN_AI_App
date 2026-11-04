using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using NAudio.Wave;
using UglyToad.PdfPig;
//using ICSharpCode.AvalonEdit;
//using ICSharpCode.AvalonEdit.Highlighting;
//using System.Windows.Documents;



using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using Microsoft.VisualBasic;

namespace NILUBRAIN.WPF.Views.InterviewAI
{
    public partial class InterviewAssistantWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private const int HOTKEY_ID = 9001;
        private const uint VK_SNAPSHOT = 0x2C; // PrintScreen Key
        private const uint MOD_NONE = 0x0000;


        private readonly object _transcriptionSync = new object();

        private SemaphoreSlim _transcriptionSemaphore = new SemaphoreSlim(1, 1);

        private TaskCompletionSource<string>? _activeTranscription;

        private const double MaxFontSize = 24.0;
        private const double MinFontSize = 9.0;
        private const double WindowSizeStep = 40.0;
        private const double MaxWindowWidth = 1100.0;
        private const double MaxWindowHeight = 900.0;
        private const double MinWindowWidth = 380.0;
        private const double MinWindowHeight = 320.0;

        private static readonly HttpClient OllamaHttp = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434/"),
            Timeout = TimeSpan.FromMinutes(2)
        };

        private const string PrimaryAiModel = "qwen2.5-coder:3b";
        //private const string PrimaryAiModel = "qwen2.5:1.5b";

        private Process? _pythonProcess;
        private StreamWriter? _pythonStdin;
        private bool _isPythonReady = false;

        private bool _isContinuousListening = false;
        private CancellationTokenSource? _cts;
        private bool _boldEnabled = true;
        private string _lastAnswer = "";
        private double _answerFontSize = 13.0;
        private double _questionFontSize = 13.5;
        private double _panelOpacityLevel = 0.91;

        private int _isGeneratingAnswer = 0;

        private readonly StringBuilder _answerBuilder = new();

        private const int UiUpdateIntervalMs = 60;

        // Dynamic Resume Context Variable
        private string _dynamicResumeText = "No resume uploaded yet. Defaulting to General Software Developer profile.";

        public InterviewAssistantWindow()
        {
            InitializeComponent();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            if (TxtAnswer != null)
            {
                TxtAnswer.SizeChanged += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(_lastAnswer))
                    {
                        RenderFormattedAnswer(_lastAnswer);
                    }
                };
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            try
            {
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                exStyle &= ~WS_EX_APPWINDOW;
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle);
            }
            catch { }

            try
            {
                SetWindowDisplayAffinity(helper.Handle, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { }

            try
            {
                RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_NONE, VK_SNAPSHOT);
                var source = HwndSource.FromHwnd(helper.Handle);
                source?.AddHook(HwndProc);
            }
            catch { }

            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

            SetStatus("Initializing Local Ollama & Whisper AI...");
            SetLiveText("Warming up local Python Whisper engine & Ollama...");
            StartPersistentPythonBackend();
        }

        private IntPtr HwndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _ = TriggerScreenshotAnalysisAsync();
                handled = true;
            }
            return IntPtr.Zero;
        }

        //private void MainWindow_Closed(object? sender, EventArgs e)
        //{
        //    _isContinuousListening = false;
        //    _cts?.Cancel();

        //    try
        //    {
        //        var helper = new WindowInteropHelper(this);
        //        UnregisterHotKey(helper.Handle, HOTKEY_ID);

        //        if (_pythonProcess != null && !_pythonProcess.HasExited)
        //        {
        //            _pythonProcess.Kill();
        //            _pythonProcess.Dispose();
        //        }
        //    }
        //    catch { }
        //}
        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _isContinuousListening = false;

            _cts?.Cancel();

            try
            {
                var helper = new WindowInteropHelper(this);

                UnregisterHotKey(helper.Handle, HOTKEY_ID);

                lock (_transcriptionSync)
                {
                    _activeTranscription?.TrySetCanceled();
                    _activeTranscription = null;
                }

                if (_pythonProcess != null &&
                    !_pythonProcess.HasExited)
                {
                    _pythonProcess.Kill(true);
                }

                _pythonProcess?.Dispose();

                _transcriptionSemaphore.Dispose();
                _cts?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Shutdown error: " + ex.Message);
            }
        }

        private void SetStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtStatus != null) TxtStatus.Text = message;
                if (TxtTimestamp != null) TxtTimestamp.Text = " · " + DateTime.Now.ToString("HH:mm");
            });
        }

        private void SetLiveText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtLiveAudio != null)
                {
                    TxtLiveAudio.Text = text;
                    TxtLiveAudio.Foreground = Brushes.White;
                }
            });
        }

        private void SetQuestionText(string question)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtLiveAudio != null)
                {
                    TxtLiveAudio.Text = question;
                    TxtLiveAudio.Foreground = Brushes.White;
                }

                if (TxtQuestion != null)
                {
                    TxtQuestion.Inlines.Clear();
                    TxtQuestion.Inlines.Add(new System.Windows.Documents.Run("💬 ") { Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#00B4D8")! });
                    TxtQuestion.Inlines.Add(new System.Windows.Documents.Run("Question: ") { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                    TxtQuestion.Inlines.Add(new System.Windows.Documents.Run(question) { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                    TxtQuestion.FontSize = _questionFontSize;
                }
            });
        }

        //private void SetAnswerText(string answer)
        //{
        //    _lastAnswer = answer;
        //    Dispatcher.Invoke(() =>
        //    {
        //        if (TxtAnswer != null)
        //        {
        //            TxtAnswer.Inlines.Clear();
        //            TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("⭐ ") { Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#FFD700")! });
        //            TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("Answer: ") { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        //            TxtAnswer.Inlines.Add(new System.Windows.Documents.Run(answer) { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        //            TxtAnswer.FontSize = _answerFontSize;
        //            AnswerScroll?.ScrollToHome();
        //        }
        //    });
        //}
        //private void SetAnswerText(string answer)
        //{
        //    _lastAnswer = answer;
        //    Dispatcher.Invoke(() =>
        //    {
        //        try
        //        {
        //            if (TxtAnswer != null)
        //            {
        //                TxtAnswer.Inlines.Clear();

        //                // Add header icon and label
        //                TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("⭐ ") { Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#FFD700")! });
        //                TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("Answer:\n") { FontWeight = FontWeights.Bold, Foreground = Brushes.White });

        //                // Format with VS Code colors (Gold key points, Green comments, Dark text)
        //                FormatAnswerWithVsCodeTheme(answer, TxtAnswer);

        //                // Scroll cleanly back to the top
        //                AnswerScroll?.ScrollToHome();
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            Debug.WriteLine("UI Render Error: " + ex.Message);
        //        }
        //    });
        //}

        //private void FormatAnswerWithVsCodeTheme(string rawAnswer, TextBlock targetTextBlock)
        //{
        //    string[] lines = rawAnswer.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        //    bool insideCodeBlock = false;

        //    foreach (var line in lines)
        //    {
        //        if (line.Trim().StartsWith("```"))
        //        {
        //            insideCodeBlock = !insideCodeBlock;
        //            targetTextBlock.Inlines.Add(new System.Windows.Documents.LineBreak());
        //            continue;
        //        }

        //        if (insideCodeBlock)
        //        {
        //            var codeRun = new System.Windows.Documents.Run(line + "\n")
        //            {
        //                Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#D4D4D4")!, // VS Code text
        //                FontFamily = new FontFamily("Consolas")
        //            };

        //            // Highlight comments in Green (#6A9955)
        //            if (line.Trim().StartsWith("//") || line.Trim().StartsWith("#"))
        //            {
        //                codeRun.Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#6A9955")!;
        //            }

        //            targetTextBlock.Inlines.Add(codeRun);
        //        }
        //        else
        //        {
        //            // Highlight Key Points / Headers in Gold (#FFD700)
        //            if (line.Contains(":") && (line.StartsWith("1.") || line.StartsWith("2.") || line.StartsWith("3.") || line.StartsWith("-") || line.Contains("APPROACH") || line.Contains("C# CODE")))
        //            {
        //                var parts = line.Split(':');
        //                targetTextBlock.Inlines.Add(new System.Windows.Documents.Run(parts[0] + ":")
        //                {
        //                    FontWeight = FontWeights.Bold,
        //                    Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#FFD700")! // Gold Highlight
        //                });

        //                if (parts.Length > 1)
        //                {
        //                    targetTextBlock.Inlines.Add(new System.Windows.Documents.Run(parts[1] + "\n") { Foreground = Brushes.White });
        //                }
        //            }
        //            else
        //            {
        //                targetTextBlock.Inlines.Add(new System.Windows.Documents.Run(line + "\n") { Foreground = Brushes.White });
        //            }
        //        }
        //    }
        //}

        private void SetAnswerText(string answer)
        {
            _lastAnswer = answer;

            Dispatcher.Invoke(() =>
            {
                RenderFormattedAnswer(answer);
                AnswerScroll?.ScrollToTop();
            });
        }

        //private void RenderFormattedAnswer(string answer)
        //{
        //    if (TxtAnswer == null)
        //        return;

        //    TxtAnswer.Inlines.Clear();

        //    string pattern =
        //        @"```(\w*)\s*\r?\n([\s\S]*?)```";

        //    int lastIndex = 0;

        //    foreach (Match match in Regex.Matches(answer, pattern))
        //    {
        //        // Normal text before code
        //        string normalText = answer.Substring(
        //            lastIndex,
        //            match.Index - lastIndex);

        //        AddColoredText(normalText);

        //        // Code language
        //        string language =
        //            match.Groups[1].Value;

        //        // Code content
        //        string code =
        //            match.Groups[2].Value;

        //        AddCodeBox(code, language);

        //        lastIndex = match.Index + match.Length;
        //    }

        //    // Remaining normal text
        //    AddColoredText(
        //        answer.Substring(lastIndex));
        //}
        private void RenderFormattedAnswer(string answer)
        {
            if (TxtAnswer == null)
                return;

            TxtAnswer.Inlines.Clear();

            TxtAnswer.Inlines.Add(new Run("⭐ ")
            {
                Foreground = Brushes.Gold
            });

            TxtAnswer.Inlines.Add(new Run("Answer:\n")
            {
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });

            string pattern = @"```(\w*)\s*\r?\n([\s\S]*?)(?:```|$)";

            int lastIndex = 0;

            foreach (Match match in Regex.Matches(answer, pattern))
            {
                AddColoredText(answer.Substring(
                    lastIndex, match.Index - lastIndex));

                string language = match.Groups[1].Value;
                string code = match.Groups[2].Value;

                AddCodeBox(code, language);

                lastIndex = match.Index + match.Length;
            }

            AddColoredText(answer.Substring(lastIndex));
        }

        private void AddColoredText(string text)
        {
            string[] lines = text.Split('\n');

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');

                if (string.IsNullOrWhiteSpace(line))
                {
                    TxtAnswer.Inlines.Add(
                        new LineBreak());
                    continue;
                }

                bool isHeading =
                    Regex.IsMatch(line,
                    @"^\s*(\d+\.|APPROACH|C# CODE|KEY POINT|IMPORTANT|LINE-BY-LINE|SAMPLE INPUT|COMPLEXITY|TIME COMPLEXITY|SPACE COMPLEXITY)",
                    RegexOptions.IgnoreCase);

                var run = new Run(line)
                {
                    Foreground = isHeading
                        ? new SolidColorBrush(
                            Color.FromRgb(255, 215, 0))
                        : Brushes.White,

                    FontWeight = isHeading
                        ? FontWeights.Bold
                        : (_boldEnabled
                            ? FontWeights.Bold
                            : FontWeights.Normal),

                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = _answerFontSize
                };

                TxtAnswer.Inlines.Add(run);
                TxtAnswer.Inlines.Add(new LineBreak());
            }
        }





        private void AddCodeBox(string code, string language)
        {
            if (TxtAnswer == null || string.IsNullOrWhiteSpace(code))
                return;

            double availableWidth = TxtAnswer.ActualWidth;

            if (availableWidth <= 0)
                availableWidth = 700;

            double boxWidth = Math.Max(200, availableWidth - 4);

            var editor = new TextEditor
            {
                Text = code.TrimEnd(),

                IsReadOnly = true,

                // Remove line numbers
                ShowLineNumbers = false,

                FontFamily = new FontFamily("Consolas"),
                FontSize = _answerFontSize,
                FontWeight = FontWeights.Bold,

                Background = new SolidColorBrush(
                    Color.FromRgb(30, 34, 45)),

                Foreground = new SolidColorBrush(
                    Color.FromRgb(235, 238, 245)),

                Padding = new Thickness(12, 6, 12, 6),

                Width = boxWidth - 4,

                BorderThickness = new Thickness(0),

                HorizontalScrollBarVisibility =
                    System.Windows.Controls.ScrollBarVisibility.Auto,

                VerticalScrollBarVisibility =
                    System.Windows.Controls.ScrollBarVisibility.Disabled
            };

            // Syntax highlighting
            string syntax = language.Trim().ToLowerInvariant();

            if (syntax == "cs" || syntax == "csharp")
                syntax = "C#";

            var definition =
                HighlightingManager.Instance.GetDefinition(syntax);

            if (definition != null)
                editor.SyntaxHighlighting = definition;

            // Exact height according to code lines
            int lineCount = editor.Document.LineCount;

            double lineHeight = _answerFontSize * 1.55;

            editor.Height =
                (lineCount * lineHeight) + 12;

            // Premium rounded code box
            var border = new Border
            {
                Width = boxWidth,

                Background = new SolidColorBrush(
                    Color.FromRgb(30, 34, 45)),

                CornerRadius = new CornerRadius(10),

                BorderThickness = new Thickness(0),

                Padding = new Thickness(2),

                // Minimum gap
                Margin = new Thickness(0),

                HorizontalAlignment = HorizontalAlignment.Left,

                SnapsToDevicePixels = true,

                ClipToBounds = true,

                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 12,
                    ShadowDepth = 2,
                    Opacity = 0.30
                },

                Child = editor
            };

            // Start code box on next line
            TxtAnswer.Inlines.Add(new LineBreak());

            TxtAnswer.Inlines.Add(
                new InlineUIContainer(border)
                {
                    BaselineAlignment = BaselineAlignment.Top
                }
            );

            // Next theory on next line
            TxtAnswer.Inlines.Add(new LineBreak());
        }


        private static string CleanTokenText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text;
        }

        private void StartWaveAnimation()
        {
            Dispatcher.Invoke(() =>
            {
                if (Resources["WaveAnimation"] is Storyboard sb)
                {
                    sb.Begin();
                }
            });
        }

        private void StopWaveAnimation()
        {
            Dispatcher.Invoke(() =>
            {
                if (Resources["WaveAnimation"] is Storyboard sb)
                {
                    sb.Stop();
                }
            });
        }

        private void StartPersistentPythonBackend()
        {
            Task.Run(() =>
            {
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string pythonDir = Path.Combine(baseDir, "PythonBackend");
                    if (!Directory.Exists(pythonDir)) pythonDir = baseDir;

                    string scriptPath = Path.Combine(pythonDir, "speech_listener.py");
                    if (!File.Exists(scriptPath)) scriptPath = Path.Combine(baseDir, "speech_listener.py");

                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string[] pythonCommands = {
                        "python", "py", "python3",
                        Path.Combine(localAppData, @"Programs\Python\Python312\python.exe"),
                        Path.Combine(localAppData, @"Programs\Python\Python311\python.exe"),
                        @"C:\Program Files\Python312\python.exe",
                        @"C:\Python312\python.exe"
                    };

                    string selectedCmd = "";
                    foreach (var cmd in pythonCommands)
                    {
                        if (string.IsNullOrEmpty(cmd)) continue;
                        if (cmd.Contains('\\') && !File.Exists(cmd)) continue;
                        selectedCmd = cmd;
                        break;
                    }

                    if (string.IsNullOrEmpty(selectedCmd))
                    {
                        SetStatus("Python not found!");
                        return;
                    }

                    var psi = new ProcessStartInfo
                    {
                        FileName = selectedCmd,
                        Arguments = $"\"{scriptPath}\"",
                        WorkingDirectory = pythonDir,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    _pythonProcess = Process.Start(psi);
                    if (_pythonProcess != null)
                    {
                        _pythonStdin = _pythonProcess.StandardInput;

                        Task.Run(async () =>
                        {
                            try
                            {
                                while (!_pythonProcess.StandardOutput.EndOfStream)
                                {
                                    string? line = _pythonProcess.StandardOutput.ReadLine();
                                    if (!string.IsNullOrEmpty(line))
                                    {
                                        if (line == "AI_READY" || line.Contains("AI_READY"))
                                        {
                                            _isPythonReady = true;
                                            SetStatus("Ready 🎙 (Stealth Active)");
                                            SetLiveText("Nilubrain AI hidden & ready for interview!");
                                            break;
                                        }
                                    }
                                }
                            }
                            catch { }
                        });

                        Task.Run(async () =>
                        {
                            await Task.Delay(4000);
                            if (!_isPythonReady)
                            {
                                _isPythonReady = true;
                                SetStatus("Ready 🎙 (Stealth Active)");
                                SetLiveText("Nilubrain AI hidden & ready for interview!");
                            }
                        });
                    }
                }
                catch
                {
                    _isPythonReady = true;
                    SetStatus("Ready 🎙");
                }
            });
        }

        private void ToggleContinuousListening()
        {
            if (!_isContinuousListening)
            {
                _isContinuousListening = true;
                _cts = new CancellationTokenSource();
                SetStatus("🔴 LIVE MEETING & INTERVIEW MODE (Smart VAD)");
                SetLiveText("Listening to Teams speaker...");
                StartWaveAnimation();

                Task.Run(() => ContinuousListenLoop(_cts.Token));
            }
            else
            {
                _isContinuousListening = false;
                _cts?.Cancel();
                SetStatus("Ready 🎙 (Paused)");
                SetLiveText("Live listening paused.");
                StopWaveAnimation();
            }
        }

        private async Task ContinuousListenLoop(CancellationToken token)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string pythonDir = Path.Combine(baseDir, "PythonBackend");
            if (!Directory.Exists(pythonDir)) pythonDir = baseDir;

            while (_isContinuousListening && !token.IsCancellationRequested)
            {
                string uniqueWavPath = Path.Combine(pythonDir, $"speech_{Guid.NewGuid():N}.wav");

                try
                {
                    SetLiveText("🎙 Listening to interview question...");
                    long recordedBytes = await RecordUntilSilenceAsync(uniqueWavPath, token);

                    if (!token.IsCancellationRequested && _isContinuousListening)
                    {
                        if (recordedBytes > 4000 && File.Exists(uniqueWavPath))
                        {
                            SetStatus("⚡ Transcribing full question...");
                            string recognizedText = await RunTranscriptionAsync(uniqueWavPath);

                            if (!string.IsNullOrWhiteSpace(recognizedText) &&
                                !recognizedText.Contains("No speech") &&
                                !recognizedText.Contains("Error") &&
                                recognizedText.Length > 3)
                            {
                                SetQuestionText(recognizedText);

                                SetStatus("⚡ Building Interview Prompt...");

                                string finalPrompt =
                                    BuildInterviewPrompt(recognizedText);

                                SetStatus("⚡ Streaming AI Answer...");

                                await StreamAiAnswerRealtimeAsync(finalPrompt);
                            }
                            else
                            {
                                SetLiveText("Listening... (Waiting for question)");
                            }
                        }
                        else
                        {
                            if (File.Exists(uniqueWavPath)) { try { File.Delete(uniqueWavPath); } catch { } }
                        }
                    }
                }
                catch (Exception ex)
                {
                    SetLiveText("Loop Error: " + ex.Message);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(uniqueWavPath))
                            File.Delete(uniqueWavPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("WAV cleanup error: " + ex.Message);
                    }
                }
                SetStatus("🔴 LIVE MEETING & INTERVIEW MODE");
                await Task.Delay(100, token);

            }

        }

        private static Task<long> RecordUntilSilenceAsync(string outputPath, CancellationToken token)
        {
            return Task.Run(() =>
            {
                long totalBytesWritten = 0;
                try
                {
                    using var waveIn = new WasapiLoopbackCapture();
                    waveIn.WaveFormat = new WaveFormat(16000, 16, 1);

                    using var writer = new WaveFileWriter(outputPath, waveIn.WaveFormat);
                    var stopEvent = new ManualResetEvent(false);

                    bool isSpeaking = false;
                    int silenceCounter = 0;
                    const int silenceThresholdLimit = 20;
                    //const int silenceThresholdLimit = 8;

                    waveIn.DataAvailable += (s, e) =>
                    {
                        if (e.BytesRecorded > 0)
                        {
                            float maxVal = 0;
                            for (int i = 0; i < e.BytesRecorded; i += 2)
                            {
                                short sample = BitConverter.ToInt16(e.Buffer, i);
                                float sampleVal = Math.Abs(sample / 32768f);
                                if (sampleVal > maxVal) maxVal = sampleVal;
                            }

                            if (maxVal > 0.015f)
                            {
                                isSpeaking = true;
                                silenceCounter = 0;
                            }
                            else if (isSpeaking)
                            {
                                silenceCounter++;
                            }

                            writer.Write(e.Buffer, 0, e.BytesRecorded);
                            totalBytesWritten += e.BytesRecorded;
                        }
                    };

                    waveIn.RecordingStopped += (s, e) => stopEvent.Set();
                    waveIn.StartRecording();

                    //int maxDurationMs = 20000;
                    int maxDurationMs = 30000;
                    int elapsed = 0;

                    while (elapsed < maxDurationMs && !token.IsCancellationRequested)
                    {
                        Thread.Sleep(100);

                        elapsed += 100;

                        if (isSpeaking && silenceCounter >= silenceThresholdLimit)
                        {
                            break;
                        }
                    }

                    waveIn.StopRecording();
                    stopEvent.WaitOne(200);
                    writer.Flush();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("VAD AUDIO ERROR: " + ex.Message);
                }
                return totalBytesWritten;
            }, token);
        }

        private async Task<string> RunTranscriptionAsync(string wavPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_pythonStdin == null || _pythonProcess == null || _pythonProcess.HasExited)
                    {
                        return "No speech detected";
                    }

                    lock (_pythonStdin)
                    {
                        _pythonStdin.WriteLine($"TRANSCRIBE:{wavPath}");
                        _pythonStdin.Flush();
                    }

                    DateTime start = DateTime.Now;
                    while ((DateTime.Now - start).TotalSeconds < 15)
                    {
                        if (!_pythonProcess.StandardOutput.EndOfStream)
                        {
                            string? line = _pythonProcess.StandardOutput.ReadLine();
                            if (!string.IsNullOrEmpty(line) && line.StartsWith("RESULT:"))
                            {
                                return line[7..].Trim();
                            }
                        }
                        Thread.Sleep(50);
                    }
                    return "No speech detected";
                }
                catch (Exception ex)
                {
                    return "Ex: " + ex.Message;
                }
            });
        }

        private void UploadResume_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Resume Files (*.pdf;*.txt)|*.pdf;*.txt|All Files (*.*)|*.*";

                if (openFileDialog.ShowDialog() == true)
                {
                    string filePath = openFileDialog.FileName;
                    string extractedText = "";

                    if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // फाईल लॉक असण्याचा प्रॉब्लेम टाळण्यासाठी बाईट्स मेमरीमध्ये लोड करून वाचणे
                            byte[] fileBytes = File.ReadAllBytes(filePath);
                            using (MemoryStream ms = new MemoryStream(fileBytes))
                            {
                                using (PdfDocument pdf = PdfDocument.Open(ms))
                                {
                                    StringBuilder sb = new StringBuilder();
                                    foreach (var page in pdf.GetPages())
                                    {
                                        sb.AppendLine(page.Text);
                                    }
                                    extractedText = sb.ToString();
                                }
                            }
                        }
                        catch (Exception pdfEx)
                        {
                            MessageBox.Show("Could not read this PDF format. Try converting your resume to a simple .txt file.\n\nDetails: " + pdfEx.Message, "PDF Read Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        extractedText = File.ReadAllText(filePath, Encoding.UTF8);
                    }

                    if (!string.IsNullOrWhiteSpace(extractedText))
                    {
                        _dynamicResumeText = extractedText.Trim();
                        SetStatus("✅ Resume Loaded Successfully!");
                        MessageBox.Show("Resume loaded successfully! Now NILUBRAIN AI. will give answers according to your resume.", "Resume Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("The selected resume file appears to be empty or contains unreadable text.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus("Error reading resume file.");
                MessageBox.Show("Error reading resume: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task TriggerScreenshotAnalysisAsync()
        {
            try
            {
                if (Interlocked.CompareExchange(
                    ref _isGeneratingAnswer, 0, 0) == 1)
                {
                    SetStatus("Please wait... AI is generating answer.");
                    return;
                }

                SetStatus("Reading question from Clipboard...");

                string detectedQuestion = await Dispatcher.InvokeAsync(() =>
                {
                    return Clipboard.ContainsText()
                        ? Clipboard.GetText().Trim()
                        : "";
                });

                if (string.IsNullOrWhiteSpace(detectedQuestion) ||
                    detectedQuestion.Length < 3)
                {
                    SetAnswerText(
                        "Please copy the question first using Ctrl + C.");

                    SetStatus("Ready 🎙");
                    return;
                }

                SetQuestionText(detectedQuestion);

                // Always build the correct interview prompt.
                string finalPrompt = BuildInterviewPrompt(detectedQuestion);

                SetStatus("⚡ Screenshot question detected. Generating answer...");

                await StreamAiAnswerRealtimeAsync(finalPrompt);
            }
            catch (Exception ex)
            {
                SetAnswerText("Screenshot Error: " + ex.Message);
                SetStatus("Ready 🎙");
            }
        }
        private string DetectInterviewQuestionType(string question)
        {
            string q = question.Trim().ToLowerInvariant();

            // Self Introduction
            if (Regex.IsMatch(q,
                @"\b(tell me about yourself|introduce yourself|"
                + @"give your introduction|self introduction|"
                + @"walk me through your resume)\b"))
            {
                return "SELF_INTRODUCTION";
            }

            // Coding Questions
            if (Regex.IsMatch(q,
                @"\b(write a program|write code|implement|"
                + @"solve this problem|coding question|"
                + @"write a function|find the output|"
                + @"reverse a string|reverse a number|"
                + @"check whether|check if|"
                + @"find the largest|find the smallest|"
                + @"sort an array|remove duplicates|"
                + @"palindrome|fibonacci|factorial|"
                + @"prime number|two sum|binary search)\b"))
            {
                return "CODING";
            }

            // Project Questions
            if (Regex.IsMatch(q,
                @"\b(current project|previous project|"
                + @"explain your project|project architecture|"
                + @"project workflow|project modules|"
                + @"project challenges|your responsibilities|"
                + @"role in project|database design|"
                + @"project description)\b"))
            {
                return "PROJECT";
            }

            // HR Questions
            if (Regex.IsMatch(q,
                @"\b(strengths|weaknesses|salary expectation|"
                + @"why should we hire you|why are you leaving|"
                + @"reason for change|career goals|"
                + @"where do you see yourself|"
                + @"notice period|relocation)\b"))
            {
                return "HR";
            }

            // Scenario Based Questions
            if (Regex.IsMatch(q,
                @"\b(how would you handle|what would you do|"
                + @"real time scenario|real world scenario|"
                + @"production issue|performance issue|"
                + @"debug a production|how do you troubleshoot)\b"))
            {
                return "SCENARIO";
            }

            // Technical Theory Questions
            if (Regex.IsMatch(q,
                @"\b(what is|what are|explain|difference between|"
                + @"why do we use|how does|how do you|"
                + @"advantages|disadvantages|features of|"
                + @"lifecycle|architecture of|types of)\b"))
            {
                return "TECHNICAL_THEORY";
            }

            return "GENERAL_INTERVIEW";
        }
        private string BuildInterviewPrompt(string detectedQuestion)
        {
            string questionType =
                DetectInterviewQuestionType(detectedQuestion);

            string candidateName =
     ExtractNameFromResume(_dynamicResumeText);

            string candidateExperience =
                ExtractExperienceFromResume(_dynamicResumeText);

            string resume = _dynamicResumeText;

            string commonInstructions = $@"
You are an experienced Senior .NET Full Stack Developer
conducting a real technical job interview.

You must answer as the candidate using first person
when the question is about personal experience.

CANDIDATE PROFILE:
- Experience:  {candidateExperience}.
- Primary technology: .NET Full Stack Development.
- Programming: C#.
- Backend: ASP.NET Core, ASP.NET MVC, Web API, ADO.NET.
- ORM: Entity Framework.
- Frontend: Angular, JavaScript.
- Database: SQL Server.



IMPORTANT RULES:
1. Use simple and natural English.
2. Answers must sound like a real human developer.
3. Use short, clear and professional sentences.
4. Avoid difficult English and unnecessary textbook definitions.
5. Never invent company experience, project details or achievements.
6. Use only the uploaded resume for personal experience.
7. Give practical real-world examples wherever useful.
8. Do not repeat the interview question.
9. Do not add unnecessary greetings.
10. Make answers easy to speak in a real interview.
";

            string specificInstructions = "";

            switch (questionType)
            {
                case "SELF_INTRODUCTION":

                    specificInstructions = $@"
QUESTION TYPE: SELF INTRODUCTION

Prepare a natural introduction.

The following introduction is already displayed before
the AI response:

Good morning, sir/madam. First of all, thank you for
giving me this opportunity to introduce myself.
My name is {candidateName}.

Continue directly after this sentence.

Do not repeat the greeting or candidate name.

Include these points in a natural order:
1. Professional experience.
2. Current or previous company, if mentioned in resume.
3. Technical skills.
4. Current project.
5. Your actual responsibilities.
6. Educational background.
7. A professional closing sentence.

Use approximately 60-90 seconds of spoken English.

Do not add any experience or project details that are
not present in the uploaded resume.
";
                    break;

                case "CODING":

                    specificInstructions = @"
QUESTION TYPE: LIVE CODING INTERVIEW

Use C# as the primary programming language because
the candidate is a .NET Full Stack Developer.

STRICT OUTPUT FORMAT:

1. APPROACH
Explain the logic in 2-4 simple sentences.
Explain how a developer would think about the problem.

2. C# CODE
Provide complete, working C# code.

Requirements:
- Use correct C# syntax.
- Include necessary using statements.
- Include a complete Main method when a console
  application is appropriate.
- Use meaningful variable names.
- Handle important edge cases.
- Do not provide incomplete pseudocode.
- Put code inside a C# code block.
- Keep the code clean and easy to copy.

3. LINE-BY-LINE EXPLANATION
Explain every important code statement in simple English.
Follow the actual order of the code.
Explain what each statement does and why it is used.

4. SAMPLE INPUT AND OUTPUT
Show at least one example.

5. COMPLEXITY
Time Complexity: O(...)
Space Complexity: O(...)

IMPORTANT:
The code must compile and solve the actual problem.
Do not assume that every problem needs an advanced
algorithm. Prefer a simple and correct solution first.
";
                    break;

                case "TECHNICAL_THEORY":

                    specificInstructions = @"
QUESTION TYPE: TECHNICAL THEORY

Answer using this format:

1. What is it?
Give a simple definition.

2. How does it work?
Explain the concept in easy English.

3. Real-time example
Give a practical example from .NET development.

4. C# example
Provide a short and correct code example when useful.

5. Interview closing
Give one natural sentence explaining why it is useful.

For difference-between questions, use a comparison table.

Avoid lengthy textbook explanations.
";
                    break;

                case "PROJECT":

                    specificInstructions = @"
QUESTION TYPE: REAL-TIME PROJECT INTERVIEW

Answer as a developer explaining actual project work.

Use only project information available in the resume.

Structure:
1. Project introduction.
2. Business purpose.
3. Technologies used.
4. Architecture and application flow.
5. Modules and functionality.
6. Database interaction.
7. Your actual responsibilities.
8. Challenges and solutions, only when supported
   by the resume or question context.

Use first-person sentences such as:
- In my project, we use...
- My responsibility is...
- I worked on...
- I developed...
- I handled...

Do not invent modules, APIs, architecture or
performance improvements.
If a specific detail is missing, explain only what
is known instead of making up an answer.
";
                    break;

                case "HR":

                    specificInstructions = @"
QUESTION TYPE: HR INTERVIEW

Answer in first person.

Use simple, polite and confident English.

Keep the answer natural and around 30-60 seconds.

Use the resume wherever personal experience is needed.
Do not invent salary, notice period or personal details.
";
                    break;

                case "SCENARIO":

                    specificInstructions = @"
QUESTION TYPE: REAL-WORLD SCENARIO

Answer like an experienced .NET developer.

Structure:
1. Understand the problem.
2. Explain how you would investigate it.
3. Explain the technical solution.
4. Give a practical .NET example where relevant.
5. Explain how you would test and verify the solution.

Use practical development situations.
Do not give only theoretical definitions.
";
                    break;

                default:

                    specificInstructions = @"
QUESTION TYPE: GENERAL TECHNICAL INTERVIEW

Answer directly in simple English.

Explain the concept, give a practical example,
and include C# or .NET code wherever useful.

If the question asks about personal experience,
use only the uploaded resume.
";
                    break;
            }

            return $@"
{commonInstructions}

{specificInstructions}

INTERVIEWER QUESTION:
{detectedQuestion}

Now provide the final interview-ready answer.
";
        }

        private async Task StreamAiAnswerRealtimeAsync(string questionPrompt)
        {
            // Prevent multiple AI requests at the same time.
            if (Interlocked.CompareExchange(
                ref _isGeneratingAnswer, 1, 0) != 0)
            {
                SetStatus("Please wait... AI is generating answer.");
                return;
            }

            try
            {
                bool isSelfIntro =
                    questionPrompt.Contains(
                        "SELF_INTRODUCTION",
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    questionPrompt.Contains(
                        "SELF INTRODUCTION",
                        StringComparison.OrdinalIgnoreCase);

                string candidateName =
                    ExtractNameFromResume(_dynamicResumeText);

                string mandatoryIntro =
                    $"Good morning, sir/madam. First of all, thank you for giving me this opportunity to introduce myself. My name is {candidateName}. ";

                string expertSystemPrompt = $@"
You are a professional .NET Full Stack Developer
answering real technical interview questions.

CANDIDATE RESUME:
{_dynamicResumeText}

COMMUNICATION STYLE:
- Speak in basic English.
- Sound natural, confident and professional.
- Use first-person language for personal experience.
- Give technically correct answers.
- Never invent resume information.
- Make answers easy to speak aloud.

CODING ANSWERS:
- Provide complete working C# code when appropriate.
- Include correct syntax and necessary using statements.
- Explain important code statements line by line.
- Include sample input and output.
- Include time and space complexity.
- Use proper C# Markdown code fences.

THEORY ANSWERS:
- Explain what the concept means.
- Explain how it works internally.
- Explain WHY and HOW.
- Give practical .NET examples.
- Include C# code when useful.
- Explain performance implications when relevant.
- Answer every part of the question.
- Use simple, natural English.

PROJECT ANSWERS:
- Use only actual uploaded resume information.
- Never invent project architecture or responsibilities.
- Explain actual workflow and technical responsibilities.

SELF INTRODUCTION:
- The greeting and candidate name are already displayed.
- Never repeat the greeting or candidate name.
- Continue directly with professional experience.

IMPORTANT:
Answer only the interviewer question.
Do not include unnecessary greetings or closing statements.
";

                // Prepare answer display on UI thread.
                //System.Windows.Documents.Run? bodyRun = null;

                //StringBuilder displayBuffer = new StringBuilder();

                //if (isSelfIntro)
                //{
                //    displayBuffer.Append(mandatoryIntro);
                //}

                //_lastAnswer = displayBuffer.ToString();

                //await Dispatcher.InvokeAsync(() =>
                //{
                //    if (TxtAnswer == null)
                //        return;

                //    TxtAnswer.Inlines.Clear();

                //    TxtAnswer.Inlines.Add(
                //        new System.Windows.Documents.Run("⭐ ")
                //        {
                //            Foreground = Brushes.Gold
                //        });

                //    TxtAnswer.Inlines.Add(
                //        new System.Windows.Documents.Run("Answer: ")
                //        {
                //            FontWeight = FontWeights.Bold,
                //            Foreground = Brushes.White
                //        });

                //    bodyRun = new System.Windows.Documents.Run
                //    {
                //        FontWeight = _boldEnabled
                //            ? FontWeights.Bold
                //            : FontWeights.Normal,

                //        Foreground = Brushes.White
                //    };

                //    bodyRun.Text = displayBuffer.ToString();

                //    TxtAnswer.Inlines.Add(bodyRun);

                //    TxtAnswer.FontSize = _answerFontSize;

                //    // Always start from the first line.
                //    AnswerScroll?.ScrollToHome();
                //});

                //SetStatus("⚡ AI Answer Generating...");

                //var payload = new
                //{
                //    model = PrimaryAiModel,

                //    messages = new object[]
                //    {
                //new
                //{
                //    role = "system",
                //    content = expertSystemPrompt
                //},
                //new
                //{
                //    role = "user",
                //    content = questionPrompt
                //}
                //    },

                //    stream = true,

                //    keep_alive = -1,
                //    options = new
                //    {
                //        num_predict = 800,
                //        temperature = 0.2,
                //        top_p = 0.9,
                //        repeat_penalty = 1.05,
                //        num_ctx = 4096
                //    }

                //    //options = new
                //    //{
                //    //    //num_predict = 1000,
                //    //    //temperature = 0.2,
                //    //    //top_p = 0.9,
                //    //    //repeat_penalty = 1.1

                //    //}
                //};

                //using var request = new HttpRequestMessage(
                //    HttpMethod.Post,
                //    "api/chat");

                //request.Content = new StringContent(
                //    JsonSerializer.Serialize(payload),
                //    Encoding.UTF8,
                //    "application/json");

                //using var response = await OllamaHttp.SendAsync(
                //    request,
                //    HttpCompletionOption.ResponseHeadersRead);

                //if (!response.IsSuccessStatusCode)
                //{
                //    string error =
                //        await response.Content.ReadAsStringAsync();

                //    SetAnswerText(
                //        "AI Error: " + response.StatusCode + "\n" + error);

                //    SetStatus("Ready 🎙");
                //    return;
                //}

                //using var responseStream =
                //    await response.Content.ReadAsStreamAsync();

                //using var reader =
                //    new StreamReader(responseStream, Encoding.UTF8);

                //// Collect tokens before updating the UI.
                //StringBuilder pendingBuffer = new StringBuilder();

                //Stopwatch updateTimer = Stopwatch.StartNew();

                //// const int UiUpdateIntervalMs = 50;
                //const int UiUpdateIntervalMs = 30;

                //bool isCompleted = false;

                //while (!isCompleted)
                //{
                //    string? line = await reader.ReadLineAsync();

                //    if (line == null)
                //        break;

                //    if (string.IsNullOrWhiteSpace(line))
                //        continue;

                //    try
                //    {
                //        using JsonDocument json =
                //            JsonDocument.Parse(line);

                //        JsonElement root = json.RootElement;

                //        if (root.TryGetProperty(
                //            "message", out JsonElement message) &&
                //            message.TryGetProperty(
                //            "content", out JsonElement content))
                //        {
                //            string chunk = CleanTokenText(
                //                content.GetString() ?? "");

                //            if (!string.IsNullOrEmpty(chunk))
                //            {
                //                pendingBuffer.Append(chunk);
                //            }
                //        }

                //        if (root.TryGetProperty(
                //            "done", out JsonElement done))
                //        {
                //            isCompleted = done.GetBoolean();
                //        }
                //    }
                //    catch (JsonException)
                //    {
                //        continue;
                //    }

                //    // Update UI only once every 50 milliseconds.
                //    if (pendingBuffer.Length > 0 &&
                //        updateTimer.ElapsedMilliseconds >=
                //        UiUpdateIntervalMs)
                //    {
                //        string newText = pendingBuffer.ToString();

                //        pendingBuffer.Clear();

                //        displayBuffer.Append(newText);

                //        updateTimer.Restart();

                //        await Dispatcher.InvokeAsync(() =>
                //        {
                //            if (bodyRun != null)
                //            {
                //                // Replace complete displayed text.
                //                // Avoid repeated string concatenation.
                //                bodyRun.Text = displayBuffer.ToString();
                //            }
                //        });
                //    }
                //}

                //// Flush remaining tokens.
                //if (pendingBuffer.Length > 0)
                //{
                //    displayBuffer.Append(pendingBuffer.ToString());

                //    pendingBuffer.Clear();
                //}

                //// Final UI update.
                //// Final formatted answer rendering.
                //await Dispatcher.InvokeAsync(() =>
                //{
                //    _lastAnswer = displayBuffer.ToString();

                //    RenderFormattedAnswer(_lastAnswer);

                //    TxtAnswer.FontSize = _answerFontSize;

                //    AnswerScroll?.ScrollToHome();
                //});

                //SetStatus("Ready 🎙 (Interview Assistant Active)");

                // Prepare answer display on UI thread.
                StringBuilder displayBuffer = new StringBuilder();

                if (isSelfIntro)
                {
                    displayBuffer.Append(mandatoryIntro);
                }

                _lastAnswer = displayBuffer.ToString();

                // Initial UI rendering
                await Dispatcher.InvokeAsync(() =>
                {
                    if (TxtAnswer == null)
                        return;

                    RenderFormattedAnswer(displayBuffer.ToString());

                    TxtAnswer.FontSize = _answerFontSize;

                    // Always start from the first line.
                    AnswerScroll?.ScrollToHome();
                });

                SetStatus("⚡ AI Answer Generating...");

                var payload = new
                {
                    model = PrimaryAiModel,

                    messages = new object[]
                    {
        new
        {
            role = "system",
            content = expertSystemPrompt
        },
        new
        {
            role = "user",
            content = questionPrompt
        }
                    },

                    stream = true,

                    keep_alive = -1,

                    options = new
                    {
                        num_predict = 800,
                        temperature = 0.2,
                        top_p = 0.9,
                        repeat_penalty = 1.05,
                        num_ctx = 4096
                    }
                };

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    "api/chat");

                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                using var response = await OllamaHttp.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    string error =
                        await response.Content.ReadAsStringAsync();

                    SetAnswerText(
                        "AI Error: " + response.StatusCode + "\n" + error);

                    SetStatus("Ready 🎙");
                    return;
                }

                using var responseStream =
                    await response.Content.ReadAsStreamAsync();

                using var reader =
                    new StreamReader(responseStream, Encoding.UTF8);

                // Collect tokens before updating the UI.
                StringBuilder pendingBuffer = new StringBuilder();

                Stopwatch updateTimer = Stopwatch.StartNew();

                const int UiUpdateIntervalMs = 100;

                bool isCompleted = false;

                while (!isCompleted)
                {
                    string? line = await reader.ReadLineAsync();

                    if (line == null)
                        break;

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        using JsonDocument json =
                            JsonDocument.Parse(line);

                        JsonElement root = json.RootElement;

                        if (root.TryGetProperty(
                            "message", out JsonElement message) &&
                            message.TryGetProperty(
                            "content", out JsonElement content))
                        {
                            string chunk = CleanTokenText(
                                content.GetString() ?? "");

                            if (!string.IsNullOrEmpty(chunk))
                            {
                                pendingBuffer.Append(chunk);
                            }
                        }

                        if (root.TryGetProperty(
                            "done", out JsonElement done))
                        {
                            isCompleted = done.GetBoolean();
                        }
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    // Update formatted UI periodically.
                    if (pendingBuffer.Length > 0 &&
                        updateTimer.ElapsedMilliseconds >=
                        UiUpdateIntervalMs)
                    {
                        string newText = pendingBuffer.ToString();

                        pendingBuffer.Clear();

                        displayBuffer.Append(newText);

                        updateTimer.Restart();

                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (TxtAnswer != null)
                            {
                                _lastAnswer = displayBuffer.ToString();

                                // Render formatted text during streaming.
                                RenderFormattedAnswer(_lastAnswer);

                                TxtAnswer.FontSize = _answerFontSize;
                            }
                        });
                    }
                }

                // Flush remaining tokens.
                if (pendingBuffer.Length > 0)
                {
                    displayBuffer.Append(pendingBuffer.ToString());

                    pendingBuffer.Clear();
                }

                // Final UI update.
                await Dispatcher.InvokeAsync(() =>
                {
                    _lastAnswer = displayBuffer.ToString();

                    RenderFormattedAnswer(_lastAnswer);

                    TxtAnswer.FontSize = _answerFontSize;

                    AnswerScroll?.ScrollToHome();
                });

                SetStatus("Ready 🎙 (Interview Assistant Active)");

            }
            catch (Exception ex)
            {
                SetAnswerText("Exception: " + ex.Message);
                SetStatus("Ready 🎙");
            }
            finally
            {
                // Always release generation lock.
                Interlocked.Exchange(ref _isGeneratingAnswer, 0);
            }
        }

        private async Task StartInterviewSessionAsync()
        {
            string firstQuestion = "Tell me about yourself.";

            SetQuestionText(firstQuestion);

            string prompt = BuildInterviewPrompt(firstQuestion);

            SetStatus("⚡ Starting .NET Full Stack Interview...");

            await StreamAiAnswerRealtimeAsync(prompt);
        }
        // 🔍 प्रत्यक्ष रेझ्युमेच्या टेक्स्टमधून अचूक नाव शोधणारी स्मार्ट मेथड (डिफॉल्ट नाव काढून टाकले आहे)

        private string ExtractNameFromResume(string resumeText)
        {
            if (string.IsNullOrWhiteSpace(resumeText) ||
                resumeText.Contains("No resume uploaded",
                StringComparison.OrdinalIgnoreCase))
            {
                return "Candidate";
            }

            try
            {
                string[] lines = resumeText.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                // 1. Search name in first 15 lines
                foreach (var line in lines.Take(15))
                {
                    string cleanLine = line.Trim();

                    if (cleanLine.Contains("My name is",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        int index = cleanLine.IndexOf(
                            "My name is",
                            StringComparison.OrdinalIgnoreCase) + 10;

                        string extracted = cleanLine.Substring(index)
                            .Trim().TrimEnd('.', ',');

                        if (!string.IsNullOrWhiteSpace(extracted))
                        {
                            string[] nameParts = extracted.Split(
                                new[] { ' ' },
                                StringSplitOptions.RemoveEmptyEntries);

                            if (nameParts.Length >= 2)
                                return $"{nameParts[0]} {nameParts[1]}";

                            return nameParts[0];
                        }
                    }

                    if (cleanLine.StartsWith("Full Name:",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        string name = cleanLine.Substring(10).Trim();

                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }

                    if (cleanLine.StartsWith("Name:",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        string name = cleanLine.Substring(5).Trim();

                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }

                // 2. Search name in first 3 lines
                foreach (var line in lines.Take(3))
                {
                    string cleanLine = line.Trim();

                    if (cleanLine.Length >= 3 &&
                        cleanLine.Length <= 30 &&
                        !cleanLine.Contains("@") &&
                        !cleanLine.Contains(".com",
                            StringComparison.OrdinalIgnoreCase) &&
                        !cleanLine.Contains("Resume",
                            StringComparison.OrdinalIgnoreCase) &&
                        !cleanLine.Contains("Developer",
                            StringComparison.OrdinalIgnoreCase) &&
                        !cleanLine.Any(char.IsDigit))
                    {
                        return cleanLine;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Name extraction error: " + ex.Message);
            }

            // 3. Fallback
            try
            {
                string[] allLines = resumeText.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

                if (allLines.Length > 0)
                {
                    string firstWord = allLines[0]
                        .Trim()
                        .Split(' ')[0];

                    if (firstWord.Length > 2)
                        return firstWord;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Fallback error: " + ex.Message);
            }

            return "Candidate";
        }

        private string ExtractExperienceFromResume(string resumeText)
        {
            if (string.IsNullOrWhiteSpace(resumeText) ||
                resumeText.Contains("No resume uploaded",
                StringComparison.OrdinalIgnoreCase))
                return "Not specified in resume";

            try
            {
                string pattern =
                    @"(?i)\b(\d+(?:\.\d+)?)\s*\+?\s*" +
                    @"(years?|yrs?)\s*(?:of\s*)?" +
                    @"(?:professional\s*)?(?:work\s*)?" +
                    @"(?:experience|exp)\b";

                Match match = Regex.Match(resumeText, pattern);

                if (match.Success)
                {
                    return match.Groups[1].Value + " " +
                           match.Groups[2].Value + " of experience";
                }

                pattern =
                    @"(?i)\bexperience\s*[:\-]\s*" +
                    @"(\d+(?:\.\d+)?)\s*\+?\s*(years?|yrs?)\b";

                match = Regex.Match(resumeText, pattern);

                if (match.Success)
                {
                    return match.Groups[1].Value + " " +
                           match.Groups[2].Value + " of experience";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Experience Error: " + ex.Message);
            }

            return "Not specified in resume";
        }



        private void AudioToggle_Click(object sender, RoutedEventArgs e) => ToggleContinuousListening();
        private void Screenshot_Click(object sender, RoutedEventArgs e) => _ = TriggerScreenshotAnalysisAsync();

        private void Answer_Click(object sender, RoutedEventArgs e)
        {
            string currentText = TxtLiveAudio?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(currentText) || currentText.Contains("Press F5"))
            {
                _ = TriggerScreenshotAnalysisAsync();
            }
            else
            {
                string finalPrompt = BuildInterviewPrompt(currentText);

                SetStatus("Getting AI Answer...");

                _ = StreamAiAnswerRealtimeAsync(finalPrompt);
            }
        }

        //private void Chat_Click(object sender, RoutedEventArgs e)
        //{
        //    TxtLiveAudio?.Focus();
        //    SetStatus("Type your question in the box above.");
        //}
        //private void Chat_Click(object sender, RoutedEventArgs e)
        //{
        //    // Clear input box
        //    if (TxtLiveAudio != null)
        //    {
        //        TxtLiveAudio.Text = "";
        //        TxtLiveAudio.Focus();
        //        TxtLiveAudio.CaretIndex = 0;
        //    }

        //    // Clear previous question
        //    if (TxtQuestion != null)
        //    {
        //        TxtQuestion.Inlines.Clear();

        //        TxtQuestion.Inlines.Add(
        //            new System.Windows.Documents.Run("💬 ")
        //            {
        //                Foreground = (SolidColorBrush)
        //                new BrushConverter().ConvertFrom("#00B4D8")!
        //            });

        //        TxtQuestion.Inlines.Add(
        //            new System.Windows.Documents.Run("Question: ")
        //            {
        //                FontWeight = FontWeights.Bold,
        //                Foreground = Brushes.White
        //            });

        //        TxtQuestion.Inlines.Add(
        //            new System.Windows.Documents.Run("Type your new question...")
        //            {
        //                Foreground = Brushes.Gray
        //            });
        //    }

        //    // Clear previous answer
        //    if (TxtAnswer != null)
        //    {
        //        TxtAnswer.Inlines.Clear();

        //        TxtAnswer.Inlines.Add(
        //            new System.Windows.Documents.Run("⭐ ")
        //            {
        //                Foreground = Brushes.Gold
        //            });

        //        TxtAnswer.Inlines.Add(
        //            new System.Windows.Documents.Run("Answer: ")
        //            {
        //                FontWeight = FontWeights.Bold,
        //                Foreground = Brushes.White
        //            });

        //        TxtAnswer.Inlines.Add(
        //            new System.Windows.Documents.Run("Waiting for your new question...")
        //            {
        //                Foreground = Brushes.Gray
        //            });
        //    }

        //    _lastAnswer = "";

        //    SetStatus("Chat Ready - Type your new question.");
        //}
        private void Chat_Click(object sender, RoutedEventArgs e)
        {
            if (TxtLiveAudio != null)
            {
                TxtLiveAudio.Clear();
                TxtLiveAudio.Focus();
            }

            if (TxtQuestion != null)
            {
                TxtQuestion.Inlines.Clear();
                TxtQuestion.Text = "💬 Question: Type your new question...";
            }

            if (TxtAnswer != null)
            {
                TxtAnswer.Inlines.Clear();
                TxtAnswer.Text = "⭐ Answer: Waiting for your question...";
            }

            _lastAnswer = "";

            SetStatus("Chat Ready - Type your question.");
        }

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            _boldEnabled = !_boldEnabled;
            if (!string.IsNullOrEmpty(_lastAnswer))
            {
                SetAnswerText(_lastAnswer);
            }
        }

        private void ClearLive_Click(object sender, RoutedEventArgs e)
        {
            SetLiveText("");
            if (TxtQuestion != null)
            {
                TxtQuestion.Inlines.Clear();
                TxtQuestion.Inlines.Add(new System.Windows.Documents.Run("💬 ") { Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#00B4D8")! });
                TxtQuestion.Inlines.Add(new System.Windows.Documents.Run("Question: ") { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                TxtQuestion.Inlines.Add(new System.Windows.Documents.Run("Ready for meeting audio capture (Press F5)."));
            }
            if (TxtAnswer != null)
            {
                TxtAnswer.Inlines.Clear();
                TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("⭐ ") { Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom("#FFD700")! });
                TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("Answer: ") { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                TxtAnswer.Inlines.Add(new System.Windows.Documents.Run("AI answers will stream here instantly word by word."));
            }
            _lastAnswer = "";
            SetStatus("Cleared.");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CopyAnswer_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastAnswer))
            {
                Clipboard.SetText(_lastAnswer);
                SetStatus("Answer copied to clipboard!");
            }
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            double newWidth = Math.Max(MinWindowWidth, Width - WindowSizeStep);
            double newHeight = Math.Max(MinWindowHeight, Height - WindowSizeStep);

            if (newWidth != Width || newHeight != Height)
            {
                Left += (Width - newWidth) / 2;
                Top += (Height - newHeight) / 2;
                Width = newWidth;
                Height = newHeight;
            }
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            double newWidth = Math.Min(MaxWindowWidth, Width + WindowSizeStep);
            double newHeight = Math.Min(MaxWindowHeight, Height + WindowSizeStep);

            if (newWidth != Width || newHeight != Height)
            {
                Left -= (newWidth - Width) / 2;
                Top -= (newHeight - Height) / 2;
                Width = newWidth;
                Height = newHeight;
            }
        }

        private void DarknessDown_Click(object sender, RoutedEventArgs e)
        {
            if (_panelOpacityLevel > 0.4)
            {
                _panelOpacityLevel -= 0.1;
                if (AnswerPanel != null) AnswerPanel.Opacity = _panelOpacityLevel;
                SetStatus($"Panel Opacity: {Math.Round(_panelOpacityLevel, 1)}");
            }
        }

        private void DarknessUp_Click(object sender, RoutedEventArgs e)
        {
            if (_panelOpacityLevel < 1.0)
            {
                _panelOpacityLevel += 0.1;
                if (AnswerPanel != null) AnswerPanel.Opacity = _panelOpacityLevel;
                SetStatus($"Panel Opacity: {Math.Round(_panelOpacityLevel, 1)}");
            }
        }

        //private void FontDown_Click(object sender, RoutedEventArgs e) => SetStatus("Font Down clicked.");
        //private void FontUp_Click(object sender, RoutedEventArgs e) => SetStatus("Font Up clicked.");
        private void FontUp_Click(object sender, RoutedEventArgs e)
        {
            _answerFontSize = Math.Min(
                MaxFontSize,
                _answerFontSize + 1);

            if (!string.IsNullOrEmpty(_lastAnswer))
            {
                SetAnswerText(_lastAnswer);
            }

            SetStatus($"Font Size: {_answerFontSize}");
        }
        private void FontDown_Click(object sender, RoutedEventArgs e)
        {
            _answerFontSize = Math.Max(
                MinFontSize,
                _answerFontSize - 1);

            if (!string.IsNullOrEmpty(_lastAnswer))
            {
                SetAnswerText(_lastAnswer);
            }

            SetStatus($"Font Size: {_answerFontSize}");
        }
        private void FeedbackDown_Click(object sender, RoutedEventArgs e) => SetStatus("Feedback Down clicked.");
        private void FeedbackUp_Click(object sender, RoutedEventArgs e) => SetStatus("Feedback Up clicked.");

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Settings_Click(object sender, RoutedEventArgs e) => SetStatus("Settings clicked.");

        private void TxtLiveAudio_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Answer_Click(sender, e);
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                _ = TriggerScreenshotAnalysisAsync();

                e.Handled = true;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }


        //=============




    }
}