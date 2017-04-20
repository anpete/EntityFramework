// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests
{
    public class GearsOfWarFromSqlQuerySqlServerTest : GearsOfWarFromSqlQueryTestBase<SqlServerTestStore, GearsOfWarQuerySqlServerFixture>
    {
        private readonly GearsOfWarQuerySqlServerFixture _fixture;

        public GearsOfWarFromSqlQuerySqlServerTest(GearsOfWarQuerySqlServerFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            _fixture = fixture;
            _fixture.TestSqlLoggerFactory.Clear();
        }

        public override void From_sql_queryable_simple_columns_out_of_order()
        {
            base.From_sql_queryable_simple_columns_out_of_order();

            Assert.Equal(
                @"SELECT ""Id"", ""Name"", ""IsAutomatic"", ""AmmunitionType"", ""OwnerFullName"", ""SynergyWithId"" FROM ""Weapon"" ORDER BY ""Name""",
                Sql);
        }

        protected override void ClearLog()
            => _fixture.TestSqlLoggerFactory.Clear();

        private string Sql => _fixture.TestSqlLoggerFactory.Sql;
    }
}
