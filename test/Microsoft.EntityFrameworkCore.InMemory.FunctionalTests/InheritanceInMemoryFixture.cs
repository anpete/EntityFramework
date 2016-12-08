// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestModels.Inheritance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class InheritanceInMemoryFixture : InheritanceFixtureBase
    {
        private readonly DbContextOptionsBuilder _optionsBuilder = new DbContextOptionsBuilder();

        private readonly TestLoggerFactory _testLoggerFactory = new TestLoggerFactory();

        public InheritanceInMemoryFixture()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .AddSingleton<ILoggerFactory>(_testLoggerFactory)
                .AddSingleton(TestInMemoryModelSource.GetFactory(OnModelCreating))
                .BuildServiceProvider();

            _optionsBuilder.UseInMemoryDatabase().UseInternalServiceProvider(serviceProvider);

            using (var context = CreateContext())
            {
                SeedData(context);
            }
        }

        public sealed override InheritanceContext CreateContext()
            => new InheritanceContext(_optionsBuilder.Options);
    }
}
