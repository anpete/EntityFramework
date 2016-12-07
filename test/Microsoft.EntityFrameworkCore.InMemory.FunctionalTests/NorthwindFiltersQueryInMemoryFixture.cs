// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Northwind;

// ReSharper disable StringStartsWithIsCultureSpecific
namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class NorthwindFiltersQueryInMemoryFixture : NorthwindQueryInMemoryFixture
    {
        public override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            FiltersTestBase<NorthwindFiltersQueryInMemoryFixture>.ConfigureModel(modelBuilder);
        }
    }
}
