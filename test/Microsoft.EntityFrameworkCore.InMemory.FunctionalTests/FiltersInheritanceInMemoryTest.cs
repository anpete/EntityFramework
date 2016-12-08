// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Specification.Tests;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class FiltersInheritanceInMemoryTest : FiltersInheritanceTestBase<FiltersInheritanceInMemoryFixture>
    {
        public FiltersInheritanceInMemoryTest(FiltersInheritanceInMemoryFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestLoggerFactory.TestOutputHelper = testOutputHelper;
        }

        public override void Count_query_root_class()
        {
            base.Count_query_root_class();
        }
    }
}
