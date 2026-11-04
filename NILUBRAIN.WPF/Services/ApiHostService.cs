
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NILUBRAIN.WPF.Services
{
    public class ApiHostService : IDisposable
    {
        private Process? _apiProcess;

        public async Task<bool> StartAsync(
            string apiExePath,
            string apiUrl)
        {
            if (_apiProcess != null &&
                !_apiProcess.HasExited)
            {
                return true;
            }

            if (!File.Exists(apiExePath))
            {
                throw new FileNotFoundException(
                    "NILUBRAIN.API executable not found.",
                    apiExePath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = apiExePath,
                WorkingDirectory =
                    Path.GetDirectoryName(apiExePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(apiUrl);

            _apiProcess = new Process
            {
                StartInfo = startInfo
            };

            _apiProcess.Start();

            // API Ready आहे का ते तपासण्यासाठी
            using var client = new System.Net.Http.HttpClient();

            for (int i = 0; i < 30; i++)
            {
                if (_apiProcess.HasExited)
                    return false;

                try
                {
                    var response = await client.GetAsync(
                        apiUrl);

                    if ((int)response.StatusCode < 500)
                        return true;
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }

            return false;
        }

        public void Stop()
        {
            if (_apiProcess != null)
            {
                if (!_apiProcess.HasExited)
                {
                    _apiProcess.Kill(entireProcessTree: true);
                    _apiProcess.WaitForExit(5000);
                }

                _apiProcess.Dispose();
                _apiProcess = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
