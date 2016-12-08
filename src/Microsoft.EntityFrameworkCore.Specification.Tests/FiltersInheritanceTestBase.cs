// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Inheritance;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;
using Xunit;

// ReSharper disable StringStartsWithIsCultureSpecific
// ReSharper disable InconsistentNaming
// ReSharper disable ConvertToExpressionBodyWhenPossible
// ReSharper disable ConvertMethodToExpressionBody
namespace Microsoft.EntityFrameworkCore.Specification.Tests
{
    public abstract class FiltersInheritanceTestBase<TFixture> : IClassFixture<TFixture>, IDisposable
        where TFixture : InheritanceFixtureBase, new()
    {
        [ConditionalFact]
        public virtual void Count_query_leaf_class()
        {
            Assert.Equal(0, _context.Set<Kiwi>().Count());
        }
        
        [ConditionalFact]
        public virtual void Count_query_root_class()
        {
            Assert.Equal(0, _context.Set<Bird>().Select(a => a.Name).ToList().Count);
        }

        public static void ConfigureModel(ModelBuilder modelBuilder)
        {
//            Expression<Func<Kiwi, bool>> kiwiFilter = k => k.FoundOn == Island.North;
//
//            modelBuilder.Entity<Kiwi>().Metadata.Filter = kiwiFilter;
//            
//            Expression<Func<Animal, bool>> animalFilter = a => a.CountryId == 1;
//
//            modelBuilder.Entity<Animal>().Metadata.Filter = animalFilter;
        }

        private readonly InheritanceContext _context;

        protected FiltersInheritanceTestBase(TFixture fixture)
        {
            _context = fixture.CreateContext();
        }

        public void Dispose() => _context.Dispose();
    }
}
