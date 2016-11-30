// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Specification.Tests
{
    public abstract class FiltersTestBase<TFixture> : IClassFixture<TFixture>
        where TFixture : NorthwindQueryFixtureBase, new()
    {
        [ConditionalFact]
        public virtual void Count_query()
        {
            using (var context = CreateContext())
            {
                Assert.Equal(7, context.Customers.Count());
            }
        }

        [ConditionalFact]
        public virtual void Include_query()
        {
            using (var context = CreateContext())
            {
                var results = context.Customers.Include(c => c.Orders).ToList();

                Assert.Equal(7, results.Count);
            }
        }

        protected NorthwindContext CreateContext() => Fixture.CreateContext();

        protected FiltersTestBase(TFixture fixture)
        {
            Fixture = fixture;
        }

        protected TFixture Fixture { get; }
    }
}
