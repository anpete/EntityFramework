// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests
{
    public class ViewSqlServerTest : ViewTestBase<NorthwindQuerySqlServerFixture>
    {
        public override void Simple_query()
        {
            base.Simple_query();

            Assert.Equal(
                @"SELECT [c].[CustomerId]
FROM [Customers] AS [c]",
                Sql);
        }

        public override void Simple_from_sql_query()
        {
            base.Simple_from_sql_query();

            Assert.Equal(
                @"select * from customers",
                Sql);
        }

        public override void Composed_from_sql_query()
        {
            base.Composed_from_sql_query();

            Assert.Equal(
                @"SELECT [c].[CustomerId]
FROM (
    select * from customers
) AS [c]
WHERE [c].[CustomerId] = N'ALFKI'",
                Sql);
        }

        public ViewSqlServerTest(NorthwindQuerySqlServerFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }

        private const string FileLineEnding = @"
";

        private static string Sql => TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}
