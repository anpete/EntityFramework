// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming

// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable AccessToDisposedClosure
// ReSharper disable ReturnValueOfPureMethodIsNotUsed
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable UnusedMember.Local
namespace Microsoft.EntityFrameworkCore.Query
{
    public class QueryBugsTest2 : IClassFixture<SqlServerFixture>
    {
        private readonly SqlServerFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;

        public QueryBugsTest2(SqlServerFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
            _fixture.TestSqlLoggerFactory.Clear();
            _fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        #region #8909

        [Fact]
        public void Bug_8909_memory_leak()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ILoggerFactory>(_fixture.TestSqlLoggerFactory);

            services.AddMemoryCache();

            services.AddEntityFrameworkSqlServer()
                .AddDbContext<MyTestContext>((serviceProvider, options) => options
                    .ConfigureWarnings(w => w.Throw(RelationalEventId.QueryClientEvaluationWarning))
                    .UseSqlServer("Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=True;Initial Catalog=Bug_8909"));



            var provider = services.BuildServiceProvider();
            var memoryCache = (MemoryCache)provider.GetRequiredService<IMemoryCache>();
            using (var scope = provider.CreateScope())
            {
                using (var dbContext = scope.ServiceProvider.GetRequiredService<MyTestContext>())
                {
                    //dbContext.Database.EnsureDeleted();
                   // dbContext.Database.EnsureCreated();
                }
            }

            //TestEfProperty(provider);
            
            //    TestContains(provider);

            TestContainsFromExpression(provider);
            
            //  TextExpressionFunc(provider);

            //TestExpressionConst(provider);

            _testOutputHelper.WriteLine("Press enter to exit");
        }

