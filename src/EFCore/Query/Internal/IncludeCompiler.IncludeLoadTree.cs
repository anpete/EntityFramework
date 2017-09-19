// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Metadata;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public partial class IncludeCompiler
    {
        private sealed class IncludeLoadTree : IncludeLoadTreeNodeBase
        {
            private QueryModel _queryModel;

            public IncludeLoadTree(
                QuerySourceReferenceExpression querySourceReferenceExpression,
                QueryModel queryModel)
            {
                QuerySourceReferenceExpression = querySourceReferenceExpression;

                _queryModel = queryModel;
            }

            public QuerySourceReferenceExpression QuerySourceReferenceExpression { get; }

            public void AddLoadPath(IReadOnlyList<INavigation> navigationPath)
            {
                AddLoadPath(this, navigationPath, index: 0);
            }

            public void Compile(
                QueryCompilationContext queryCompilationContext,
                bool trackingQuery,
                bool asyncQuery,
                ref int collectionIncludeId)
            {
                var querySourceReferenceExpression = QuerySourceReferenceExpression;

                if (querySourceReferenceExpression.ReferencedQuerySource is GroupJoinClause groupJoinClause)
                {
                    // We expand GJs to: 'from e in [g] select e' so we can rewrite the projector

                    var joinClause = groupJoinClause.JoinClause;

                    var mainFromClause
                        = new MainFromClause(joinClause.ItemName, joinClause.ItemType, QuerySourceReferenceExpression);

                    querySourceReferenceExpression = new QuerySourceReferenceExpression(mainFromClause);

                    var subQueryModel
                        = new QueryModel(
                            mainFromClause,
                            new SelectClause(querySourceReferenceExpression));

                    ApplyIncludeExpressionsToQueryModel(
                        _queryModel, QuerySourceReferenceExpression, new SubQueryExpression(subQueryModel));

                    _queryModel = subQueryModel;
                }

                Compile(
                    queryCompilationContext,
                    _queryModel,
                    trackingQuery,
                    asyncQuery,
                    ref collectionIncludeId,
                    querySourceReferenceExpression);
            }
        }
    }
}
