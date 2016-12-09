// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests
{
    public class FiltersSqlServerTest : FiltersTestBase<NorthwindFiltersQuerySqlServerFixture>
    {
        public FiltersSqlServerTest(NorthwindFiltersQuerySqlServerFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }

        public override void Count_query()
        {
            base.Count_query();

            Assert.Equal(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE [c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)",
                Sql);
        }

        public override void Materialized_query()
        {
            base.Materialized_query();

            Assert.Equal(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)",
                Sql);
        }

        public override void Materialized_query_parameter()
        {
            base.Materialized_query_parameter();
        }

        public override void Projection_query_parameter()
        {
            base.Projection_query_parameter();
        }

        public override void Projection_query()
        {
            base.Projection_query();

            Assert.Equal(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)",
                Sql);
        }

        public override void Include_query()
        {
            base.Include_query();

            Assert.Equal(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)
ORDER BY [c].[CustomerID]

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE EXISTS (
    SELECT 1
    FROM [Customers] AS [c]
    WHERE ([c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)) AND ([o].[CustomerID] = [c].[CustomerID]))
ORDER BY [o].[CustomerID]",
                Sql);
        }

        public override void Included_many_to_one_query()
        {
            base.Included_many_to_one_query();

            Assert.Equal(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate], [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Orders] AS [o]
LEFT JOIN (
    SELECT [c0].*
    FROM [Customers] AS [c0]
    WHERE [c0].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c0].[CompanyName]) = 1)
) AS [c] ON [o].[CustomerID] = [c].[CustomerID]",
                Sql);
        }

        public override void Included_one_to_many_query()
        {
            base.Included_one_to_many_query();
            
            Assert.Equal(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitsInStock]
FROM [Products] AS [p]
ORDER BY [p].[ProductID]

SELECT [o].[OrderID], [o].[ProductID], [o].[Discount], [o].[Quantity], [o].[UnitPrice]
FROM [Order Details] AS [o]
WHERE ([o].[Quantity] > 100) AND EXISTS (
    SELECT 1
    FROM [Products] AS [p]
    WHERE [o].[ProductID] = [p].[ProductID])
ORDER BY [o].[ProductID]",
                Sql);
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
