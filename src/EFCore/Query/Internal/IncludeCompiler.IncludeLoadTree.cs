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
            public IncludeLoadTree(QuerySourceReferenceExpression querySourceReferenceExpression)
                => QuerySourceReferenceExpression = querySourceReferenceExpression;

            public QuerySourceReferenceExpression QuerySourceReferenceExpression { get; }

            public void AddLoadPath(IReadOnlyList<INavigation> navigationPath)
            {
                AddLoadPath(this, navigationPath, index: 0);
            }

            public void Compile(
                QueryCompilationContext queryCompilationContext,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                ref int collectionIncludeId)
            {
                var querySourceReferenceExpression = QuerySourceReferenceExpression;

                if (querySourceReferenceExpression.ReferencedQuerySource is GroupJoinClause groupJoinClause)
                {
                    var joinClause = groupJoinClause.JoinClause;

                    queryModel = (joinClause.InnerSequence as SubQueryExpression)?.QueryModel;

                    if (queryModel == null)
                    {
                        var mainFromClause
                            = new MainFromClause(joinClause.ItemName, joinClause.ItemType, joinClause.InnerSequence);

                        querySourceReferenceExpression = new QuerySourceReferenceExpression(mainFromClause);

                        queryModel
                            = new QueryModel(
                                mainFromClause,
                                new SelectClause(querySourceReferenceExpression));

                        groupJoinClause.JoinClause.InnerSequence = new SubQueryExpression(queryModel);
                    }
                    else
                    {
                        querySourceReferenceExpression
                            = (QuerySourceReferenceExpression)queryModel.SelectClause.Selector;
                    }
                }

                Compile(
                    queryCompilationContext,
                    queryModel,
                    trackingQuery,
                    asyncQuery,
                    ref collectionIncludeId,
                    querySourceReferenceExpression);
            }
        }
    }
}
