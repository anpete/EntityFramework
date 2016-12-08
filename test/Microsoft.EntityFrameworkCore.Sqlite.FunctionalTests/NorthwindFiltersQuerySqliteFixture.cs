// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Specification.Tests;

// ReSharper disable StringStartsWithIsCultureSpecific
namespace Microsoft.EntityFrameworkCore.Sqlite.FunctionalTests
{
    public class NorthwindFiltersQuerySqliteFixture : NorthwindQuerySqliteFixture
    {
        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            FiltersTestBase<NorthwindFiltersQuerySqliteFixture>.ConfigureModel(modelBuilder);
        }
    }
}
