// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;
using Xunit;

// ReSharper disable ConvertToExpressionBodyWhenPossible
// ReSharper disable ConvertMethodToExpressionBody
// ReSharper disable StringStartsWithIsCultureSpecific
namespace Microsoft.EntityFrameworkCore.Specification.Tests
{
    public abstract class FiltersTestBase<TFixture> : IClassFixture<TFixture>, IDisposable
        where TFixture : NorthwindQueryFixtureBase, new()
    {
        [ConditionalFact]
        public virtual void Count_query()
        {
            Assert.Equal(7, _context.Customers.Count());
        }

        [ConditionalFact]
        public virtual void Materialized_query()
        {
            Assert.Equal(7, _context.Customers.ToList().Count);
        }

        [ConditionalFact]
        public virtual void Projection_query()
        {
            Assert.Equal(7, _context.Customers.Select(c => c.CustomerID).ToList().Count);
        }

        [ConditionalFact]
        public virtual void Unbound_expression_error()
        {
            Assert.Throws<InvalidOperationException>(() => _context.Employees.ToList());
        }

        [ConditionalFact]
        public virtual void Client_eval_error()
        {
            Assert.Throws<InvalidOperationException>(() => _context.Set<BadFilter>().ToList());
        }

        [ConditionalFact]
        public virtual void Include_query()
        {
            var results = _context.Customers.Include(c => c.Orders).ToList();

            Assert.Equal(7, results.Count);
        }

        [ConditionalFact]
        public virtual void Included_many_to_one_query()
        {
            var results = _context.Orders.Include(o => o.Customer).ToList();

            Assert.Equal(830, results.Count);
            Assert.True(results.All(o => o.Customer == null || o.CustomerID.StartsWith("B")));
        }

        [ConditionalFact]
        public virtual void Included_one_to_many_query()
        {
            var results = _context.Products.Include(p => p.OrderDetails).ToList();

            Assert.Equal(77, results.Count);
            Assert.True(results.All(p => !p.OrderDetails.Any() || p.OrderDetails.All(od => od.Quantity > 100)));
        }

        public static void ConfigureModel(ModelBuilder modelBuilder)
        {
            Expression<Func<Customer, bool>> customerFilter = c => c.CompanyName.StartsWith("B");

            modelBuilder.Entity<Customer>().Metadata.Filter = customerFilter;

            Expression<Func<OrderDetail, bool>> orderDetailFilter = od => od.Quantity > 100;

            modelBuilder.Entity<OrderDetail>().Metadata.Filter = orderDetailFilter;

            Expression<Func<Employee, bool>> employeeFilter = e => e.Address.StartsWith("A");

            modelBuilder.Entity<Employee>().Metadata.Filter = employeeFilter;

            Expression<Func<BadFilter, bool>> badFilter = b => b.ClientMethod();

            modelBuilder.Entity<BadFilter>().Metadata.Filter = badFilter;
        }

        public class BadFilter
        {
            public int Id { get; set; }

            public bool ClientMethod() => false;
        }

        private readonly TFixture _fixture;
        private readonly NorthwindContext _context;

        protected FiltersTestBase(TFixture fixture)
        {
            _fixture = fixture;
            _context = _fixture.CreateContext();
        }

        protected NorthwindContext CreateContext() => _fixture.CreateContext();

        public void Dispose() => _context.Dispose();
    }
}
