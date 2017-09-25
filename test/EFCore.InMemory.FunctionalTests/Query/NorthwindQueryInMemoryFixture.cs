// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore.Query
{
    public class NorthwindQueryInMemoryFixture<TModelCustomizer> : NorthwindQueryFixtureBase<TModelCustomizer>
        where TModelCustomizer : IModelCustomizer, new()
    {
        protected override ITestStoreFactory TestStoreFactory => InMemoryTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder.View<CustomerView>()
                .ToQuery(
                    db => db.Set<Customer>()
                        .Select(
                            c => new CustomerView
                            {
                                Address = c.Address,
                                City = c.City,
                                CompanyName = c.CompanyName,
                                ContactName = c.ContactName,
                                ContactTitle = c.ContactTitle
                            }));

            modelBuilder.View<OrderView>()
                .ToQuery(
                    db => db.Set<Order>()
                        //.Include(o => o.Customer)
                        .Select(
                            o => new OrderView
                            {
                                CustomerID = o.CustomerID,
                                Customer = o.Customer,
                            }));

            modelBuilder.View<ProductView>()
                .ToQuery(
                    db => db.Set<Product>()
                        .Where(p => !p.Discontinued)
                        .Select(
                            p => new ProductView
                            {
                                ProductID = p.ProductID,
                                ProductName = p.ProductName,
                                CategoryName = "Food"
                            }));
        }
    }
}
