// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Northwind;
using Xunit;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local
// ReSharper disable ClassNeverInstantiated.Local
namespace Microsoft.EntityFrameworkCore.Specification.Tests
{
    public abstract class ViewTestBase<TFixture> : IClassFixture<TFixture>
        where TFixture : NorthwindQueryFixtureBase, new()
    {
        [Fact]
        public virtual void Simple_query()
        {
            using (var context = CreateContext())
            {
                var results = context.View<CustomerViewModel>().ToList();

                Assert.Equal(91, results.Count);

                Assert.Null(context.Model.FindEntityType(typeof(CustomerViewModel)));
            }
        }

        [Fact]
        public virtual void Simple_from_sql_query()
        {
            using (var context = CreateContext())
            {
                var results
                    = context.View<CustomerViewModel>()
                        .FromSql("select * from customers")
                        .ToList();

                Assert.Equal(91, results.Count);
            }
        }

        [Fact]
        public virtual void Composed_from_sql_query()
        {
            using (var context = CreateContext())
            {
                var results
                    = context.View<CustomerViewModel>()
                        .FromSql("select * from customers")
                        .Where(c => c.CustomerId == "ALFKI")
                        .ToList();

                Assert.Equal(1, results.Count);
            }
        }

        [Fact]
        public virtual void AsTracked_query()
        {
            using (var context = CreateContext())
            {
                var results = context.View<CustomerViewModel>().AsTracking().ToList();

                Assert.Equal(91, results.Count);
            }
        }

        // AsNoTracking is no-op
        // OfType
        // Composition
        // Entity composition
        // Include throws
        // FromSQL
        // Data annotations

        [Table("Customers")]
        private class CustomerViewModel
        {
            public string CustomerId { get; set; }
        }

        protected NorthwindContext CreateContext() => Fixture.CreateContext();

        protected ViewTestBase(TFixture fixture)
        {
            Fixture = fixture;
        }

        protected TFixture Fixture { get; }
    }
}
