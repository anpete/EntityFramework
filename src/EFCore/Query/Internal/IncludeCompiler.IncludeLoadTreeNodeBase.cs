﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata;
using Remotion.Linq;
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
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                ref int collectionIncludeId,
                QuerySourceReferenceExpression targetQuerySourceReferenceExpression)
            {
                var entityParameter
                    = Expression.Parameter(targetQuerySourceReferenceExpression.Type, name: "entity");

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

                foreach (var includeLoadTreeNode in Children)
                {
                    includeLoadTreeNode.Compile(
                        queryCompilationContext,
                        targetQuerySourceReferenceExpression,
                        entityParameter,
                        propertyExpressions,
                        blockExpressions,
                        trackingQuery,
                        asyncQuery,
                        ref includedIndex,
                        ref collectionIncludeId);
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
                                    _includeAsyncMethodInfo.MakeGenericMethod(targetQuerySourceReferenceExpression.Type),
                                    EntityQueryModelVisitor.QueryContextParameter,
                                    targetQuerySourceReferenceExpression,
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

                if (includeExpression == null)
                {
                    includeExpression
                        = Expression.Call(
                            _includeMethodInfo.MakeGenericMethod(targetQuerySourceReferenceExpression.Type),
                            EntityQueryModelVisitor.QueryContextParameter,
                            targetQuerySourceReferenceExpression,
                            Expression.NewArrayInit(typeof(object), propertyExpressions),
                            Expression.Lambda(
                                Expression.Block(typeof(void), blockExpressions),
                                EntityQueryModelVisitor.QueryContextParameter,
                                entityParameter,
                                _includedParameter));
                }

                ApplyIncludeExpressionsToQueryModel(queryModel, targetQuerySourceReferenceExpression, includeExpression);
            }

            private static void ApplyIncludeExpressionsToQueryModel(
                QueryModel queryModel,
                QuerySourceReferenceExpression querySourceReferenceExpression,
                Expression expression)
            {
                var includeReplacingExpressionVisitor = new IncludeReplacingExpressionVisitor();

                foreach (var groupResultOperator
                    in queryModel.ResultOperators.OfType<GroupResultOperator>())
                {
                    var newElementSelector
                        = includeReplacingExpressionVisitor.Replace(
                            querySourceReferenceExpression,
                            expression,
                            groupResultOperator.ElementSelector);

                    if (!ReferenceEquals(newElementSelector, groupResultOperator.ElementSelector))
                    {
                        groupResultOperator.ElementSelector = newElementSelector;

                        return;
                    }
                }

                queryModel.SelectClause.TransformExpressions(
                    e => includeReplacingExpressionVisitor.Replace(
                        querySourceReferenceExpression,
                        expression,
                        e));
            }

            private static readonly MethodInfo _awaitIncludesMethodInfo
                = typeof(IncludeLoadTree).GetTypeInfo()
                    .GetDeclaredMethod(nameof(_AwaitIncludes));

            // ReSharper disable once InconsistentNaming
            private static async Task _AwaitIncludes(IReadOnlyList<Func<Task>> taskFactories)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < taskFactories.Count; i++)
                {
                    await taskFactories[i]();
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