        private void TestEfProperty(IServiceProvider provider)
        {
            Console.WriteLine("EF.Property");
            var baseValue = 400;
            for (var i = 1; i < 6; i++)
            {
                using (var scope = provider.CreateScope())
                {
                    using (var dbContext = scope.ServiceProvider.GetRequiredService<MyTestContext>())
                    {
                        var itemIds = new List<int> { baseValue + i * 1, baseValue + i * 2, baseValue + i * 3, baseValue + i * 4, baseValue + i * 5, baseValue + i * 6 };
                        _testOutputHelper.WriteLine($"\tBefore iteration {i} query cache count {dbContext.CacheCount()}");
                        var items = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(EF.Property<int>(item, "Id"))).ToList();
                        var ex = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(item.Id)).Expression;
                        _testOutputHelper.WriteLine($"\tAfter iteration {i} query cache count {dbContext.CacheCount()}");
                    }
                }
            }
        }

        private void TestContains(IServiceProvider provider)
        {
            _testOutputHelper.WriteLine("List<int>.Contains");
            var baseValue = 100;
            for (var i = 1; i < 6; i++)
            {
                using (var scope = provider.CreateScope())
                {
                    using (var dbContext = scope.ServiceProvider.GetRequiredService<MyTestContext>())
                    {
                        var itemIds = new List<int> { baseValue + i * 1, baseValue + i * 2, baseValue + i * 3, baseValue + i * 4, baseValue + i * 5, baseValue + i * 6 };
                        _testOutputHelper.WriteLine($"\tBefore iteration {i} query cache count {dbContext.CacheCount()}");
                        var items = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(item.Id)).ToList();
                        var ex = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(item.Id)).Expression;
                        _testOutputHelper.WriteLine($"\tAfter iteration {i} query cache count {dbContext.CacheCount()}");
                    }
                }
            }
        }

        private void TestContainsFromExpression(IServiceProvider provider)
        {
            _testOutputHelper.WriteLine("Contains from Expression");

            var baseValue = 500;
            
            for (var i = 1; i < 6; i++)
            {
                using (var scope = provider.CreateScope())
                {
                    using (var dbContext = scope.ServiceProvider.GetRequiredService<MyTestContext>())
                    {
                        var itemIds = new List<int> { baseValue + i * 1, baseValue + i * 2, baseValue + i * 3, baseValue + i * 4, baseValue + i * 5, baseValue + i * 6 };

                        _testOutputHelper.WriteLine($"\tBefore iteration {i} query cache count {dbContext.CacheCount()}");

                        var items = dbContext.Set<MyTable1>().Where(GetWhereFromExpressionContains(itemIds)).ToList();

                        var ex = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(item.Id)).Expression;

                        _testOutputHelper.WriteLine($"\tAfter iteration {i} query cache count {dbContext.CacheCount()}");
                    }
                }
            }
        }

        private static Expression<Func<MyTable1, bool>> GetWhereFromExpressionContains(List<int> itemIds)
        {
            Expression<Func<MyTable1, int>> keyEntityProperty = x => x.Id;

            var contains = Expression.Call(
                Expression.Constant(itemIds),
                typeof(ICollection<int>).GetMethod("Contains", new[] { typeof(int) }),
                keyEntityProperty.Body);

            return Expression.Lambda<Func<MyTable1, bool>>(contains, keyEntityProperty.Parameters);
        }

        private static Expression<Func<MyTable1, bool>> GetWhereConstExpression(List<int> ids)
        {
            Expression<Func<MyTable1, int>> keyEntityProperty = x => x.Id;
            BinaryExpression last = null;
            
            foreach (var key in ids)
            {
                var keyExp = Expression.Equal(keyEntityProperty.Body, Expression.Constant(key));
                if (last == null)
                {
                    last = keyExp;
                }
                else
                {
                    last = Expression.OrElse(last, keyExp);
                }
            }

            var where = Expression.Lambda<Func<MyTable1, bool>>(last, keyEntityProperty.Parameters);
            return where;
        }

        private static Expression<Func<MyTable1, bool>> GetWhereFuncExpression(List<int> ids)
        {
            Expression<Func<MyTable1, int>> keyEntityProperty = x => x.Id;
            BinaryExpression last = null;
            
            foreach (var key in ids)
            {
                var k = key;
                Expression<Func<int>> idLambda = () => k;
                var keyExp = Expression.Equal(keyEntityProperty.Body, idLambda.Body);
                if (last == null)
                {
                    last = keyExp;
                }
                else
                {
                    last = Expression.OrElse(last, keyExp);
                }
            }

            var where = Expression.Lambda<Func<MyTable1, bool>>(last, keyEntityProperty.Parameters);
            return where;
        }

        private void TestExpressionConst(IServiceProvider provider)
            {
                _testOutputHelper.WriteLine("GetWhereConstExpression");
                
                var baseValue = 300;
                
                for (var i = 1; i < 6; i++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        using (var dbContext = scope.ServiceProvider.GetRequiredService<MyTestContext>())
                        {
                            var itemIds = new List<int> { baseValue + i * 1, baseValue + i * 2, baseValue + i * 3, baseValue + i * 4, baseValue + i * 5, baseValue + i * 6 };
                            _testOutputHelper.WriteLine($"\tBefore iteration {i} query cache count {dbContext.CacheCount()}");
                            var items = dbContext.Set<MyTable1>().Where(GetWhereConstExpression(itemIds)).ToList();
                            var ex = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(item.Id)).Expression;
                            _testOutputHelper.WriteLine($"\tAfter iteration {i} query cache count {dbContext.CacheCount()}");
                        }
                    }
                }
            }

            private void TextExpressionFunc(IServiceProvider provider)
            {
                _testOutputHelper.WriteLine("GetWhereFuncExpression");
                
                var baseValue = 200;
                
                for (var i = 1; i < 6; i++)
                {
                    using (var scope = provider.CreateScope())
                    {
                        using (var dbContext = scope.ServiceProvider.GetRequiredService<MyTestContext>())
                        {
                            var itemIds = new List<int> { baseValue + i * 1, baseValue + i * 2, baseValue + i * 3, baseValue + i * 4, baseValue + i * 5, baseValue + i * 6 };
                            _testOutputHelper.WriteLine($"\tAfter itemIds.Contains() query cache count {dbContext.CacheCount()}");
                            var items = dbContext.Set<MyTable1>().Where(GetWhereFuncExpression(itemIds)).ToList();
                            var ex = dbContext.Set<MyTable1>().Where(item => itemIds.Contains(item.Id)).Expression;
                            _testOutputHelper.WriteLine($"\tAfter iteration {i} query cache count {dbContext.CacheCount()}");
                        }
                    }
                }
            }
    

        public class MyTestContext : DbContext
        {
            public MyTestContext(DbContextOptions options)
                : base(options)
            {
            }

            public DbSet<MyTable1> MyTable1 { get; set; }

            public int CacheCount()
            {
                var compiledQueryCache = (CompiledQueryCache)this.GetService<ICompiledQueryCache>();
                return ((MemoryCache)typeof(CompiledQueryCache).GetTypeInfo().GetField("_memoryCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(compiledQueryCache)).Count;
            }
        }

        public class MyTable1
        {
            [Key]
            public int Id { get; set; }
            public string Name { get; set; }
        }

        #endregion
        
        protected void ClearLog()
            => _fixture.TestSqlLoggerFactory.Clear();

        private void AssertSql(params string[] expected)
            => _fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
