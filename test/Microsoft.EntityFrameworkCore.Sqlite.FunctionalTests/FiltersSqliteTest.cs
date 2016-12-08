// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore.Sqlite.FunctionalTests
{
    public class FiltersSqliteTest : FiltersTestBase<NorthwindFiltersQuerySqliteFixture>
    {
        public FiltersSqliteTest(NorthwindFiltersQuerySqliteFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            //TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }

        [ConditionalFact]
        public void FromSql_is_composed()
        {
            using (var context = CreateContext())
            {
                var results = context.Customers.FromSql("select * from Customers").ToList();

                Assert.Equal(7, results.Count);
            }
        }
    }
}
