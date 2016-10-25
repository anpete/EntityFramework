// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace PerfDriver
{
    public class Program
    {
        private const int Threads = 32;

        private static readonly TimeSpan _duration = TimeSpan.FromSeconds(10);
        private static readonly Stopwatch _stopwatch = new Stopwatch();

        private static long _requests;
        private static long _connections;

        private static int _stopwatchStarted;

        public static void Main(string[] args)
        {
            InitializeDatabase();

#pragma warning disable 4014
            WriteResults();
#pragma warning restore 4014

            Test(TestEf).Wait();
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

        private static async Task TestEf()
        {
            var random = new Random();

            using (var context = new ApplicationDbContext())
            {
                Interlocked.Increment(ref _connections);

                while (true)
                {
                    var id = random.Next(1, 10001);

                    var world = await context.World.FirstAsync(w => w.Id == id);

                    Debug.Assert(world != null);

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

        private static async void WriteResults()
        {
            var lastRequests = (long) 0;
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
        {
            Log(
                $"Connections: {_connections}, Requests: {totalRequests}, RPS: {Math.Round(currentRequests / elapsed.TotalSeconds)}");
        }

        private static void Log(string message)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        private static void InitializeDatabase()
        {
            using (var context = new ApplicationDbContext())
            {
                context.Database.EnsureCreated();

                var world = context.World.Count();
                var fortune = context.Fortune.Count();

                if (world == 0 || fortune == 0)
                {
                    if (world == 0)
                    {
                        var random = new Random();

                        for (var i = 0; i < 10000; i++)
                        {
                            context.World.Add(new World {RandomNumber = random.Next(1, 10001)});
                        }

                        context.SaveChanges();
                    }

                    if (fortune == 0)
                    {
                        context.Fortune.Add(new Fortune {Message = "fortune: No such file or directory"});
                        context.Fortune.Add(new Fortune
                        {
                            Message = "A computer scientist is someone who fixes things that aren't broken."
                        });
                        context.Fortune.Add(new Fortune {Message = "After enough decimal places, nobody gives a damn."});
                        context.Fortune.Add(new Fortune
                        {
                            Message = "A bad random number generator: 1, 1, 1, 1, 1, 4.33e+67, 1, 1, 1"
                        });
                        context.Fortune.Add(new Fortune
                        {
                            Message = "A computer program does what you tell it to do, not what you want it to do."
                        });
                        context.Fortune.Add(new Fortune
                        {
                            Message = "Emacs is a nice operating system, but I prefer UNIX. — Tom Christaensen"
                        });
                        context.Fortune.Add(new Fortune {Message = "Any program that runs right is obsolete."});
                        context.Fortune.Add(new Fortune
                        {
                            Message = "A list is only as strong as its weakest link. — Donald Knuth"
                        });
                        context.Fortune.Add(new Fortune {Message = "Feature: A bug with seniority."});
                        context.Fortune.Add(new Fortune {Message = "Computers make very fast, very accurate mistakes."});
                        context.Fortune.Add(new Fortune
                        {
                            Message =
                                "<script>alert(\"This should not be displayed in a browser alert box.\");</script>"
                        });
                        context.Fortune.Add(new Fortune {Message = "フレームワークのベンチマーク"});

                        context.SaveChanges();
                    }

                    Log("Database successfully seeded!");
                }
            }
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
            => optionsBuilder.UseSqlServer(
            "Data Source=(localdb)\\MSSQLLocalDB;Database=Fortunes;Integrated Security=True;Connect Timeout=30");
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

    [Table("world")]
    public class World
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("randomnumber")]
        public int RandomNumber { get; set; }
    }
}
