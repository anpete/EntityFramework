// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Microbenchmarks.Core;
using Microsoft.EntityFrameworkCore.Microbenchmarks.Models.Orders;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Microbenchmarks.Query
{
    // Test class for manual profiling work.
    public class Profile
    {
        private const int OperationsPerThread = 100;

        private static readonly string _connectionString
            = $@"{BenchmarkConfig.Instance.BenchmarkDatabase}Database=Perf_Query_Simple;";

        [Fact]
        public async Task Run()
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
        }

        private static Task CreateWork(ConcurrentQueue<Task> queue)
        {
            return Task.Run((Action)(() =>
                {
                    while (true)
                    {
                        if (queue.Count < 5)
                        {
                            queue.Enqueue(DoWork());
                        }
                    }
                }));
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
                        break;
                }
            }
        }

        private static async Task DoWork()
        {
            using (var context = new OrdersContext(_connectionString))
            {
                for (var j = 0; j < 1; j++)
                {
                    var products = await context.Products.Select(p => p.ProductId).ToListAsync();

                    Assert.Equal(1000, products.Count);
                }
            }
        }
    }
}
