// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.FunctionalTests;

namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class QueryNavigationsInMemoryTest : QueryNavigationsTestBase<NorthwindQueryInMemoryFixture>
    {
        public override void Select_Where_Navigation_Null()
        {
            base.Select_Where_Navigation_Null();
        }

        public QueryNavigationsInMemoryTest(NorthwindQueryInMemoryFixture fixture)
            : base(fixture)
        {
        }
    }
}
