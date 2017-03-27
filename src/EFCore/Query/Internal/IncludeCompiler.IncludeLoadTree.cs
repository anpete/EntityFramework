// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
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
                AddLoadPath(this, navigationPath, 0);
            }

            public Expression Compile(
                QueryCompilationContext queryCompilationContext,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                ICollection<Ordering> parentOrderings,
                ref int collectionIncludeId)
            {
                var entityParameter
                    = Expression.Parameter(QuerySourceReferenceExpression.Type, name: "entity");

                var propertyExpressions = new List<Expression>();
                var blockExpressions = new List<Expression>();

                if (trackingQuery)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            Expression.Property(
                                EntityQueryModelVisitor.QueryContextParameter,
                                nameof(QueryContext.QueryBuffer)),
                            _queryBufferStartTrackingMethodInfo,
                            entityParameter,
                            Expression.Constant(
                                queryCompilationContext.Model
                                    .FindEntityType(entityParameter.Type))));
                }

                var includedIndex = 0;

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var includeLoadTreeNode in Children)
                {
                    blockExpressions.Add(
                        includeLoadTreeNode.BuildIncludeExpressions(
                            queryCompilationContext,
                            queryModel,
                            trackingQuery,
                            asyncQuery,
                            QuerySourceReferenceExpression,
                            parentOrderings,
                            entityParameter,
                            propertyExpressions,
                            ref includedIndex,
                            ref collectionIncludeId));
                }

                Expression includeExpression = null;

                if (asyncQuery)
                {
                    var taskExpression = new List<Expression>();

                    foreach (var expression in blockExpressions.ToArray())
                    {
                        if (expression.Type == typeof(Task))
                        {
                            blockExpressions.Remove(expression);
                            taskExpression.Add(expression);
                        }
                    }

                    if (taskExpression.Count > 0)
                    {
                        blockExpressions.Add(
                            Expression.Call(
                                _awaitIncludesMethodInfo,
                                Expression.NewArrayInit(
                                    typeof(Func<Task>),
                                    taskExpression.Select(e => Expression.Lambda(e)))));

                        includeExpression
                            = Expression.Property(
                                Expression.Call(
                                    _includeAsyncMethodInfo.MakeGenericMethod(QuerySourceReferenceExpression.Type),
                                    EntityQueryModelVisitor.QueryContextParameter,
                                    QuerySourceReferenceExpression,
                                    Expression.NewArrayInit(typeof(object), propertyExpressions),
                                    Expression.Lambda(
                                        Expression.Block(blockExpressions),
                                        EntityQueryModelVisitor.QueryContextParameter,
                                        entityParameter,
                                        _includedParameter,
                                        _cancellationTokenParameter),
                                    _cancellationTokenParameter),
                                nameof(Task<object>.Result));
                    }
                }

                return includeExpression
                       ?? Expression.Call(
                           _includeMethodInfo.MakeGenericMethod(QuerySourceReferenceExpression.Type),
                           EntityQueryModelVisitor.QueryContextParameter,
                           QuerySourceReferenceExpression,
                           Expression.NewArrayInit(typeof(object), propertyExpressions),
                           Expression.Lambda(
                               Expression.Block(typeof(void), blockExpressions),
                               EntityQueryModelVisitor.QueryContextParameter,
                               entityParameter,
                               _includedParameter));
            }
        }
    }
}
