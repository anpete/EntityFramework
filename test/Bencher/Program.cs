// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bencher
{
    public class Program
    {
        private const int Threads = 32;

        private static readonly TimeSpan _duration = TimeSpan.FromSeconds(10);

        private static int _stopwatchStarted;

        private static readonly Stopwatch _stopwatch = new Stopwatch();

        private static long _requests;

        private static IServiceProvider _serviceProvider;

        public static void Main(string[] args)
        {
            _serviceProvider
                = new ServiceCollection()
                    .AddEntityFrameworkSqlServer()
                    .AddPooledDbContext<ApplicationDbContext>((sp, options) =>
                        {
                            options
                                .UseInternalServiceProvider(sp)
                                .UseSqlServer("Data Source=(localdb)\\MSSQLLocalDB;Database=Fortunes;Integrated Security=True");
                        })
                    .BuildServiceProvider();

            ExecuteEfSingleQuery().Wait();

            WriteResults();

            Test(TestEf).Wait();
        }

        private static async Task TestEf()
        {
            while (true)
            {
                await ExecuteEfSingleQuery();
            }
        }

        private static async Task ExecuteEfSingleQuery()
        {
            using (var serviceScope = _serviceProvider.CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetService<ApplicationDbContext>();
                var efDb = new EfDb(context);

                await efDb.LoadSingleQueryRow();

                Interlocked.Increment(ref _requests);
            }
        }

        #region Other tests

        //        private static async Task TestEf_Fast()
        //        {
        //            using (var context = _serviceProvider.GetService<ApplicationDbContext>())
        //            {
        //                var efDb = new EfDb(context);
        //
        //                while (true)
        //                {
        //                    await efDb.LoadSingleQueryRow();
        //                    //await efDb.LoadMultipleQueriesRows(20);
        //
        //                    Interlocked.Increment(ref _requests);
        //                }
        //            }
        //        }
        //
        //        private static async Task TestRaw()
        //        {
        //            while (true)
        //            {
        //                var rawDb = new RawDb();
        //
        //                await rawDb.LoadSingleQueryRow();
        //                //await rawDb.LoadMultipleQueriesRows(20);
        //
        //                Interlocked.Increment(ref _requests);
        //            }
        //        }

        #endregion

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

        private static async void WriteResults()
        {
            if (Interlocked.Exchange(ref _stopwatchStarted, 1) == 0)
            {
                _stopwatch.Start();
            }

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
            => Log($"Requests: {totalRequests}, RPS: {Math.Round(currentRequests / elapsed.TotalSeconds)}");

        private static void Log(string message)
            => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }

    public static class PoolExtensions
    {
        private static readonly ConcurrentQueue<object> _pool = new ConcurrentQueue<object>();

        public static IServiceCollection AddPooledDbContext<TContext>(
            this IServiceCollection serviceCollection,
            Action<IServiceProvider, DbContextOptionsBuilder> optionsAction,
            ServiceLifetime contextLifetime = ServiceLifetime.Scoped)
            where TContext : DbContext
        {
            serviceCollection
                .AddMemoryCache()
                .AddLogging();

            serviceCollection.TryAddSingleton(p => DbContextOptionsFactory<TContext>(p, optionsAction));
            serviceCollection.AddSingleton<DbContextOptions>(p => p.GetRequiredService<DbContextOptions<TContext>>());

            serviceCollection.TryAdd(
                new ServiceDescriptor(
                    typeof(TContext),
                    CreateInstance<TContext>,
                    contextLifetime));

            return serviceCollection;
        }

        private static TContext CreateInstance<TContext>(IServiceProvider sp) 
            where TContext : DbContext
        {
            object instance;
            if (_pool.TryDequeue(out instance))
            {
                return (TContext)instance;
            }

            var context = ActivatorUtilities.CreateInstance<TContext>(sp);

            Console.WriteLine("Created new instance!");

            var applicationDbContext = context as ApplicationDbContext;

            if (applicationDbContext != null)
            {
                applicationDbContext.OnDispose = () => _pool.Enqueue(context);
            }

            return context;
        }

        private static DbContextOptions<TContext> DbContextOptionsFactory<TContext>(
            IServiceProvider applicationServiceProvider,
            Action<IServiceProvider, DbContextOptionsBuilder> optionsAction)
            where TContext : DbContext
        {
            var builder = new DbContextOptionsBuilder<TContext>(
                    new DbContextOptions<TContext>(new Dictionary<Type, IDbContextOptionsExtension>()))
                .UseMemoryCache(applicationServiceProvider.GetService<IMemoryCache>())
                .UseLoggerFactory(applicationServiceProvider.GetService<ILoggerFactory>());

            optionsAction?.Invoke(applicationServiceProvider, builder);

            return builder.Options;
        }
    }

    public class RawDb
    {
        private const string ConnectionString = "Data Source=(localdb)\\MSSQLLocalDB;Database=Fortunes;Integrated Security=True";

        private readonly Random _random = new Random();

        public async Task<World> LoadSingleQueryRow()
        {
            using (var db = new SqlConnection())
            {
                using (var cmd = CreateReadCommand(db))
                {
                    db.ConnectionString = ConnectionString;

                    await db.OpenAsync();

                    return await ReadSingleRow(cmd);
                }
            }
        }

        private static async Task<World> ReadSingleRow(DbCommand cmd)
        {
            // Prepared statements improve PostgreSQL performance by 10-15%
            cmd.Prepare();

            using (var rdr = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow))
            {
                await rdr.ReadAsync();

                return new World
                {
                    Id = rdr.GetInt32(0),
                    RandomNumber = rdr.GetInt32(1)
                };
            }
        }

        private DbCommand CreateReadCommand(DbConnection connection)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, randomnumber FROM world WHERE id = @Id";
            var id = cmd.CreateParameter();
            id.ParameterName = "@Id";
            id.DbType = DbType.Int32;
            id.Value = _random.Next(1, 10001);
            cmd.Parameters.Add(id);

            return cmd;
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var result = new World[count];

            using (var db = new SqlConnection())
            {
                using (var cmd = CreateReadCommand(db))
                {
                    db.ConnectionString = ConnectionString;

                    await db.OpenAsync();

                    for (var i = 0; i < count; i++)
                    {
                        result[i] = await ReadSingleRow(cmd);

                        cmd.Parameters["@Id"].Value = _random.Next(1, 10001);
                    }
                }
            }

            return result;
        }
    }

    public class EfDb
    {
        private readonly Random _random = new Random();

        private readonly ApplicationDbContext _context;

        public EfDb(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<World> LoadSingleQueryRow()
        {
            var id = _random.Next(1, 10001);

            return _context.World.FirstAsync(w => w.Id == id);
        }

        public async Task<World[]> LoadMultipleQueriesRows(int count)
        {
            var result = new World[count];

            //            using (var context = new ApplicationDbContext())
            //            {
            //                for (var i = 0; i < count; i++)
            //                {
            //                    var id = _random.Next(1, 10001);
            //
            //                    result[i] = await context.World.FirstAsync(w => w.Id == id);
            //                }
            //            }

            return result;
        }

        //public void Dispose() => _context.Dispose();
    }

    public sealed class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions options)
            : base(options)
        {
            Database.AutoTransactionsEnabled = false;
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        }

        //        public ApplicationDbContext()
        //        {
        //            Database.AutoTransactionsEnabled = false;
        //            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        //        }

        public DbSet<World> World { get; set; }
        public DbSet<Fortune> Fortune { get; set; }

        public Action OnDispose;

        public override void Dispose()
        {
            OnDispose();
        }

        //        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        //            => optionsBuilder.UseSqlServer("Data Source=(localdb)\\MSSQLLocalDB;Database=Fortunes;Integrated Security=True");
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
