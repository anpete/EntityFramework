// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Bencher
{
    public class Program
    {
        private const int Threads = 32;

        private static readonly TimeSpan _duration = TimeSpan.FromSeconds(10);

        private static int _stopwatchStarted;

        private static readonly Stopwatch _stopwatch = new Stopwatch();

        private static long _requests;
        private static long _connections;

        public static void Main(string[] args)
        {
            WriteResults();
            Test(TestEf).Wait();
        }

        private static async Task TestEf()
        {
            while (true)
            {
                using (var context = new ApplicationDbContext())
                {
                    Interlocked.Increment(ref _connections);

                    var efDb = new EfDb(context);

                    await efDb.LoadSingleQueryRow();

                    Interlocked.Increment(ref _requests);

                    EnsureWatchStarted();
                }
            }
        }

        private static void EnsureWatchStarted()
        {
            if (Interlocked.Exchange(ref _stopwatchStarted, 1) == 0)
            {
                _stopwatch.Start();
            }
        }

        private static Task Test(Func<Task> action)
        {
            Log($"Running test for {_duration}...");

            var tasks = new Task[Threads];

            for (var i = 0; i < Threads; i++)
            {
                tasks[i] = action();
            }

            return Task.WhenAll(tasks);
        }

        private static void Log(string message)
            => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");

        private static async void WriteResults()
        {
            var lastRequests = (long)0;
            var lastElapsed = TimeSpan.Zero;

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));

                var currentRequests = _requests - lastRequests;
                lastRequests = _requests;

                var elapsed = _stopwatch.Elapsed;
                var currentElapsed = elapsed - lastElapsed;
                lastElapsed = elapsed;

                WriteResult(_requests, currentRequests, currentElapsed);

                if (elapsed > _duration)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Average RPS: {Math.Round(_requests / elapsed.TotalSeconds)}");
                    Environment.Exit(0);
                }
            }
        }

        private static void WriteResult(long totalRequests, long currentRequests, TimeSpan elapsed)
            => Log($"Connections: {_connections}, Requests: {totalRequests}, RPS: {Math.Round(currentRequests / elapsed.TotalSeconds)}");
    }

    public class EfDb
    {
        private readonly Random _random = new Random();
        private readonly ApplicationDbContext _dbContext;

        public EfDb(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
            _dbContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        public Task<World> LoadSingleQueryRow()
        {
            var id = _random.Next(1, 10001);
            return _dbContext.World.FirstAsync(w => w.Id == id);
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var result = new World[count];

            for (var i = 0; i < count; i++)
            {
                var id = _random.Next(1, 10001);
                result[i] = await _dbContext.World.FirstAsync(w => w.Id == id);
            }

            return result;
        }

        public async Task<IEnumerable<Fortune>> LoadFortunesRows()
        {
            var result = await _dbContext.Fortune.ToListAsync();

            result.Add(new Fortune { Message = "Additional fortune added at request time." });
            result.Sort();

            return result;
        }
    }

    public sealed class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext()
        {
            Database.AutoTransactionsEnabled = false;
        }

        public DbSet<World> World { get; set; }
        public DbSet<Fortune> Fortune { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseSqlServer("Data Source=(localdb)\\MSSQLLocalDB;Database=Fortunes;Integrated Security=True");
    }

    [Table("world")]
    public class World
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("randomnumber")]
        public int RandomNumber { get; set; }
    }

    [Table("fortune")]
    public class Fortune : IComparable<Fortune>, IComparable
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("message")]
        [StringLength(2048)]
        public string Message { get; set; }

        public int CompareTo(object obj)
        {
            var other = obj as Fortune;

            if (other == null)
            {
                throw new ArgumentException($"Object to compare must be of type {nameof(Fortune)}", nameof(obj));
            }

            return CompareTo(other);
        }

        public int CompareTo(Fortune other) => string.Compare(Message, other.Message, StringComparison.Ordinal);
    }
}
