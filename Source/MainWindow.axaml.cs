using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Source
{
    public partial class MainWindow : Window
    {
        private bool micPaused = false;
        private DispatcherTimer? transcriptTimer;
        private Process? whisperProcess;
        private string lastTranscript = "";
        private string projectRoot;

        public MainWindow()
        {
            InitializeComponent();
            projectRoot = AppDomain.CurrentDomain.BaseDirectory;

            // >>> STEALTH MODE ACTIVATION <<<
            // This calls the helper to hide the window from Zoom/Teams/Screenshots
            this.Opened += (s, e) => StealthHelper.SetStealth(this);
            
            StartWhisperProcess();
            StartTimer();
        }

        // --- 1. START PYTHON (Cross Platform) ---
        private void StartWhisperProcess()
        {
            try
            {
                // Mac uses 'python3', Windows uses 'python'
                string pythonCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "python" : "python3";

                try {
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
                        Process.Start("pkill", "-f whisper_mic_bridge.py");
                } catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    Arguments = "whisper_mic_bridge.py",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                whisperProcess = Process.Start(psi);
                UpdateUiState(false);
            }
            catch (Exception ex) { Console.WriteLine("Error: " + ex.Message); }
        }

        // --- 2. SPACEBAR LOGIC ---
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                if (!micPaused) PauseAndAnswer();
                else ResumeMic();
            }
            base.OnKeyDown(e);
        }

        private async void PauseAndAnswer()
        {
            micPaused = true;
            UpdateUiState(true);
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "pause.flag"), "1");
            
            string question = TranscriptBlock.Text ?? "";
            string resume = ResumeBox.Text ?? "";
            
            // Get Answer
            var statusText = this.FindControl<TextBlock>("StatusText");
            if(statusText != null) statusText.Text = "AI Thinking...";

            string answer = await GenerateAnswer(question, resume);
            
            // Display Answer
            TranscriptBlock.Text = "AI ANSWER:\n\n" + answer;
            
            if(statusText != null) statusText.Text = "Answer Ready (Press Space to Resume)";
        }

        private void ResumeMic()
        {
            micPaused = false;
            UpdateUiState(false);
            TranscriptBlock.Text = "[Listening...]";
            
            File.WriteAllText(Path.Combine(projectRoot, "clear.flag"), "1");
            
            var pauseFile = Path.Combine(projectRoot, "pause.flag");
            if (File.Exists(pauseFile)) File.Delete(pauseFile);
        }

        private void UpdateUiState(bool paused)
        {
            var indicator = this.FindControl<Border>("MicIndicator");
            var statusText = this.FindControl<TextBlock>("StatusText");

            if(indicator != null) indicator.Background = paused ? Brushes.OrangeRed : Brushes.LimeGreen;
            if(statusText != null && !paused) statusText.Text = "Listening...";
        }

        // --- 3. TRANSCRIPT TIMER ---
        private void StartTimer()
        {
            transcriptTimer = new DispatcherTimer();
            transcriptTimer.Interval = TimeSpan.FromMilliseconds(200);
            transcriptTimer.Tick += (s, e) => 
            {
                if (micPaused) return;
                
                string path = Path.Combine(projectRoot, "latest.txt");
                if (File.Exists(path))
                {
                    try {
                        string text = File.ReadAllText(path);
                        if (text != lastTranscript && !string.IsNullOrEmpty(text))
                        {
                            lastTranscript = text;
                            TranscriptBlock.Text = text;
                        }
                    } catch {}
                }
            };
            transcriptTimer.Start();
        }

        // --- 4. API CALL ---
        private async Task<string> GenerateAnswer(string question, string resume)
        {
            try
            {
                string keyPath = Path.Combine(projectRoot, "apikey.txt");
                if (!File.Exists(keyPath)) return "Error: apikey.txt missing. Please create it.";
                string apiKey = (await File.ReadAllTextAsync(keyPath)).Trim();

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    var payload = new {
                        model = "claude-3-sonnet-20240229",
                        messages = new[] { new { role = "user", content = $"Resume: {resume}\n\nQ: {question}\n\nAnswer:" } }
                    };

                    var response = await client.PostAsync(
                        "https://openrouter.ai/api/v1/chat/completions",
                        new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    );

                    string json = await response.Content.ReadAsStringAsync();
                    
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if(doc.RootElement.TryGetProperty("choices", out var choices))
                        {
                            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "No answer";
                        }
                        return "API Error: " + json;
                    }
                }
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }
    }
}