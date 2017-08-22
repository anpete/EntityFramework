// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Linq;

namespace Microsoft.EntityFrameworkCore.Query
{
    public abstract class NorthwindQueryFixtureBase<TModelCustomizer> : SharedStoreFixtureBase<NorthwindContext>, IQueryFixtureBase
        where TModelCustomizer : IModelCustomizer, new()
    {
        protected NorthwindQueryFixtureBase()
        {
            var entitySorters = new Dictionary<Type, Func<dynamic, object>>
            {
                { typeof(Customer), e => e.CustomerID },
                { typeof(CustomerView), e => e.CustomerID },
                { typeof(Order), e => e.OrderID },
                { typeof(Employee), e => e.EmployeeID },
                { typeof(Product), e => e.ProductID },
                { typeof(OrderDetail), e => e.OrderID.ToString() + " " + e.ProductID.ToString() }
            };

            var entityAsserters = new Dictionary<Type, Action<dynamic, dynamic>>();

            QueryAsserter = new QueryAsserter<NorthwindContext>(
                CreateContext,
                new NorthwindData(),
                entitySorters,
                entityAsserters);

            ViewQueryAsserter = new QueryAsserter<NorthwindContext>(
                CreateContext,
                new NorthwindData(),
                entitySorters,
                entityAsserters);

            ViewQueryAsserter.SetExtractor = new ViewExtractor();
        }

        private class ViewExtractor : ISetExtractor
        {
            public override IQueryable<TView> Set<TView>(DbContext context)
                => context.View<TView>();
        }

        protected override string StoreName { get; } = "Northwind";

        protected override bool UsePooling => false;

        public QueryAsserterBase QueryAsserter { get; set; }
        public QueryAsserterBase ViewQueryAsserter { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
            => new TModelCustomizer().Customize(modelBuilder, context);

        protected override void Seed(NorthwindContext context) => NorthwindData.Seed(context);

        public override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
            => base.AddOptions(builder).ConfigureWarnings(
                c => c
                    .Log(CoreEventId.RowLimitingOperationWithoutOrderByWarning)
                    .Log(CoreEventId.FirstWithoutOrderByAndFilterWarning)
                    .Log(CoreEventId.PossibleUnintendedCollectionNavigationNullComparisonWarning)
                    .Log(CoreEventId.PossibleUnintendedReferenceComparisonWarning));
    }
}
