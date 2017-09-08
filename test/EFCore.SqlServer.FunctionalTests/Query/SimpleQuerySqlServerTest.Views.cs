// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.EntityFrameworkCore.Query
{
    public partial class SimpleQuerySqlServerTest
    {
        public override void View_simple()
        {
            base.View_simple();

            AssertSql(
                @"SELECT [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle]
FROM [Customers] AS [c]");
        }

        public override void View_where_simple()
        {
            base.View_where_simple();

            AssertSql(
                @"SELECT [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle]
FROM [Customers] AS [c]
WHERE [c].[City] = N'London'");
        }

        public override void View_backed_by_view()
        {
            base.View_backed_by_view();

            AssertSql(
                @"SELECT [p].[CategoryName], [p].[ProductName], [p].[ProductSales]
FROM [Product Sales for 1997] AS [p]");
        }
    }
}
