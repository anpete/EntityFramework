// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public partial class IncludeCompiler
    {
        private abstract class IncludeLoadTreeNodeBase
        {
            protected static void AddLoadPath(
                IncludeLoadTreeNodeBase node,
                IReadOnlyList<INavigation> navigationPath,
                int index)
            {
                while (index < navigationPath.Count)
                {
                    var navigation = navigationPath[index];
                    var childNode = node.Children.SingleOrDefault(n => n.Navigation == navigation);

                    if (childNode == null)
                    {
                        node.Children.Add(childNode = new IncludeLoadTreeNode(navigation));
                    }

                    node = childNode;
                    index = index + 1;
                }
            }

            protected ICollection<IncludeLoadTreeNode> Children { get; } = new List<IncludeLoadTreeNode>();

            protected void Compile(
                QueryCompilationContext queryCompilationContext,
                NavigationRewritingExpressionVisitor navigationRewritingExpressionVisitor,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                QuerySourceReferenceExpression parentQuerySourceReferenceExpression,
                ICollection<Ordering> parentOrderings,
                ref int collectionIncludeId)
            {
                var entityParameterExpression
                    = Expression.Parameter(parentQuerySourceReferenceExpression.Type, name: "entity");

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
                            entityParameterExpression,
                            Expression.Constant(
                                queryCompilationContext.Model
                                    .FindEntityType(entityParameterExpression.Type))));
                }

                var includedIndex = 0;

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var includeLoadTreeNode in Children)
                {
                    blockExpressions.Add(
                        includeLoadTreeNode.BuildIncludeExpressions(
                            queryCompilationContext,
                            navigationRewritingExpressionVisitor,
                            queryModel,
                            trackingQuery,
                            asyncQuery,
                            parentQuerySourceReferenceExpression,
                            parentOrderings,
                            entityParameterExpression,
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
                                    _includeAsyncMethodInfo.MakeGenericMethod(parentQuerySourceReferenceExpression.Type),
                                    EntityQueryModelVisitor.QueryContextParameter,
                                    parentQuerySourceReferenceExpression,
                                    Expression.NewArrayInit(typeof(object), propertyExpressions),
                                    Expression.Lambda(
                                        Expression.Block(blockExpressions),
                                        EntityQueryModelVisitor.QueryContextParameter,
                                        entityParameterExpression,
                                        _includedParameter,
                                        _cancellationTokenParameter),
                                    _cancellationTokenParameter),
                                nameof(Task<object>.Result));
                    }
                }

                includeExpression
                    = includeExpression
                      ?? Expression.Call(
                          _includeMethodInfo.MakeGenericMethod(parentQuerySourceReferenceExpression.Type),
                          EntityQueryModelVisitor.QueryContextParameter,
                          parentQuerySourceReferenceExpression,
                          Expression.NewArrayInit(typeof(object), propertyExpressions),
                          Expression.Lambda(
                              Expression.Block(typeof(void), blockExpressions),
                              EntityQueryModelVisitor.QueryContextParameter,
                              entityParameterExpression,
                              _includedParameter));

                ApplyIncludeExpressionsToQueryModel(queryModel, includeExpression, parentQuerySourceReferenceExpression);
            }

            private static void ApplyIncludeExpressionsToQueryModel(
                QueryModel queryModel, Expression expression, QuerySourceReferenceExpression querySourceReferenceExpression)
            {
                var includeReplacingExpressionVisitor = new IncludeReplacingExpressionVisitor();

                queryModel.SelectClause.TransformExpressions(
                    e => includeReplacingExpressionVisitor.Replace(
                        querySourceReferenceExpression,
                        expression,
                        e));

                foreach (var groupResultOperator
                    in queryModel.ResultOperators.OfType<GroupResultOperator>())
                {
                    groupResultOperator.ElementSelector
                        = includeReplacingExpressionVisitor.Replace(
                            querySourceReferenceExpression,
                            expression,
                            groupResultOperator.ElementSelector);
                }
            }

            private class IncludeReplacingExpressionVisitor : RelinqExpressionVisitor
            {
                private QuerySourceReferenceExpression _querySourceReferenceExpression;
                private Expression _includeExpression;

                public Expression Replace(
                    QuerySourceReferenceExpression querySourceReferenceExpression,
                    Expression includeExpression,
                    Expression searchedExpression)
                {
                    _querySourceReferenceExpression = querySourceReferenceExpression;
                    _includeExpression = includeExpression;

                    return Visit(searchedExpression);
                }

                protected override Expression VisitQuerySourceReference(
                    QuerySourceReferenceExpression querySourceReferenceExpression)
                {
                    if (ReferenceEquals(querySourceReferenceExpression, _querySourceReferenceExpression))
                    {
                        _querySourceReferenceExpression = null;

                        return _includeExpression;
                    }

                    return querySourceReferenceExpression;
                }
            }
        }
    }
}
