using System;
using System.IO;

namespace NdjsonErrorCollector.Services
{
    class RunDiagnostics
    {
        private readonly string _logFilePath;

        public RunDiagnostics(string stateDirectory)
        {
            Directory.CreateDirectory(stateDirectory);
            _logFilePath = Path.Combine(stateDirectory, "run.log");
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warning(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            var line = $"{DateTime.UtcNow:O} [{level}] {message}";
            Console.WriteLine(line);
            File.AppendAllText(_logFilePath, line + Environment.NewLine);
        }
    }
}
