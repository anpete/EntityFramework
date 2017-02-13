// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Northwind;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class IncludeInMemoryTest : IncludeTestBase<NorthwindQueryInMemoryFixture>
    {
        public IncludeInMemoryTest(NorthwindQueryInMemoryFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestLoggerFactory.TestOutputHelper = testOutputHelper;
        }

        public override void Include_collection_on_inner_group_join_clause_with_filter(bool useString)
        {
            using (var context = CreateContext())
            {
                var customers
                    = (from c in context.Set<Customer>()
                       join o in context.Set<Order>().Select(o => _Include(o, new object[] { o.Customer }))
                       on c.CustomerID equals o.CustomerID into g
                       where c.CustomerID == "ALFKI"
                       select new { c, g })
                    .ToList();
            }

            //base.Include_collection_on_inner_group_join_clause_with_filter(useString);
        }



        private static T _Include<T>(T entity, object[] included)
        {
            return entity;
        }
    }
}
