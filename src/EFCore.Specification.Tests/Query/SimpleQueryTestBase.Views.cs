// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;

// ReSharper disable InconsistentNaming

namespace Microsoft.EntityFrameworkCore.Query
{
    // ReSharper disable once UnusedTypeParameter
    public abstract partial class SimpleQueryTestBase<TFixture>
    {
        // TODO:
        // - Inheritance
        // - Outbound navs
        // - InMemory
        // - structs
        // - DbView props on context?
        // - Mixed tracking

        [ConditionalFact]
        public virtual void View_simple()
        {
            AssertQuery<CustomerView>(cs => cs);
        }

        [ConditionalFact]
        public virtual void View_where_simple()
        {
            AssertQuery<CustomerView>(
                cs => cs.Where(c => c.City == "London"));
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
    }
}
