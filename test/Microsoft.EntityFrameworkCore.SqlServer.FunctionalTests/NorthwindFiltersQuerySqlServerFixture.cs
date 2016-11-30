// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Northwind;

// ReSharper disable StringStartsWithIsCultureSpecific
namespace Microsoft.EntityFrameworkCore.SqlServer.FunctionalTests
{
    public class NorthwindFiltersQuerySqlServerFixture : NorthwindQuerySqlServerFixture
    {
        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            Expression<Func<Customer, bool>> filter = c => c.CompanyName.StartsWith("B");

            modelBuilder.Entity<Customer>().Metadata.Filter = filter;
        }
    }
}
