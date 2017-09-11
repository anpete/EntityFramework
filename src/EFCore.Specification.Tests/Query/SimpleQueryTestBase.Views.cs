// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;

// ReSharper disable InconsistentNaming

namespace Microsoft.EntityFrameworkCore.Query
{
    // ReSharper disable once UnusedTypeParameter
    public abstract partial class SimpleQueryTestBase<TFixture>
    {
        // TODO:
        // - Inheritance, all view types in hierarchy
        // - Outbound navs
        // - InMemory
        // - DbView props on context?
        // - Mixed tracking?
        // - Conventions - can't be principal, key detection?
        // - State manager
        // - Migrations ignores
        // - Query filters

        [ConditionalFact]
        public virtual void View_simple()
        {
            AssertQuery<CustomerView>(cvs => cvs);
        }

        [ConditionalFact]
        public virtual void View_where_simple()
        {
            AssertQuery<CustomerView>(
                cvs => cvs.Where(c => c.City == "London"));
        }

        [ConditionalFact]
        public virtual void View_backed_by_view()
        {
            using (var context = CreateContext())
            {
                var results = context.View<ProductSales1997>().ToArray();

                Assert.Equal(77, results.Length);
            }
        }

        [ConditionalFact]
        public virtual void View_with_nav()
        {
            AssertQuery<OrderView>(ovs => ovs.Where(ov => ov.CustomerID == "ALFKI"));
        }

        [ConditionalFact]
        public virtual void View_with_included_nav()
        {
            AssertIncludeQuery<OrderView>(
                ovs => from ov in ovs.Include(ov => ov.Customer)
                    where ov.CustomerID == "ALFKI"
                    select ov,
                new List<IExpectedInclude> {new ExpectedInclude<OrderView>(ov => ov.Customer, "Customer")},
                entryCount: 1);
        }

        [ConditionalFact]
        public virtual void View_with_included_navs_multi_level()
        {
            AssertIncludeQuery<OrderView>(
                ovs => from ov in ovs.Include(ov => ov.Customer.Orders)
                    where ov.CustomerID == "ALFKI"
                    select ov,
                new List<IExpectedInclude>
                {
                    new ExpectedInclude<OrderView>(ov => ov.Customer, "Customer"),
                    new ExpectedInclude<Customer>(c => c.Orders, "Orders")
                },
                entryCount: 7);
        }

        [ConditionalFact]
        public virtual void Select_Where_Navigation()
        {
            AssertQuery<OrderView>(
                ovs => from ov in ovs
                    where ov.Customer.City == "Seattle"
                    select ov);
        }
    }
}