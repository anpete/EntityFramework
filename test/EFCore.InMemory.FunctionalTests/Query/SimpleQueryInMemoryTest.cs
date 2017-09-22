// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable RedundantOverridenMember
// ReSharper disable ConvertMethodToExpressionBody
namespace Microsoft.EntityFrameworkCore.Query
{
    public class SimpleQueryInMemoryTest : SimpleQueryTestBase<NorthwindQueryInMemoryFixture<NoopModelCustomizer>>
    {
        public SimpleQueryInMemoryTest(NorthwindQueryInMemoryFixture<NoopModelCustomizer> fixture,
            ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            //TestLoggerFactory.TestOutputHelper = testOutputHelper;
        }

        [Fact]
        public override void View_simple()
        {
            base.View_simple();
        }

        public override void View_where_simple()
        {
            base.View_where_simple();
        }

        public override void View_backed_by_view()
        {
            base.View_backed_by_view();
        }

        public override void View_with_nav()
        {
            base.View_with_nav();
        }

        public override void View_with_included_nav()
        {
            base.View_with_included_nav();
        }

        public override void View_with_included_navs_multi_level()
        {
            base.View_with_included_navs_multi_level();
        }

        [Fact(Skip = "See issue #9591")]
        public override void Select_Distinct_GroupBy()
        {
            base.Select_Distinct_GroupBy();
        }
    }
}