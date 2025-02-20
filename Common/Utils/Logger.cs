﻿using System;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace PSXPrev.Common.Utils
{
    public class Logger : IDisposable
    {
        private StreamWriter _writer;

        private bool _logToFile;
        public bool LogToFile
        {
            get => _logToFile;
            set
            {
                _logToFile = value;
                if (_logToFile && _writer == null)
                {
                    Open(); // No log file is open yet
                }
            }
        }
        public bool LogToConsole { get; set; }
        public bool UseConsoleColor { get; set; }

        // Default colors are now assigned in Settings.
        public ConsoleColor StandardColor { get; set; }
        public ConsoleColor PositiveColor { get; set; }
        public ConsoleColor WarningColor { get; set; }
        public ConsoleColor ErrorColor { get; set; }
        public ConsoleColor ExceptionPrefixColor { get; set; }

        public Logger(bool logToFile = false, bool logToConsole = true)
        {
            LogToFile = logToFile;
            LogToConsole = logToConsole;
            ReadSettings(Settings.Defaults);
        }

        ~Logger()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
            Close();
        }

        public void Flush()
        {
            if (_logToFile && _writer != null)
            {
                _writer.Flush();
            }
        }

        public void ReadSettings(Settings settings)
        {
            StandardColor = settings.LogStandardColor;
            PositiveColor = settings.LogPositiveColor;
            WarningColor = settings.LogWarningColor;
            ErrorColor = settings.LogErrorColor;
            ExceptionPrefixColor = settings.LogExceptionPrefixColor;
            UseConsoleColor = settings.LogUseConsoleColor;
        }

        public void WriteSettings(Settings settings)
        {
            settings.LogStandardColor = StandardColor;
            settings.LogPositiveColor = PositiveColor;
            settings.LogWarningColor = WarningColor;
            settings.LogErrorColor = ErrorColor;
            settings.LogExceptionPrefixColor = ExceptionPrefixColor;
            settings.LogUseConsoleColor = UseConsoleColor;
        }

        private void Open()
        {
            if (_writer == null)
            {
                var time = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.fffffff", CultureInfo.InvariantCulture);
                _writer = new StreamWriter(Path.Combine(Application.StartupPath, $"{time}.log"));
            }
        }

        private void Close()
        {
            if (_writer != null)
            {
                try
                {
                    _writer.Flush();
                }
                finally
                {
                    _writer.Dispose();
                    _writer = null;
                    _logToFile = false;
                }
            }
        }


        private void WriteInternal(ConsoleColor? color, bool newLine, string text)
        {
            if (text == null)
            {
                text = string.Empty;
            }
            if (LogToConsole)
            {
                if (UseConsoleColor && color.HasValue)
                {
                    Console.ForegroundColor = color.Value;
                }
                else
                {
                    Console.ResetColor();
                }
                // Write whole message to WriteLine instead of appending WriteLine(), because it'll be faster.
                if (newLine)
                {
                    Console.WriteLine(text);
                }
                else
                {
                    Console.Write(text);
                }
            }
            if (_logToFile && _writer != null)
            {
                if (newLine)
                {
                    _writer.WriteLine(text);
                }
                else
                {
                    _writer.Write(text);
                }
            }
        }


        public void WriteColor(ConsoleColor color, string format, params object[] args)
        {
            WriteInternal(color, false, string.Format(format, args));
        }
        public void WriteColor(ConsoleColor color, object value)
        {
            WriteInternal(color, false, value?.ToString());
        }
        public void WriteColor(ConsoleColor color, string text)
        {
            WriteInternal(color, false, text);
        }

        public void WriteColorLine(ConsoleColor color, string format, params object[] args)
        {
            WriteInternal(color, true, string.Format(format, args));
        }
        public void WriteColorLine(ConsoleColor color, object value)
        {
            WriteInternal(color, true, value?.ToString());
        }
        public void WriteColorLine(ConsoleColor color, string text)
        {
            WriteInternal(color, true, text);
        }


        public void Write(string format, params object[] args) => WriteColor(StandardColor, format, args);
        public void Write(object value) => WriteColor(StandardColor, value);
        public void Write(string text)  => WriteColor(StandardColor, text);

        public void WriteLine(string format, params object[] args) => WriteColorLine(StandardColor, format, args);
        public void WriteLine(object value) => WriteColorLine(StandardColor, value);
        public void WriteLine(string text)  => WriteColorLine(StandardColor, text);

        public void WriteLine()
        {
            WriteInternal(null, true, string.Empty);
        }


        public void WritePositive(string format, params object[] args) => WriteColor(PositiveColor, format, args);
        public void WritePositive(object value) => WriteColor(PositiveColor, value);
        public void WritePositive(string text)  => WriteColor(PositiveColor, text);

        public void WritePositiveLine(string format, params object[] args) => WriteColorLine(PositiveColor, format, args);
        public void WritePositiveLine(object value) => WriteColorLine(PositiveColor, value);
        public void WritePositiveLine(string text)  => WriteColorLine(PositiveColor, text);


        public void WriteWarning(string format, params object[] args) => WriteColor(WarningColor, format, args);
        public void WriteWarning(object value) => WriteColor(WarningColor, value);
        public void WriteWarning(string text)  => WriteColor(WarningColor, text);

        public void WriteWarningLine(string format, params object[] args) => WriteColorLine(WarningColor, format, args);
        public void WriteWarningLine(object value) => WriteColorLine(WarningColor, value);
        public void WriteWarningLine(string text)  => WriteColorLine(WarningColor, text);


        public void WriteError(string format, params object[] args) => WriteColor(ErrorColor, format, args);
        public void WriteError(object value) => WriteColor(ErrorColor, value);
        public void WriteError(string text)  => WriteColor(ErrorColor, text);

        public void WriteErrorLine(string format, params object[] args) => WriteColorLine(ErrorColor, format, args);
        public void WriteErrorLine(object value) => WriteColorLine(ErrorColor, value);
        public void WriteErrorLine(string text)  => WriteColorLine(ErrorColor, text);


        public void WriteExceptionLine(Exception exp)
        {
            WriteColorLine(ErrorColor, exp);
        }
        public void WriteExceptionLine(Exception exp, string prefixFormat, params object[] args)
        {
            WriteColor(ExceptionPrefixColor, prefixFormat + ": ", args);
            WriteColorLine(ErrorColor, exp);
        }
        public void WriteExceptionLine(Exception exp, object prefixValue)
        {
            WriteExceptionLine(exp, "{0}", prefixValue);
        }
        public void WriteExceptionLine(Exception exp, string prefixText)
        {
            WriteExceptionLine(exp, "{0}", prefixText);
        }
    }
}
