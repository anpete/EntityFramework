// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests
{
    public class FiltersSqlServerTest : FiltersTestBase<NorthwindFiltersQuerySqlServerFixture>
    {
        public FiltersSqlServerTest(NorthwindFiltersQuerySqlServerFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }

        public override void Included_many_to_one_query()
        {
            base.Included_many_to_one_query();
        }

        [ConditionalFact]
        public void FromSql_is_composed()
        {
            using (var context = CreateContext())
            {
                var results = context.Customers.FromSql("select * from Customers").ToList();

                Assert.Equal(7, results.Count);
            }

            Assert.Equal(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM (
    select * from Customers
) AS [c]
WHERE [c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)",
                Sql);
        }

        private const string FileLineEnding = @"
";

        private static string Sql => TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}
