// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

        [Fact(Skip = "#8907")]
        public override void Query_for_base_type_loads_all_owned_navs()
        {
            base.Query_for_base_type_loads_all_owned_navs();

            AssertSql("");
        }

        [Fact(Skip = "#8907")]
        public override void Query_for_branch_type_loads_all_owned_navs()
        {
            base.Query_for_branch_type_loads_all_owned_navs();

            AssertSql("");
        }

        [Fact(Skip = "#8907")]
        public override void Query_for_leaf_type_loads_all_owned_navs()
        {
            base.Query_for_leaf_type_loads_all_owned_navs();

            AssertSql("");
        }

        [Fact(Skip = "#8907")]
        public override void Query_when_group_by()
        {
            base.Query_when_group_by();
        }

        [Fact(Skip = "#8907")]
        public override void Query_when_subquery()
        {
            base.Query_when_subquery();
        }

        protected override DbContext CreateContext() => _fixture.CreateContext();

        private void AssertSql(params string[] expected)
            => _fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
