// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests
{
    public class FiltersInheritanceSqlServerTest : FiltersInheritanceTestBase<FiltersInheritanceSqlServerFixture>
    {
        public FiltersInheritanceSqlServerTest(FiltersInheritanceSqlServerFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }

//        public override void Count_query()
//        {
//            base.Count_query();
//
//            Assert.Equal(
//                @"SELECT COUNT(*)
//FROM [Animal] AS [a]
//WHERE [a].[Discriminator] = N'Kiwi'
//AND [c].[CompanyName] LIKE N'B' + N'%' AND (CHARINDEX(N'B', [c].[CompanyName]) = 1)",
//                Sql);
//        }

        private const string FileLineEnding = @"
";

        private static string Sql => TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}
