// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query
{
    public class OwnedQuerySqlServerTest : OwnedQueryTestBase, IClassFixture<OwnedQuerySqlServerFixture>
    {
        private readonly OwnedQuerySqlServerFixture _fixture;

        public OwnedQuerySqlServerTest(OwnedQuerySqlServerFixture fixture)
        {
            _fixture = fixture;
        }

        public override void Query_for_base_type_loads_all_owned_navs()
        {
            base.Query_for_base_type_loads_all_owned_navs();

            AssertSql("");
        }

        protected override DbContext CreateContext() => _fixture.CreateContext();

        private void AssertSql(params string[] expected)
            => _fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
