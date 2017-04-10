// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Specification.Tests;
using Microsoft.EntityFrameworkCore.Specification.Tests.TestUtilities.Xunit;

namespace Microsoft.EntityFrameworkCore.Sqlite.FunctionalTests
{
    public class ComplexNavigationsQuerySqliteTest : ComplexNavigationsQueryTestBase<SqliteTestStore, ComplexNavigationsQuerySqliteFixture>
    {
        public ComplexNavigationsQuerySqliteTest(ComplexNavigationsQuerySqliteFixture fixture)
            : base(fixture)
        {
        }

        [ConditionalFact(Skip = "Bug in projection shaper. See Issue #8095")]
        public override void Multiple_complex_includes()
        {
            base.Multiple_complex_includes();
        }

        [ConditionalFact(Skip = "Bug in projection shaper. See Issue #8095")]
        public override void Multiple_complex_includes_self_ref()
        {
            base.Multiple_complex_includes_self_ref();
        }

        [ConditionalFact(Skip = "Bug in projection shaper. See Issue #8095")]
        public override void Multiple_complex_include_select()
        {
            base.Multiple_complex_include_select();
        }

        [ConditionalFact(Skip = "Bug in projection shaper. See Issue #8095")]
        public override void Include_with_groupjoin_skip_and_take()
        {
            base.Include_with_groupjoin_skip_and_take();
        }
    }
}
