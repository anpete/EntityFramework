// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class ComplexNavigationsQueryInMemoryTest : ComplexNavigationsQueryTestBase<InMemoryTestStore, ComplexNavigationsQueryInMemoryFixture>
    {
        public ComplexNavigationsQueryInMemoryTest(ComplexNavigationsQueryInMemoryFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestLoggerFactory.TestOutputHelper = testOutputHelper;
        }

        [Fact]
        public void Test()
        {
            using (var context = CreateContext())
            {
                var query = context.LevelOne
                    .Include(e => e.OneToOne_Required_FK).ThenInclude(e => e.OneToMany_Optional)
                    //.Include(e => e.OneToOne_Required_FK).ThenInclude(e => e.OneToMany_Required)
                    //.OrderBy(t => t.Name)
                    .Skip(0)
                    //.Take(10)
                    ;

                var result = query.ToList();

            }
        }
    }
}
