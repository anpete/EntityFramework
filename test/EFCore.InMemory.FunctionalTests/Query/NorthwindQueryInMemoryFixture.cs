using System;
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

        protected override Type ContextType => typeof(NorthwindInMemoryContext);

        private class NorthwindInMemoryContext : NorthwindContext
        {
            public NorthwindInMemoryContext(DbContextOptions options)
                : base(options)
            {
            }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);

                modelBuilder.View<CustomerView>()
                    .ToQuery(context => context.Set<Customer>()
                        .Select(c => new CustomerView
                        {
                            Address = c.Address,
                            City = c.City,
                            CompanyName = c.CompanyName,
                            ContactName = c.ContactName,
                            ContactTitle = c.ContactTitle
                        }));


                modelBuilder.View<OrderView>()
                    .ToQuery(context => context.Set<Order>()
                        .Include(o => o.Customer)
                        .Select(o => new OrderView
                        {
                            CustomerID = o.CustomerID,
                            Customer = o.Customer
                        }));

                //modelBuilder.View<ProductSales1997>().ToQuery("Product Sales for 1997");
            }
        }
    }
}