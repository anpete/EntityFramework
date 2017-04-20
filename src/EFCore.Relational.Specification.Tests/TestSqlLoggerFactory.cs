// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.Internal;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable 618

namespace Microsoft.EntityFrameworkCore.Specification.Tests
{
    public class TestSqlLoggerFactory : ILoggerFactory
    {
        private const string FileLineEnding = @"
";
        private static readonly string _newLine = Environment.NewLine;

        public void AssertBaseline(string[] expected, bool assertOrder = true)
        {
            var sqlStatements
                = _logger.SqlStatements
                    .Select(sql => sql.Replace(Environment.NewLine, FileLineEnding))
                    .ToList();

            try
            {
                if (assertOrder)
                {
                    for (var i = 0; i < expected.Length; i++)
                    {
                        Assert.Equal(expected[i], sqlStatements[i]);
                    }
                }
                else
                {
                    foreach (var expectedFragment in expected)
                    {
                        Assert.Contains(expectedFragment, sqlStatements);
                    }
                }
            }
            catch
            {
                var methodCallLine = Environment.StackTrace.Split(
                        new[] { Environment.NewLine },
                        StringSplitOptions.RemoveEmptyEntries)[4]
                    .Substring(6);

                var testName = methodCallLine.Substring(0, methodCallLine.IndexOf(')') + 1);
                var lineIndex = methodCallLine.LastIndexOf("line", StringComparison.Ordinal);
                var lineNumber = lineIndex > 0 ? methodCallLine.Substring(lineIndex) : "";

                const string indent = FileLineEnding + "                ";

                var currentDirectory = Directory.GetCurrentDirectory();
                var logFile = currentDirectory.Substring(
                                  0,
                                  currentDirectory.LastIndexOf("\\test\\", StringComparison.Ordinal) + 1)
                              + "QueryBaseline.cs";

                var testInfo = $"{testName + " : " + lineNumber}" + FileLineEnding;

                var newBaseLine = $@"            AssertSql(
                {string.Join("," + indent + "//" + indent, sqlStatements.Take(9).Select(sql => "@\"" + sql.Replace("\"", "\"\"") + "\""))});

";

                if (sqlStatements.Count > 9)
                {
                    newBaseLine += "Output truncated.";
                }

                _logger.TestOutputHelper?.WriteLine(newBaseLine);

                var contents = testInfo + newBaseLine + FileLineEnding + FileLineEnding;

                File.AppendAllText(logFile, contents);

                throw;
            }
        }

        private readonly Logger _logger = new Logger();

        public void Clear()
        {
            _logger.Clear();
        }

        public string Log => _logger.LogBuilder.ToString();

        public IReadOnlyList<string> SqlStatements => _logger.SqlStatements;

        public string Sql => string.Join(_newLine + _newLine, SqlStatements);

        public IReadOnlyList<DbCommandLogData> CommandLogData => _logger.LogData;

        public CancellationToken CancelQuery()
        {
            _logger.CancellationTokenSource = new CancellationTokenSource();

            return _logger.CancellationTokenSource.Token;
        }

        public void SetTestOutputHelper(ITestOutputHelper testOutputHelper)
        {
            _logger.TestOutputHelper = testOutputHelper;
        }

        ILogger ILoggerFactory.CreateLogger(string categoryName) => _logger;

        void ILoggerFactory.AddProvider(ILoggerProvider provider) => throw new NotImplementedException();

        void IDisposable.Dispose()
        {
        }

        private sealed class Logger : ILogger
        {
            public List<DbCommandLogData> LogData { get; } = new List<DbCommandLogData>();
            public IndentedStringBuilder LogBuilder { get; } = new IndentedStringBuilder();
            public List<string> SqlStatements { get; } = new List<string>();
            public CancellationTokenSource CancellationTokenSource { get; set; }

            public ITestOutputHelper TestOutputHelper { get; set; }

            public void Clear()
            {
                SqlStatements.Clear();
                LogBuilder.Clear();
                LogData.Clear();
                CancellationTokenSource = null;
            }

            void ILogger.Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                var format = formatter(state, exception)?.Trim();

                if (format != null)
                {
                    if (CancellationTokenSource != null)
                    {
                        CancellationTokenSource.Cancel();
                        CancellationTokenSource = null;
                    }

                    var commandLogData = state as DbCommandLogData;

                    if (commandLogData != null)
                    {
                        var parameters = "";

                        if (commandLogData.Parameters.Any())
                        {
                            parameters
                                = string.Join(
                                      _newLine,
                                      commandLogData.Parameters
                                          .Select(p => $"{p.Name}: {p.FormatParameter(quoteValues: false)}"))
                                  + _newLine + _newLine;
                        }

                        SqlStatements.Add(parameters + commandLogData.CommandText);

                        LogData.Add(commandLogData);
                    }

                    else
                    {
                        LogBuilder.AppendLine(format);
                    }

                    TestOutputHelper?.WriteLine(format + Environment.NewLine);
                }
            }

            bool ILogger.IsEnabled(LogLevel logLevel) => true;
            IDisposable ILogger.BeginScope<TState>(TState state) => LogBuilder.Indent();
        }
    }
}
