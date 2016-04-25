// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.dotMemoryUnit;
using JetBrains.dotMemoryUnit.Kernel;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace ConsoleApplication1
{
    public class Program
    {
//        private readonly ITestOutputHelper _testOutputHelper;
        private static bool _done;

        private const string ConnectionString
            = @"Server=(localdb)\mssqllocaldb;Trusted_Connection=True;MultipleActiveResultSets=true;Database=Perf_Query_Simple;";

        private const int OperationsPerThread = 100;

        private static int _total;

        private static void Main()
        {
            dotMemoryApi.CollectAllocations = true;
            dotMemoryApi.GetSnapshot();

            Run().Wait();

            Console.WriteLine("Done...");
        }

        public Program(ITestOutputHelper testOutputHelper)
        {
//            _testOutputHelper = testOutputHelper;

            DotMemoryUnitTestOutput.SetOutputMethod(testOutputHelper.WriteLine);
        }

        [Fact]
        public void Test()
        {
            dotMemoryApi.CollectAllocations = true;
            dotMemoryApi.GetSnapshot();

            Run().Wait();

            Console.WriteLine("Done...");
        }

        public static async Task Run()
        {
            SqlConnection.ClearAllPools();

            var createTasks = new List<Task>();
            var completeTasks = new List<Task>();

            for (var i = 0; i < 10; i++)
            {
                var queue = new ConcurrentQueue<Task>();

                createTasks.Add(CreateWork(queue));
                completeTasks.Add(CompleteWork(queue));
            }

            await Task.WhenAll(completeTasks);

            dotMemoryApi.SaveCollectedData();

            _done = true;
        }

        private static Task CreateWork(ConcurrentQueue<Task> queue)
        {
            return Task.Run(() =>
                {
                    while (!_done)
                    {
                        if (queue.Count < 5)
                        {
                            queue.Enqueue(DoWork());
                        }
                    }
                });
        }

        private static async Task CompleteWork(ConcurrentQueue<Task> queue)
        {
            var count = 0;

            while (true)
            {
                Task task;
                if (queue.TryDequeue(out task))
                {
                    await task;

                    if (++count == OperationsPerThread)
                    {
                        Console.WriteLine("Thread exiting...");

                        break;
                    }
                }
            }
        }

        private static async Task DoWork()
        {
            using (var context = new OrdersContext(ConnectionString))
            {
                for (var j = 0; j < 1; j++)
                {
                    var products = await context.Products.Select(p => p.ProductId).ToListAsync();

                    Debug.Assert(products.Count == 1000);
                }
            }

            if (Interlocked.Increment(ref _total) == 500)
            {
                dotMemoryApi.GetSnapshot();
            }
        }
    }

    public class OrdersContext : DbContext
    {
        private readonly string _connectionString;

        public OrdersContext(string connectionString)
        {
            _connectionString = connectionString;
        }

        public DbSet<Product> Products { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder
                .UseSqlServer(_connectionString);
    }

    public class Product
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SKU { get; set; }
        public decimal Retail { get; set; }
        public decimal CurrentPrice { get; set; }
        public int TargetStockLevel { get; set; }
        public int ActualStockLevel { get; set; }
        public int? ReorderStockLevel { get; set; }
        public int QuantityOnOrder { get; set; }
        public DateTime? NextShipmentExpected { get; set; }
        public bool IsDiscontinued { get; set; }
    }
}
