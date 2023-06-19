﻿using Katzebase.PublicLibrary;
using Katzebase.PublicLibrary.Exceptions;
using System.Text;
using static Katzebase.Engine.KbLib.EngineConstants;
using static Katzebase.PublicLibrary.Constants;

namespace Katzebase.Engine.Logging
{
    /// <summary>
    /// Public core class methods for locking, reading, writing and managing tasks related to logging.
    /// </summary>
    public class LogManager
    {
        private readonly Core core;
        private StreamWriter? fileHandle = null;
        private DateTime recycledTime = DateTime.MinValue;

        public LogManager(Core core)
        {
            this.core = core;
            CycleLog();
        }

        public void Write(string message) => Write(new LogEntry(message) { Severity = LogSeverity.Verbose });
        public void Trace(string message) => Write(new LogEntry(message) { Severity = LogSeverity.Trace });
        public void Write(string message, Exception ex) => Write(new LogEntry(message) { Exception = ex, Severity = LogSeverity.Exception });
        public void Write(string message, LogSeverity severity) => Write(new LogEntry(message) { Severity = severity });

        public void Start()
        {
            CycleLog();
        }

        public void Stop()
        {
            Close();
        }

        public void Checkpoint()
        {
            lock (this)
            {
                if (fileHandle != null)
                {
                    try
                    {
                        fileHandle.Flush();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Critical log exception. Failed to checkpoint log: {ex.Message}.");
                        throw;
                    }
                }
            }
        }

        public void Write(LogEntry entry)
        {
            try
            {
                if (entry.Severity == LogSeverity.Trace && core.Settings.WriteTraceData == false)
                {
                    return;
                }

                if (entry.Exception != null)
                {
                    if (typeof(KbExceptionBase).IsAssignableFrom(entry.Exception.GetType()))
                    {
                        entry.Severity = ((KbExceptionBase)entry.Exception).Severity;
                    }
                }

                lock (this)
                {
                    if (entry.Severity == LogSeverity.Warning)
                    {
                        core.Health.Increment(HealthCounterType.Warnings);
                    }
                    else if (entry.Severity == LogSeverity.Exception)
                    {
                        core.Health.Increment(HealthCounterType.Exceptions);
                    }

                    CycleLog();

                    StringBuilder message = new StringBuilder();

                    message.AppendFormat("{0}|{1}|{2}", entry.DateTime.ToShortDateString(), entry.DateTime.ToShortTimeString(), entry.Severity);

                    if (entry.Message != null && entry.Message != string.Empty)
                    {
                        message.Append("|");
                        message.Append(entry.Message);
                    }

                    if (entry.Exception != null)
                    {
                        if (typeof(KbExceptionBase).IsAssignableFrom(entry.Exception.GetType()))
                        {
                            if (entry.Exception.Message != null && entry.Exception.Message != string.Empty)
                            {
                                message.AppendFormat("|Exception: {0}: ", entry.Exception.GetType().Name);
                                message.Append(entry.Exception.Message);
                            }
                        }
                        else
                        {
                            if (entry.Exception.Message != null && entry.Exception.Message != string.Empty)
                            {
                                message.Append("|Exception: ");
                                message.Append(GetExceptionText(entry.Exception));
                            }

                            if (entry.Exception.StackTrace != null && entry.Exception.StackTrace != string.Empty)
                            {
                                message.Append("|Stack: ");
                                message.Append(entry.Exception.StackTrace);
                            }
                        }
                    }

                    if (entry.Severity == LogSeverity.Warning)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                    }
                    else if (entry.Severity == LogSeverity.Exception)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }
                    else if (entry.Severity == LogSeverity.Verbose)
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }

                    Console.WriteLine(message.ToString());

                    Console.ForegroundColor = ConsoleColor.Gray;

                    Utility.EnsureNotNull(fileHandle);

                    fileHandle.WriteLine(message.ToString());

                    if (core.Settings.FlushLog)
                    {
                        fileHandle.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical log exception. Failed write log entry: {ex.Message}.");
                throw;
            }
        }

        private string GetExceptionText(Exception excpetion)
        {
            try
            {
                var message = new StringBuilder();
                return GetExceptionText(excpetion, 0, ref message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical log exception. Failed to get exception text: {ex.Message}.");
                throw;
            }
        }

        private string GetExceptionText(Exception exception, int level, ref StringBuilder message)
        {
            try
            {
                if (exception.Message != null && exception.Message != string.Empty)
                {
                    message.AppendFormat("{0} {1}", level, exception.Message);
                }

                if (exception.InnerException != null && level < 100)
                {
                    return GetExceptionText(exception.InnerException, level + 1, ref message);
                }

                return message.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical log exception. Failed to get exception text: {ex.Message}.");
                throw;
            }
        }

        private void CycleLog()
        {
            try
            {
                lock (this)
                {
                    if (recycledTime.Date != DateTime.Now)
                    {
                        Close();

                        recycledTime = DateTime.Now;
                        string fileName = core.Settings.LogDirectory + "\\" + $"{recycledTime.Year}_{recycledTime.Month:00}_{recycledTime.Day:00}.txt";
                        Directory.CreateDirectory(core.Settings.LogDirectory);
                        fileHandle = new StreamWriter(fileName, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical log exception. Failed to cycle log file: {ex.Message}.");
                throw;
            }
        }

        public void Close()
        {
            try
            {
                if (fileHandle != null)
                {
                    fileHandle.Close();
                    fileHandle.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Critical log exception. Failed to close log file: {ex.Message}.");
                throw;
            }
        }
    }
}
