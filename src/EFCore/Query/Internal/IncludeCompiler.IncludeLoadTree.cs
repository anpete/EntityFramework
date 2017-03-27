// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public partial class IncludeCompiler
    {
        private sealed class IncludeLoadTree : IncludeLoadTreeNodeBase
        {
            public IncludeLoadTree(QuerySourceReferenceExpression querySourceReferenceExpression)
                => QuerySourceReferenceExpression = querySourceReferenceExpression;

            public QuerySourceReferenceExpression QuerySourceReferenceExpression { get; }

            public void AddLoadPath(IReadOnlyList<INavigation> navigationPath)
            {
                AddLoadPath(this, navigationPath, 0);
            }

            public void Compile(
                QueryCompilationContext queryCompilationContext,
                NavigationRewritingExpressionVisitor navigationRewritingExpressionVisitor,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                ICollection<Ordering> parentOrderings,
                ref int collectionIncludeId)
            {
                Compile(
                    queryCompilationContext,
                    navigationRewritingExpressionVisitor,
                    queryModel,
                    trackingQuery,
                    asyncQuery,
                    QuerySourceReferenceExpression,
                    parentOrderings,
                    ref collectionIncludeId);
            }
        }
    }
}
